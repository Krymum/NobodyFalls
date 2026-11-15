using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NobodyFalls.Api.Tests.Support;
using NobodyFalls.Application.Night;
using NobodyFalls.Domain.Staff;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Api.Tests.Admin;

/// <summary>Live configuration (GAME_DESIGN §9): rules, storms, seasons and feature flags.</summary>
public sealed class ConfigTests(PostgresFixture postgres)
{
    private const string Config = StaffSupport.Admin + "/config";

    [Fact]
    public async Task A_storm_planned_for_tomorrow_opens_tomorrow_and_raises_the_threshold_that_night()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, _) = await app.SignUpAsync("lea");
        var boss = await app.StaffAsync("boss", [StaffRole.Admin]);
        var tomorrow = ApiApp.Day.Next().ToString();

        Assert.Equal(HttpStatusCode.NoContent, (await boss.Client.PostAsJsonAsync($"{Config}/storms", new { day = tomorrow })).StatusCode);
        Assert.Equal("storm.already_scheduled", await CodeOf(await boss.Client.PostAsJsonAsync($"{Config}/storms", new { day = tomorrow })));
        // Today has started: its threshold never changes.
        Assert.Equal("storm.too_late", await CodeOf(await boss.Client.PostAsJsonAsync($"{Config}/storms", new { day = ApiApp.Day.ToString() })));

        await MidnightAsync(app, ApiApp.Day);

        var tonight = await Json(await lea.GetAsync(new Uri("/api/v1/tonight", UriKind.Relative)));
        Assert.True(tonight.GetProperty("storm").GetBoolean());
        // Léa's second night: the base, plus the storm, plus the fragile night (GAME_DESIGN §3).
        var baseRequired = await app.Seed.ScalarAsync<int>("select base_required from game_days where day = @day::date", ("day", tomorrow));
        Assert.Equal(baseRequired + 2, tonight.GetProperty("standing").GetProperty("required").GetInt32());
        // The day has started: the storm can no longer be cancelled.
        Assert.Equal("storm.too_late", await CodeOf(await boss.Client.DeleteAsync(new Uri($"{Config}/storms/{tomorrow}", UriKind.Relative))));
        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from audit_log where action = 'storm.scheduled' and target_id = @day", ("day", tomorrow)));
    }

    [Fact]
    public async Task A_planned_storm_can_be_cancelled_before_its_day()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var boss = await app.StaffAsync("boss", [StaffRole.Admin]);
        var day = ApiApp.Day.AddDays(3).ToString();
        await boss.Client.PostAsJsonAsync($"{Config}/storms", new { day });

        var listed = await Json(await boss.Client.GetAsync(new Uri($"{Config}/storms", UriKind.Relative)));
        Assert.Equal(ApiApp.Day.ToString(), listed.GetProperty("today").GetString());
        Assert.Equal(day, Assert.Single(listed.GetProperty("storms").EnumerateArray()).GetProperty("day").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await boss.Client.DeleteAsync(new Uri($"{Config}/storms/{day}", UriKind.Relative))).StatusCode);
        Assert.Equal("storm.not_found", await CodeOf(await boss.Client.DeleteAsync(new Uri($"{Config}/storms/{day}", UriKind.Relative))));
        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from audit_log where action = 'storm.cancelled'"));
    }

    [Fact]
    public async Task New_rules_are_a_new_version_applied_from_the_next_day_and_the_audit_keeps_what_changed()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var boss = await app.StaffAsync("boss", [StaffRole.Admin]);
        var state = await Json(await boss.Client.GetAsync(new Uri($"{Config}/rules", UriKind.Relative)));
        var rules = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(state.GetProperty("rules").GetRawText())!;
        var changed = new Dictionary<string, object>(rules.Select(kv => KeyValuePair.Create(kv.Key, (object)kv.Value))) { ["handsPerDay"] = 4 };

        Assert.Equal("rules.comment", await CodeOf(await boss.Client.PutAsJsonAsync($"{Config}/rules", new { rules = changed, comment = "" })));
        Assert.Equal("rules.unchanged", await CodeOf(await boss.Client.PutAsJsonAsync($"{Config}/rules", new { rules, comment = "Rien" })));
        var saved = await boss.Client.PutAsJsonAsync($"{Config}/rules", new { rules = changed, comment = "Une main de plus" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var after = await Json(await boss.Client.GetAsync(new Uri($"{Config}/rules", UriKind.Relative)));
        Assert.Equal(4, after.GetProperty("rules").GetProperty("handsPerDay").GetInt32());
        Assert.Equal(state.GetProperty("todayVersion").GetInt32(), after.GetProperty("todayVersion").GetInt32());
        Assert.Equal(state.GetProperty("latestVersion").GetInt32() + 1, after.GetProperty("latestVersion").GetInt32());
        var versions = await Json(await boss.Client.GetAsync(new Uri($"{Config}/rules/versions", UriKind.Relative)));
        Assert.Equal("Une main de plus", versions[0].GetProperty("comment").GetString());
        Assert.Equal("boss", versions[0].GetProperty("author").GetString());
        var audit = await app.Seed.ScalarAsync<string>("select after::text from audit_log where action = 'rules.changed'");
        Assert.Equal("""{"handsPerDay": 4}""", audit);

        var invalid = new Dictionary<string, object>(changed) { ["handsPerDay"] = 0 };
        Assert.Equal("rules.hands_per_day", await CodeOf(await boss.Client.PutAsJsonAsync($"{Config}/rules", new { rules = invalid, comment = "Zéro" })));
    }

    [Fact]
    public async Task The_next_season_is_planned_once_the_current_one_has_a_last_day()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var boss = await app.StaffAsync("boss", [StaffRole.Admin]);
        var seasons = await Json(await boss.Client.GetAsync(new Uri($"{Config}/seasons", UriKind.Relative)));
        var current = seasons.GetProperty("seasons")[0];
        var number = current.GetProperty("number").GetInt32();
        var opening = current.GetProperty("openingDay").GetString();
        var end = ApiApp.Day.AddDays(10).ToString();
        var next = ApiApp.Day.AddDays(11).ToString();

        Assert.Equal("season.overlap", await CodeOf(await boss.Client.PostAsJsonAsync($"{Config}/seasons", new { name = "Hiver", openingDay = next })));
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await boss.Client.PatchAsJsonAsync($"{Config}/seasons/{number}", new { name = current.GetProperty("name").GetString(), openingDay = opening, lastDay = end })).StatusCode);
        var planned = await boss.Client.PostAsJsonAsync($"{Config}/seasons", new { name = "Hiver", openingDay = next });
        Assert.Equal(HttpStatusCode.OK, planned.StatusCode);
        Assert.Equal(number + 1, (await Json(planned)).GetProperty("number").GetInt32());
        Assert.Equal("season.dates", await CodeOf(await boss.Client.PostAsJsonAsync($"{Config}/seasons", new { name = "Bis", openingDay = "demain" })));
        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from audit_log where action = 'season.planned'"));
        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from audit_log where action = 'season.changed'"));
    }

    [Fact]
    public async Task A_switched_off_feature_is_hidden_and_refused_but_rights_stay_open()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (lea, _) = await app.SignUpAsync("lea");
        var boss = await app.StaffAsync("boss", [StaffRole.Admin]);

        Assert.Equal("flag.not_found", await CodeOf(await boss.Client.PutAsJsonAsync($"{Config}/flags/nope", new { enabled = false })));
        foreach (var key in new[] { "ideas", "reports", "contact" })
        {
            Assert.Equal(HttpStatusCode.NoContent, (await boss.Client.PutAsJsonAsync($"{Config}/flags/{key}", new { enabled = false })).StatusCode);
        }

        var session = await Json(await lea.GetAsync(new Uri("/api/v1/session", UriKind.Relative)));
        Assert.False(session.GetProperty("features").GetProperty("ideas").GetBoolean());
        var idea = await lea.PostAsJsonAsync("/api/v1/ideas", new { title = "Une idée", body = "Avec un texte assez long.", category = "other", turnstileToken = FakeHumanVerifier.Valid });
        Assert.Equal(HttpStatusCode.Forbidden, idea.StatusCode);
        Assert.Equal("feature.disabled", await CodeOf(idea));
        Assert.Equal("feature.disabled", await CodeOf(await lea.PostAsJsonAsync("/api/v1/reports", new { target = "player", pseudo = "boss", category = "spam", turnstileToken = FakeHumanVerifier.Valid })));
        Assert.Equal("feature.disabled", await CodeOf(await lea.PostAsJsonAsync("/api/v1/tickets", new { category = "support", subject = "Aide", message = "Une question." })));
        // Asking for one's data to be erased is a right, not a feature.
        Assert.Equal(HttpStatusCode.Created, (await lea.PostAsJsonAsync("/api/v1/tickets", new { category = "account_deletion", subject = "Mes données", message = "Supprimez tout." })).StatusCode);

        // Back to the default: the override goes, and the audit keeps both changes.
        await boss.Client.PutAsJsonAsync($"{Config}/flags/ideas", new { enabled = true });
        Assert.Equal(2L, await app.Seed.ScalarAsync<long>("select count(*) from feature_flags"));
        Assert.Equal(4L, await app.Seed.ScalarAsync<long>("select count(*) from audit_log where action = 'flag.changed'"));
        var flags = await Json(await boss.Client.GetAsync(new Uri($"{Config}/flags", UriKind.Relative)));
        Assert.True(flags.EnumerateArray().Single(f => f.GetProperty("key").GetString() == "ideas").GetProperty("enabled").GetBoolean());
    }

    private static async Task MidnightAsync(ApiApp app, NobodyFalls.Domain.Common.GameDay day)
    {
        app.Clock.UtcNow = day.MidnightAt;
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NightCycle>().TickAsync(CancellationToken.None);
        app.Clock.UtcNow = day.MidnightAt.AddHours(1);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var body = await Json(response);
        return body.ValueKind == JsonValueKind.Object && body.TryGetProperty("code", out var code) ? code.GetString() : $"{(int)response.StatusCode}";
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(string.IsNullOrEmpty(text) ? "null" : text);
        return document.RootElement.Clone();
    }
}
