using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NobodyFalls.Api.Tests.Support;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Api.Tests;

/// <summary>Ideas, requests to the team and reports, as players use them (GAME_DESIGN §8).</summary>
public sealed class FeedbackTests(PostgresFixture postgres)
{
    private static object Idea(string title = "Une carte du filet", string category = "gameplay", string token = FakeHumanVerifier.Valid) => new
    {
        title,
        body = "Voir qui tient qui, à l'échelle du monde.",
        category,
        turnstileToken = token,
    };

    [Fact]
    public async Task Players_propose_ideas_and_vote_for_others()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, _) = await app.SignUpAsync("lea");
        var (tom, _) = await app.SignUpAsync("tom");

        var created = await lea.PostAsJsonAsync("/api/v1/ideas", Idea());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await Json(created)).GetProperty("id").GetInt64();

        var own = await lea.PutAsync(new Uri($"/api/v1/ideas/{id}/vote", UriKind.Relative), null);
        Assert.Equal("idea.own", (await Json(own)).GetProperty("code").GetString());

        var vote = await Json(await tom.PutAsync(new Uri($"/api/v1/ideas/{id}/vote", UriKind.Relative), null));
        Assert.Equal(1, vote.GetProperty("votes").GetInt32());
        // Voting twice changes nothing.
        vote = await Json(await tom.PutAsync(new Uri($"/api/v1/ideas/{id}/vote", UriKind.Relative), null));
        Assert.Equal(1, vote.GetProperty("votes").GetInt32());

        var seenByTom = await Json(await tom.GetAsync(new Uri($"/api/v1/ideas/{id}", UriKind.Relative)));
        Assert.True(seenByTom.GetProperty("voted").GetBoolean());
        Assert.False(seenByTom.GetProperty("mine").GetBoolean());
        Assert.Equal("lea", seenByTom.GetProperty("authorPseudo").GetString());
        Assert.Equal("new", seenByTom.GetProperty("status").GetString());

        var removed = await Json(await tom.DeleteAsync(new Uri($"/api/v1/ideas/{id}/vote", UriKind.Relative)));
        Assert.Equal(0, removed.GetProperty("votes").GetInt32());
        Assert.False(removed.GetProperty("voted").GetBoolean());
    }

    [Fact]
    public async Task Ideas_are_checked_verified_and_limited_to_three_a_day()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, _) = await app.SignUpAsync("lea");

        Assert.Equal("idea.title", await CodeAsync(await lea.PostAsJsonAsync("/api/v1/ideas", Idea(title: "Hi"))));
        Assert.Equal("idea.category", await CodeAsync(await lea.PostAsJsonAsync("/api/v1/ideas", Idea(category: "money"))));
        Assert.Equal(HttpStatusCode.Forbidden, (await lea.PostAsJsonAsync("/api/v1/ideas", Idea(token: "bot"))).StatusCode);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await lea.PostAsJsonAsync("/api/v1/ideas", Idea($"Idée numéro {i}"))).StatusCode);
        }

        var fourth = await lea.PostAsJsonAsync("/api/v1/ideas", Idea("Encore une idée"));
        Assert.Equal(HttpStatusCode.Conflict, fourth.StatusCode);
        Assert.Equal("idea.limit_reached", await CodeAsync(fourth));
    }

    [Fact]
    public async Task The_list_shows_my_language_first_then_the_most_voted()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, _) = await app.SignUpAsync("lea");
        var (tom, _) = await app.SignUpAsync("tom");
        var (ana, _) = await app.SignUpAsync("ana");
        await app.Seed.ExecAsync("update players set locale = 'en' where pseudo = 'ana'");

        var quiet = await IdAsync(await lea.PostAsJsonAsync("/api/v1/ideas", Idea("Idée calme")));
        var popular = await IdAsync(await lea.PostAsJsonAsync("/api/v1/ideas", Idea("Idée populaire")));
        await IdAsync(await ana.PostAsJsonAsync("/api/v1/ideas", Idea("An English idea")));
        await tom.PutAsync(new Uri($"/api/v1/ideas/{popular}/vote", UriKind.Relative), null);

        var forTom = await Json(await tom.GetAsync(new Uri("/api/v1/ideas", UriKind.Relative)));
        var titles = forTom.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()).ToList();
        Assert.Equal(["Idée populaire", "Idée calme", "An English idea"], titles);

        var newest = await Json(await tom.GetAsync(new Uri("/api/v1/ideas?sort=new&limit=1", UriKind.Relative)));
        Assert.Equal(popular, newest.GetProperty("items")[0].GetProperty("id").GetInt64());
        Assert.Equal(1, newest.GetProperty("nextOffset").GetInt32());
        Assert.NotEqual(quiet, popular);
    }

    [Fact]
    public async Task Hidden_ideas_are_visible_to_their_author_only_and_take_no_votes()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, _) = await app.SignUpAsync("lea");
        var (tom, _) = await app.SignUpAsync("tom");
        var id = await IdAsync(await lea.PostAsJsonAsync("/api/v1/ideas", Idea()));
        await app.Seed.ExecAsync("update ideas set hidden_at = now() where id = @id", ("id", id));

        Assert.Equal(HttpStatusCode.NotFound, (await tom.GetAsync(new Uri($"/api/v1/ideas/{id}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tom.PutAsync(new Uri($"/api/v1/ideas/{id}/vote", UriKind.Relative), null)).StatusCode);
        Assert.True((await Json(await lea.GetAsync(new Uri($"/api/v1/ideas/{id}", UriKind.Relative)))).GetProperty("hidden").GetBoolean());
        Assert.Empty((await Json(await tom.GetAsync(new Uri("/api/v1/ideas", UriKind.Relative)))).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_request_is_a_conversation_without_the_teams_internal_notes()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, leaId) = await app.SignUpAsync("lea");
        var (tom, _) = await app.SignUpAsync("tom");

        var id = await IdAsync(await lea.PostAsJsonAsync("/api/v1/tickets", new { category = "support", subject = "Mes mains", message = "Je ne vois pas mes mains." }));
        // The team answers, and writes a note to itself.
        await app.Seed.ExecAsync(
            """
            insert into ticket_messages (ticket_id, author_kind, body, internal, created_at) values
              (@id, 'staff', 'Note : vérifier le cache.', true, now()),
              (@id, 'staff', 'Peux-tu recharger la page ?', false, now());
            update tickets set status = 'waiting_player' where id = @id;
            """,
            ("id", id));

        var list = await Json(await lea.GetAsync(new Uri("/api/v1/tickets", UriKind.Relative)));
        Assert.True(list[0].GetProperty("answerWaiting").GetBoolean());

        var ticket = await Json(await lea.GetAsync(new Uri($"/api/v1/tickets/{id}", UriKind.Relative)));
        var messages = ticket.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(["player", "team"], messages.Select(m => m.GetProperty("from").GetString()));
        Assert.DoesNotContain(messages, m => m.GetProperty("body").GetString()!.Contains("Note", StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.NoContent, (await lea.PostAsJsonAsync($"/api/v1/tickets/{id}/messages", new { message = "Merci, ça marche." })).StatusCode);
        Assert.Equal("open", (await Json(await lea.GetAsync(new Uri($"/api/v1/tickets/{id}", UriKind.Relative)))).GetProperty("status").GetString());

        // Someone else's request does not exist for them.
        Assert.Equal(HttpStatusCode.NotFound, (await tom.GetAsync(new Uri($"/api/v1/tickets/{id}", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tom.PostAsJsonAsync($"/api/v1/tickets/{id}/messages", new { message = "Coucou" })).StatusCode);

        await app.Seed.ExecAsync("update tickets set status = 'closed' where id = @id", ("id", id));
        Assert.Equal("ticket.closed", await CodeAsync(await lea.PostAsJsonAsync($"/api/v1/tickets/{id}/messages", new { message = "Encore moi" })));
        Assert.NotEqual(Guid.Empty, leaId);
    }

    [Fact]
    public async Task Reports_reach_the_queue_with_a_snapshot_once_per_target()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, leaId) = await app.SignUpAsync("lea");
        var (_, tomId) = await app.SignUpAsync("tom");

        var byPseudo = await lea.PostAsJsonAsync("/api/v1/reports", new { target = "player", pseudo = "TOM", category = "pseudo", details = "Pseudo injurieux.", turnstileToken = FakeHumanVerifier.Valid });
        Assert.Equal(HttpStatusCode.Created, byPseudo.StatusCode);

        var again = await lea.PostAsJsonAsync("/api/v1/reports", new { target = "player", playerId = tomId, category = "harassment", turnstileToken = FakeHumanVerifier.Valid });
        Assert.Equal("report.already_open", await CodeAsync(again));

        var self = await lea.PostAsJsonAsync("/api/v1/reports", new { target = "player", playerId = leaId, category = "other", turnstileToken = FakeHumanVerifier.Valid });
        Assert.Equal("report.self", await CodeAsync(self));

        var nobody = await lea.PostAsJsonAsync("/api/v1/reports", new { target = "player", pseudo = "ghost", category = "other", turnstileToken = FakeHumanVerifier.Valid });
        Assert.Equal(HttpStatusCode.NotFound, nobody.StatusCode);

        var snapshot = await app.Seed.ScalarAsync<string>("select target_snapshot::text from reports where target_player_id = @id", ("id", tomId));
        Assert.Contains("\"pseudo\": \"tom\"", snapshot, StringComparison.Ordinal);
        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from reports"));
    }

    [Fact]
    public async Task An_idea_can_be_reported_and_its_author_is_recorded()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, leaId) = await app.SignUpAsync("lea");
        var (tom, _) = await app.SignUpAsync("tom");
        var id = await IdAsync(await lea.PostAsJsonAsync("/api/v1/ideas", Idea()));

        var report = await tom.PostAsJsonAsync("/api/v1/reports", new { target = "idea", ideaId = id, category = "other", details = "Hors sujet", turnstileToken = FakeHumanVerifier.Valid });

        Assert.Equal(HttpStatusCode.Created, report.StatusCode);
        Assert.Equal(leaId, await app.Seed.ScalarAsync<Guid>("select target_player_id from reports"));
        Assert.Equal("report.self", await CodeAsync(await lea.PostAsJsonAsync("/api/v1/reports", new { target = "idea", ideaId = id, category = "other", turnstileToken = FakeHumanVerifier.Valid })));
    }

    private static async Task<long> IdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("id").GetInt64();
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await Json(response)).GetProperty("code").GetString();

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
