using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NobodyFalls.Api.Tests.Support;
using NobodyFalls.Domain.Staff;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Api.Tests.Admin;

/// <summary>Moderation end to end through the API (GAME_DESIGN §9): reports, sanctions and their effects, requests, ideas.</summary>
public sealed class ModerationTests(PostgresFixture postgres)
{
    private const string Admin = StaffSupport.Admin;

    [Fact]
    public async Task A_moderator_handles_a_report_from_queue_to_sanction_and_every_step_is_audited()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (witness, _) = await app.SignUpAsync("witness");
        var (troll, trollId) = await app.SignUpAsync("troll");
        var mod = await app.StaffAsync("modo", [StaffRole.Moderator]);
        await witness.PostAsJsonAsync("/api/v1/reports", new { target = "player", pseudo = "troll", category = "harassment", details = "Il insulte.", turnstileToken = FakeHumanVerifier.Valid });

        var queue = await Json(await mod.Client.GetAsync(new Uri($"{Admin}/reports?status=open", UriKind.Relative)));
        var report = queue.GetProperty("items")[0];
        var id = report.GetProperty("id").GetInt64();
        Assert.Equal("troll", report.GetProperty("targetLabel").GetString());
        Assert.Equal("witness", report.GetProperty("reporterPseudo").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await mod.Client.PostAsJsonAsync($"{Admin}/reports/{id}/assign", new { toMe = true })).StatusCode);
        var detail = await Json(await mod.Client.GetAsync(new Uri($"{Admin}/reports/{id}", UriKind.Relative)));
        Assert.Equal("in_review", detail.GetProperty("summary").GetProperty("status").GetString());
        Assert.Equal("Il insulte.", detail.GetProperty("details").GetString());

        var resolved = await mod.Client.PostAsJsonAsync($"{Admin}/reports/{id}/resolve", new
        {
            outcome = "actioned",
            note = "Insultes confirmées.",
            sanction = new { kind = "ban", reason = "Harcèlement", publicMessage = "Tu es banni trois jours.", endsAt = app.Clock.UtcNow.AddDays(3) },
        });
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);

        // The troll is out: sessions ended, then signed in again, only notices and appeals remain.
        Assert.Equal(HttpStatusCode.Unauthorized, (await troll.GetAsync(new Uri("/api/v1/tonight", UriKind.Relative))).StatusCode);
        await ApiApp.DevLoginAsync(troll, "troll");
        var tonight = await troll.GetAsync(new Uri("/api/v1/tonight", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, tonight.StatusCode);
        Assert.Equal("player.banned", (await Json(tonight)).GetProperty("code").GetString());
        var notices = await Json(await troll.GetAsync(new Uri("/api/v1/me/notices", UriKind.Relative)));
        Assert.Equal("Tu es banni trois jours.", notices.GetProperty("ban").GetProperty("message").GetString());
        Assert.Equal("player.banned", (await Json(await troll.PostAsJsonAsync("/api/v1/tickets", new { category = "support", subject = "Pourquoi", message = "?" }))).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Created, (await troll.PostAsJsonAsync("/api/v1/tickets", new { category = "appeal", subject = "Je conteste", message = "Ce n'était pas moi." })).StatusCode);

        // Not counted among the living, not in play today.
        Assert.Equal(0L, await app.Seed.ScalarAsync<long>("select count(*) from player_days where player_id = @id", ("id", trollId)));

        // The reporter is told the report was handled, nothing more.
        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from notification_outbox where kind = 'report.handled'"));

        var audit = await Json(await (await app.StaffAsync("auditor", [StaffRole.Admin])).Client.GetAsync(new Uri($"{Admin}/audit?limit=200", UriKind.Relative)));
        var actions = audit.GetProperty("entries").EnumerateArray()
            .Where(e => e.GetProperty("actorPseudo").GetString() == "modo")
            .Select(e => e.GetProperty("action").GetString())
            .Reverse()
            .ToList();
        Assert.Equal(["admin.totp_enrolled", "report.assigned", "sanction.issued", "report.resolved"], actions);
    }

    [Fact]
    public async Task Moderators_ban_for_a_while_only_and_a_permanent_ban_wants_a_fresh_code()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (_, targetId) = await app.SignUpAsync("cheater");
        var mod = await app.StaffAsync("modo", [StaffRole.Moderator]);
        var boss = await app.StaffAsync("boss", [StaffRole.Admin]);
        var forever = new { kind = "ban", reason = "Triche répétée", publicMessage = "Banni définitivement." };

        var refused = await mod.Client.PostAsJsonAsync($"{Admin}/players/{targetId}/sanctions", forever);
        Assert.Equal("admin.forbidden", (await Json(refused)).GetProperty("code").GetString());

        app.Time.Advance(TimeSpan.FromMinutes(6));
        var stale = await boss.Client.PostAsJsonAsync($"{Admin}/players/{targetId}/sanctions", forever);
        Assert.Equal("admin.reauth_required", (await Json(stale)).GetProperty("code").GetString());

        await boss.ReauthAsync();
        Assert.Equal(HttpStatusCode.OK, (await boss.Client.PostAsJsonAsync($"{Admin}/players/{targetId}/sanctions", forever)).StatusCode);
        var sheet = await Json(await boss.Client.GetAsync(new Uri($"{Admin}/players/{targetId}", UriKind.Relative)));
        Assert.True(sheet.GetProperty("bannedUntil").GetDateTimeOffset().Year > 9000);
        Assert.Equal("boss", sheet.GetProperty("sanctions")[0].GetProperty("issuedBy").GetString());
    }

    [Fact]
    public async Task Revoking_the_ban_lets_the_player_back_in()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (player, playerId) = await app.SignUpAsync("lucky");
        var mod = await app.StaffAsync("modo", [StaffRole.Moderator]);
        var issued = await Json(await mod.Client.PostAsJsonAsync($"{Admin}/players/{playerId}/sanctions", new { kind = "ban", reason = "Erreur", publicMessage = "Banni.", endsAt = app.Clock.UtcNow.AddDays(1) }));

        Assert.Equal(HttpStatusCode.NoContent, (await mod.Client.PostAsJsonAsync($"{Admin}/sanctions/{issued.GetProperty("id").GetInt64()}/revoke", new { reason = "Mauvaise personne" })).StatusCode);

        await ApiApp.DevLoginAsync(player, "lucky");
        Assert.Equal(HttpStatusCode.OK, (await player.GetAsync(new Uri("/api/v1/me/profile", UriKind.Relative))).StatusCode);
        Assert.False((await Json(await player.GetAsync(new Uri("/api/v1/me/notices", UriKind.Relative)))).TryGetProperty("ban", out var ban) && ban.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task Warnings_are_acknowledged_and_a_reset_pseudo_is_chosen_again()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (player, playerId) = await app.SignUpAsync("rude_name");
        var mod = await app.StaffAsync("modo", [StaffRole.Moderator]);
        await mod.Client.PostAsJsonAsync($"{Admin}/players/{playerId}/sanctions", new { kind = "warning", reason = "Ton agressif", publicMessage = "Reste courtois." });
        await mod.Client.PostAsJsonAsync($"{Admin}/players/{playerId}/sanctions", new { kind = "pseudo_reset", reason = "Pseudo injurieux" });

        var notices = await Json(await player.GetAsync(new Uri("/api/v1/me/notices", UriKind.Relative)));
        var warning = notices.GetProperty("warnings")[0];
        Assert.Equal("Reste courtois.", warning.GetProperty("message").GetString());
        Assert.True(notices.GetProperty("pseudoResetPending").GetBoolean());
        Assert.StartsWith("joueur_", await app.Seed.ScalarAsync<string>("select pseudo from players where id = @id", ("id", playerId)), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NoContent, (await player.PostAsync(new Uri($"/api/v1/me/notices/warnings/{warning.GetProperty("id").GetInt64()}/ack", UriKind.Relative), null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await player.PutAsJsonAsync("/api/v1/me/pseudo", new { pseudo = "gentil" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await player.PutAsJsonAsync("/api/v1/me/pseudo", new { pseudo = "encore" })).StatusCode);

        var after = await Json(await player.GetAsync(new Uri("/api/v1/me/notices", UriKind.Relative)));
        Assert.Empty(after.GetProperty("warnings").EnumerateArray());
        Assert.False(after.GetProperty("pseudoResetPending").GetBoolean());
    }

    [Fact]
    public async Task Team_members_are_never_sanctioned_while_they_hold_a_role()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var mod = await app.StaffAsync("modo", [StaffRole.Moderator]);
        var other = await app.StaffAsync("support", [StaffRole.Support]);

        var refused = await mod.Client.PostAsJsonAsync($"{Admin}/players/{other.Id}/sanctions", new { kind = "warning", reason = "Test", publicMessage = "Test" });

        Assert.Equal("sanction.staff", (await Json(refused)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Support_answers_requests_internal_notes_stay_internal_and_the_player_is_notified()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (player, _) = await app.SignUpAsync("lost");
        var support = await app.StaffAsync("helper", [StaffRole.Support]);
        var mod = await app.StaffAsync("modo", [StaffRole.Moderator]);
        var id = (await Json(await player.PostAsJsonAsync("/api/v1/tickets", new { category = "support", subject = "Perdu", message = "Où sont mes amis ?" }))).GetProperty("id").GetInt64();

        // Moderators do not see support requests; support does.
        Assert.Empty((await Json(await mod.Client.GetAsync(new Uri($"{Admin}/tickets", UriKind.Relative)))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await mod.Client.GetAsync(new Uri($"{Admin}/tickets/{id}", UriKind.Relative))).StatusCode);

        await support.Client.PostAsJsonAsync($"{Admin}/tickets/{id}/messages", new { body = "À vérifier côté base.", @internal = true });
        await support.Client.PostAsJsonAsync($"{Admin}/tickets/{id}/messages", new { body = "Onglet Amis, en bas.", @internal = false });

        var seen = await Json(await player.GetAsync(new Uri($"/api/v1/tickets/{id}", UriKind.Relative)));
        Assert.Equal("waiting_player", seen.GetProperty("status").GetString());
        Assert.Equal(["Où sont mes amis ?", "Onglet Amis, en bas."], seen.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("body").GetString()));
        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from notification_outbox where kind = 'ticket.replied'"));

        var staffView = await Json(await support.Client.GetAsync(new Uri($"{Admin}/tickets/{id}", UriKind.Relative)));
        Assert.Equal(3, staffView.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task An_appeal_goes_to_someone_other_than_the_author_of_the_sanction()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (player, playerId) = await app.SignUpAsync("appealer");
        var mod = await app.StaffAsync("modo", [StaffRole.Moderator]);
        var other = await app.StaffAsync("modo2", [StaffRole.Moderator]);
        var sanction = (await Json(await mod.Client.PostAsJsonAsync($"{Admin}/players/{playerId}/sanctions", new { kind = "ban", reason = "Spam", publicMessage = "Banni deux jours.", endsAt = app.Clock.UtcNow.AddDays(2) }))).GetProperty("id").GetInt64();
        await ApiApp.DevLoginAsync(player, "appealer");
        var ticket = (await Json(await player.PostAsJsonAsync("/api/v1/tickets", new { category = "appeal", subject = "Contestation", message = "Erreur." }))).GetProperty("id").GetInt64();
        await app.Seed.ExecAsync("update tickets set sanction_id = @s where id = @t", ("s", sanction), ("t", ticket));

        var own = await mod.Client.PostAsJsonAsync($"{Admin}/tickets/{ticket}/messages", new { body = "Non.", @internal = false });
        Assert.Equal("ticket.own_sanction", (await Json(own)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await other.Client.PostAsJsonAsync($"{Admin}/tickets/{ticket}/messages", new { body = "Je regarde.", @internal = false })).StatusCode);
    }

    [Fact]
    public async Task Ideas_get_a_status_an_answer_and_duplicates_merge_with_their_votes()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, _) = await app.SignUpAsync("lea");
        var (tom, _) = await app.SignUpAsync("tom");
        var (ana, _) = await app.SignUpAsync("ana");
        var support = await app.StaffAsync("helper", [StaffRole.Support]);
        var main = await IdeaAsync(lea, "Une carte du monde");
        var duplicate = await IdeaAsync(tom, "Voir la carte du filet");
        await ana.PutAsync(new Uri($"/api/v1/ideas/{duplicate}/vote", UriKind.Relative), null);
        await lea.PutAsync(new Uri($"/api/v1/ideas/{duplicate}/vote", UriKind.Relative), null);

        var patched = await support.Client.PatchAsJsonAsync($"{Admin}/ideas/{main}", new { status = "planned", staffAnswer = "Prévue pour la phase 3." });
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await support.Client.PostAsJsonAsync($"{Admin}/ideas/{duplicate}/merge", new { intoId = main })).StatusCode);

        var seen = await Json(await tom.GetAsync(new Uri($"/api/v1/ideas/{main}", UriKind.Relative)));
        Assert.Equal("planned", seen.GetProperty("status").GetString());
        Assert.Equal("Prévue pour la phase 3.", seen.GetProperty("staffAnswer").GetString());
        // Ana's vote moves over; Léa's does not (she wrote the main idea).
        Assert.Equal(1, seen.GetProperty("votes").GetInt32());
        Assert.Equal(main, (await Json(await tom.GetAsync(new Uri($"/api/v1/ideas/{duplicate}", UriKind.Relative)))).GetProperty("mergedInto").GetInt64());
        Assert.Equal("idea.merge_target", (await Json(await support.Client.PostAsJsonAsync($"{Admin}/ideas/{main}/merge", new { intoId = duplicate }))).GetProperty("code").GetString());
    }

    private static async Task<long> IdeaAsync(HttpClient client, string title) =>
        (await Json(await client.PostAsJsonAsync("/api/v1/ideas", new { title, body = "Une description assez longue.", category = "gameplay", turnstileToken = FakeHumanVerifier.Valid }))).GetProperty("id").GetInt64();

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(string.IsNullOrEmpty(text) ? "null" : text);
        return document.RootElement.Clone();
    }
}
