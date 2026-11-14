using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NobodyFalls.Api.Tests.Support;
using NobodyFalls.Application.Night;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Api.Tests;

public sealed class GameTests(PostgresFixture postgres)
{
    private static async Task<JsonElement> GetAsync(HttpClient client, string url) =>
        await client.GetFromJsonAsync<JsonElement>(new Uri(url, UriKind.Relative));

    private static Task<HttpResponseMessage> HoldAsync(HttpClient client, Guid receiver, string token = FakeHumanVerifier.Valid) =>
        client.PostAsJsonAsync("/api/v1/hands", new { receiverId = receiver, turnstileToken = token });

    [Fact]
    public async Task Holding_someone_moves_them_off_the_edge_and_scores_at_midnight()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (me, _) = await app.SignUpAsync("moi");
        var (_, yuki) = await app.SignUpAsync("yuki");
        // Yuki joined today but must count as someone who can fall.
        await app.Seed.ExecAsync("update player_days set immune = false where player_id = @id", ("id", yuki));

        var edge = await GetAsync(me, "/api/v1/edge");
        Assert.Contains(edge.GetProperty("entries").EnumerateArray(), e => e.GetProperty("player").GetProperty("id").GetGuid() == yuki);

        var hand = await HoldAsync(me, yuki);
        Assert.Equal(HttpStatusCode.Created, hand.StatusCode);
        var given = await hand.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, given.GetProperty("handsLeft").GetInt32());

        edge = await GetAsync(me, "/api/v1/edge");
        Assert.DoesNotContain(edge.GetProperty("entries").EnumerateArray(), e => e.GetProperty("player").GetProperty("id").GetGuid() == yuki);

        var tonight = await GetAsync(me, "/api/v1/tonight");
        Assert.Equal(yuki, tonight.GetProperty("holding")[0].GetProperty("player").GetProperty("id").GetGuid());

        app.Clock.UtcNow = ApiApp.Day.MidnightAt;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<NightCycle>().TickAsync(CancellationToken.None);
        }

        var profile = await GetAsync(me, "/api/v1/me/profile");
        Assert.Equal(2, profile.GetProperty("guardianScore").GetInt32());
        var world = await GetAsync(app.Browser(), "/api/v1/world");
        Assert.Equal(12, world.GetProperty("nightNumber").GetInt32()); // the season opened ten days before
    }

    [Fact]
    public async Task Hand_errors_carry_stable_codes()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (me, self) = await app.SignUpAsync("moi");
        var others = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            others.Add((await app.SignUpAsync($"other{i}")).Id);
        }

        async Task<(HttpStatusCode Status, string? Code)> TryAsync(Guid receiver, string token = FakeHumanVerifier.Valid)
        {
            var response = await HoldAsync(me, receiver, token);
            var code = response.IsSuccessStatusCode ? null : (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
            return (response.StatusCode, code);
        }

        Assert.Equal((HttpStatusCode.Forbidden, "account.verification_failed"), await TryAsync(others[0], "bad"));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "hand.self"), await TryAsync(self));
        Assert.Equal((HttpStatusCode.NotFound, "hand.receiver_not_found"), await TryAsync(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, (await TryAsync(others[0])).Status);
        Assert.Equal((HttpStatusCode.Conflict, "hand.duplicate"), await TryAsync(others[0]));
        Assert.Equal(HttpStatusCode.Created, (await TryAsync(others[1])).Status);
        Assert.Equal(HttpStatusCode.Created, (await TryAsync(others[2])).Status);
        Assert.Equal((HttpStatusCode.Conflict, "hand.none_left"), await TryAsync(others[3]));
    }

    [Fact]
    public async Task While_midnight_is_resolved_hands_wait_and_say_when_to_retry()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (me, _) = await app.SignUpAsync("moi");
        var (_, other) = await app.SignUpAsync("other");

        // Midnight has passed but the night has not been resolved yet.
        app.Clock.UtcNow = ApiApp.Day.MidnightAt.AddSeconds(1);
        var response = await HoldAsync(me, other);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("2", response.Headers.RetryAfter?.ToString());
    }

    [Fact]
    public async Task Hands_are_rate_limited_per_player()
    {
        await using var app = await ApiApp.StartAsync(postgres, s => s["RateLimits:HandsPerPlayerPerMinute"] = "2");
        var (me, _) = await app.SignUpAsync("moi");

        await HoldAsync(me, Guid.NewGuid());
        await HoldAsync(me, Guid.NewGuid());
        var third = await HoldAsync(me, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("request.rate_limited", (await third.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_world_is_public()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        await app.SignUpAsync("someone");

        var world = await GetAsync(app.Browser(), "/api/v1/world");

        Assert.Equal(1, world.GetProperty("alive").GetInt32());
        Assert.Equal("2026-11-13", world.GetProperty("day").GetString());
    }
}
