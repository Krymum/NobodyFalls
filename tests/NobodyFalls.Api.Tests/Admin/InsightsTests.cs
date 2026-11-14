using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NobodyFalls.Api.Hosting;
using NobodyFalls.Api.Tests.Support;
using NobodyFalls.Domain.Staff;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Api.Tests.Admin;

/// <summary>Dashboard, linked accounts and personal data (GAME_DESIGN §9, decision Q5).</summary>
public sealed class InsightsTests(PostgresFixture postgres)
{
    private const string Admin = StaffSupport.Admin;

    [Fact]
    public async Task Activity_is_recorded_once_a_day_with_the_device_and_the_address()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, leaId) = await app.SignUpAsync("lea");

        for (var i = 0; i < 3; i++)
        {
            await lea.GetAsync(new Uri("/api/v1/tonight", UriKind.Relative));
        }

        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from player_visits where player_id = @id", ("id", leaId)));
        var events = await app.Seed.QueryAsync(
            "select kind, device_id from login_events where player_id = @id",
            r => (Kind: r.GetString(0), Device: r.GetString(1)),
            ("id", leaId));
        Assert.Single(events);
        Assert.Equal("sign_in", events[0].Kind);
        Assert.True(events[0].Device.Length > 10);
    }

    [Fact]
    public async Task Accounts_on_the_same_device_are_linked_and_every_look_is_audited()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (main, mainId) = await app.SignUpAsync("main");
        var (_, altId) = await app.SignUpAsync("alt");
        var mod = await app.StaffAsync("modo", [StaffRole.Moderator]);
        var support = await app.StaffAsync("help", [StaffRole.Support]);
        // The second account was used on the first one's device.
        await app.Seed.ExecAsync(
            "update login_events set device_id = (select device_id from login_events where player_id = @main limit 1) where player_id = @alt",
            ("main", mainId),
            ("alt", altId));

        var links = await Json(await mod.Client.GetAsync(new Uri($"{Admin}/players/{mainId}/links", UriKind.Relative)));

        var linked = links.GetProperty("accounts").EnumerateArray().Single(a => a.GetProperty("pseudo").GetString() == "alt");
        Assert.True(linked.GetProperty("sharesDevice").GetBoolean());
        Assert.NotEmpty(links.GetProperty("addresses").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await support.Client.GetAsync(new Uri($"{Admin}/players/{mainId}/links", UriKind.Relative))).StatusCode);
        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from audit_log where action = 'player.links_viewed' and target_id = @id", ("id", mainId.ToString())));
        Assert.NotNull(main);
    }

    [Fact]
    public async Task The_dashboard_counts_players_nights_retention_and_shared_devices()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        await app.SignUpAsync("one");
        await app.SignUpAsync("two");
        var boss = await app.StaffAsync("boss", [StaffRole.Admin]);
        await app.Seed.ExecAsync("update login_events set device_id = 'shared-device-000'");

        var dashboard = await Json(await boss.Client.GetAsync(new Uri($"{Admin}/dashboard", UriKind.Relative)));

        Assert.Equal(3, dashboard.GetProperty("alive").GetInt32());
        Assert.Equal(3, dashboard.GetProperty("visitedToday").GetInt32());
        Assert.Equal([1, 7, 30], dashboard.GetProperty("retention").EnumerateArray().Select(r => r.GetProperty("afterDays").GetInt32()));
        var shared = Assert.Single(dashboard.GetProperty("sharedDevices").EnumerateArray());
        Assert.Equal(3, shared.GetProperty("accounts").GetInt32());
    }

    [Fact]
    public async Task Support_exports_a_players_data_as_a_json_file()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, leaId) = await app.SignUpAsync("lea");
        await lea.PostAsJsonAsync("/api/v1/tickets", new { category = "account_deletion", subject = "Mes données", message = "Je veux une copie." });
        var support = await app.StaffAsync("help", [StaffRole.Support]);

        var response = await support.Client.GetAsync(new Uri($"{Admin}/players/{leaId}/export", UriKind.Relative));

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains($"nobodyfalls-{leaId}.json", response.Content.Headers.ContentDisposition?.ToString(), StringComparison.Ordinal);
        var data = await Json(response);
        Assert.Equal("lea", data.GetProperty("account").GetProperty("pseudo").GetString());
        Assert.Equal("Mes données", data.GetProperty("requests")[0].GetProperty("subject").GetString());
        Assert.NotEmpty(data.GetProperty("connections").EnumerateArray());
        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from audit_log where action = 'player.exported'"));
    }

    [Fact]
    public async Task Deleting_an_account_erases_it_but_keeps_the_worlds_history()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, leaId) = await app.SignUpAsync("lea");
        var (_, tomId) = await app.SignUpAsync("tom");
        await app.Seed.FriendsAsync(new(leaId), new(tomId));
        await lea.PostAsJsonAsync("/api/v1/hands", new { receiverId = tomId, turnstileToken = FakeHumanVerifier.Valid });
        var boss = await app.StaffAsync("boss", [StaffRole.Admin]);

        var noReason = await boss.Client.SendAsync(Delete(leaId, ""));
        Assert.Equal("gdpr.reason", (await Json(noReason)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await boss.Client.SendAsync(Delete(leaId, "Demande n° 12 du joueur"))).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await lea.GetAsync(new Uri("/api/v1/tonight", UriKind.Relative))).StatusCode);
        Assert.StartsWith("supprime_", await app.Seed.ScalarAsync<string>("select pseudo from players where id = @id", ("id", leaId)), StringComparison.Ordinal);
        Assert.Equal(0L, await app.Seed.ScalarAsync<long>("select count(*) from friendships"));
        Assert.Equal(0L, await app.Seed.ScalarAsync<long>("select count(*) from external_logins where player_id = @id", ("id", leaId)));
        Assert.Equal(0L, await app.Seed.ScalarAsync<long>("select count(*) from login_events where player_id = @id", ("id", leaId)));
        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from hands where giver_id = @id", ("id", leaId)));
        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from audit_log where action = 'player.deleted' and target_id = @id", ("id", leaId.ToString())));
        // The same person can sign up again, as a new player.
        await app.SignUpAsync("lea");

        var staffDelete = await boss.Client.SendAsync(Delete(boss.Id, "Test"));
        Assert.Equal(HttpStatusCode.Forbidden, staffDelete.StatusCode);
    }

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 19_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/19.0 Mobile/15E148 Safari/604.1", "safari-mobile")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:140.0) Gecko/20100101 Firefox/140.0", "firefox")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36 Edg/140.0", "edge")]
    [InlineData("", "unknown")]
    public void Browsers_are_named_coarsely(string agent, string family) =>
        Assert.Equal(family, ActivityTracking.AgentFamily(agent));

    private static HttpRequestMessage Delete(Guid id, string reason) =>
        new(HttpMethod.Delete, new Uri($"{Admin}/players/{id}", UriKind.Relative)) { Content = JsonContent.Create(new { reason }) };

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(string.IsNullOrEmpty(text) ? "null" : text);
        return document.RootElement.Clone();
    }
}
