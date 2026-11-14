using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NobodyFalls.Api.Tests.Support;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Api.Tests;

public sealed class AccountTests(PostgresFixture postgres)
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await JsonAsync(response)).GetProperty("code").GetString();

    [Fact]
    public async Task A_visitor_signs_in_then_creates_a_character()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var client = app.Browser();

        var anonymous = await JsonAsync(await client.GetAsync(new Uri("/api/v1/session", UriKind.Relative)));
        Assert.Equal("anonymous", anonymous.GetProperty("state").GetString());
        Assert.Contains("dev", anonymous.GetProperty("providers").EnumerateArray().Select(p => p.GetString()));

        await ApiApp.DevLoginAsync(client, "yuki");
        var pending = await JsonAsync(await client.GetAsync(new Uri("/api/v1/session", UriKind.Relative)));
        Assert.Equal("pendingRegistration", pending.GetProperty("state").GetString());

        var created = await client.PostAsJsonAsync("/api/v1/registration", ApiApp.Registration("Yuki"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var session = await JsonAsync(await client.GetAsync(new Uri("/api/v1/session", UriKind.Relative)));
        Assert.Equal("player", session.GetProperty("state").GetString());
        Assert.Equal("Yuki", session.GetProperty("player").GetProperty("pseudo").GetString());

        // A newcomer is in tonight's game, immune at the coming midnight.
        var tonight = await JsonAsync(await client.GetAsync(new Uri("/api/v1/tonight", UriKind.Relative)));
        Assert.True(tonight.GetProperty("standing").GetProperty("immune").GetBoolean());
        Assert.Equal(3, tonight.GetProperty("handsLeft").GetInt32());
    }

    [Fact]
    public async Task Signing_in_again_opens_the_existing_session()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        await app.SignUpAsync("kenji");

        var later = app.Browser();
        var login = await later.GetAsync(new Uri("/auth/login/dev?name=kenji&returnUrl=/en/tonight", UriKind.Relative));
        var complete = await later.GetAsync(login.Headers.Location);

        Assert.Equal("/en/tonight", complete.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await later.GetAsync(new Uri("/api/v1/tonight", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Return_urls_never_leave_the_site()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        await app.SignUpAsync("tom.b");
        var client = app.Browser();

        var login = await client.GetAsync(new Uri("/auth/login/dev?name=tom.b&returnUrl=//evil.example", UriKind.Relative));
        var complete = await client.GetAsync(login.Headers.Location);

        Assert.Equal("/", complete.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Joining_through_an_invitation_makes_friends_with_the_inviter()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, _) = await app.SignUpAsync("lea_m");
        var code = (await JsonAsync(await lea.GetAsync(new Uri("/api/v1/me/profile", UriKind.Relative)))).GetProperty("inviteCode").GetString();

        var preview = await JsonAsync(await app.Browser().GetAsync(new Uri($"/api/v1/invites/{code}", UriKind.Relative)));
        Assert.Equal("lea_m", preview.GetProperty("inviter").GetProperty("pseudo").GetString());

        var (ines, _) = await app.SignUpAsync("ines", code);
        var friends = await JsonAsync(await ines.GetAsync(new Uri("/api/v1/friends", UriKind.Relative)));
        Assert.Equal("lea_m", friends.GetProperty("items")[0].GetProperty("player").GetProperty("pseudo").GetString());
    }

    [Fact]
    public async Task Registration_is_refused_for_these_reasons()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        await app.SignUpAsync("Léa");

        async Task<HttpResponseMessage> TryAsync(string login, object body)
        {
            var client = app.Browser();
            await ApiApp.DevLoginAsync(client, login);
            return await client.PostAsJsonAsync("/api/v1/registration", body);
        }

        var taken = await TryAsync("other", ApiApp.Registration("lea"));
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal("pseudo.taken", await CodeAsync(taken));

        Assert.Equal("pseudo.reserved", await CodeAsync(await TryAsync("w", ApiApp.Registration("veilleur.99"))));
        Assert.Equal("pseudo.length", await CodeAsync(await TryAsync("x", ApiApp.Registration("ab"))));
        Assert.Equal("appearance.not_owned", await CodeAsync(await TryAsync("y", ApiApp.Registration("dressed", coat: "coat-storm"))));

        var robot = await TryAsync("z", ApiApp.Registration("robot", token: "bad"));
        Assert.Equal(HttpStatusCode.Forbidden, robot.StatusCode);
        Assert.Equal("account.verification_failed", await CodeAsync(robot));

        var nobody = await app.Browser().PostAsJsonAsync("/api/v1/registration", ApiApp.Registration("ghost"));
        Assert.Equal(HttpStatusCode.Unauthorized, nobody.StatusCode);
    }

    [Fact]
    public async Task A_fallen_player_is_back_on_their_next_visit()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (client, id) = await app.SignUpAsync("returning");
        await app.Seed.ExecAsync("update players set status = 'fallen', joined_day = '2026-10-01' where id = @id", ("id", id));
        await app.Seed.ExecAsync("delete from player_days where player_id = @id", ("id", id));

        var session = await JsonAsync(await client.GetAsync(new Uri("/api/v1/session", UriKind.Relative)));

        Assert.Equal("backToday", session.GetProperty("returned").GetString());
        Assert.Equal("alive", session.GetProperty("player").GetProperty("status").GetString());
        var tonight = await JsonAsync(await client.GetAsync(new Uri("/api/v1/tonight", UriKind.Relative)));
        Assert.False(tonight.GetProperty("standing").GetProperty("immune").GetBoolean());
    }
}
