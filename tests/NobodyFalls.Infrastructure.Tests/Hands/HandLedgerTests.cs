using NobodyFalls.Application.Night;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Rules;
using NobodyFalls.Infrastructure.Tests.Support;
using NobodyFalls.Testing;

namespace NobodyFalls.Infrastructure.Tests.Hands;

/// <summary>Hands under real concurrency, on a real PostgreSQL (ADR 0004).</summary>
public sealed class HandLedgerTests(PostgresFixture postgres)
{
    private static readonly GameDay Day = GameDay.Parse("2026-11-13");
    private static readonly DateTimeOffset Noon = Day.StartsAt.AddHours(12);
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static async Task<TestApp> WorldAsync(PostgresFixture postgres, int players)
    {
        var app = await TestApp.StartAsync(postgres, Noon);
        await app.Seed.SeasonAsync(Day.AddDays(-10));
        await app.Seed.GameDayAsync(Day);
        for (var i = 1; i <= players; i++)
        {
            await app.Seed.PlayerAsync(Ids.Numbered(i));
            await app.Seed.PlayerDayAsync(Day, Ids.Numbered(i), 2);
        }

        return app;
    }

    [Fact]
    public async Task A_hand_is_recorded_and_counted()
    {
        await using var app = await WorldAsync(postgres, 2);
        await app.Seed.FriendsAsync(Ids.Numbered(1), Ids.Numbered(2));

        var result = await app.Hands.GiveAsync(Ids.Numbered(1), Ids.Numbered(2), Noon, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.HandsLeft);
        Assert.Equal(1, result.Value.ReceiverReceived);
        Assert.Equal(2, result.Value.ReceiverRequired);
        Assert.True(await app.Seed.ScalarAsync<bool>("select giver_was_friend from hands where id = @id", ("id", result.Value.HandId)));
    }

    [Fact]
    public async Task Ten_simultaneous_hands_from_one_player_yield_exactly_three()
    {
        await using var app = await WorldAsync(postgres, 11);
        var giver = Ids.Numbered(1);

        var results = await Task.WhenAll(Enumerable.Range(2, 10)
            .Select(i => app.Hands.GiveAsync(giver, Ids.Numbered(i), Noon, Ct)));

        Assert.Equal(3, results.Count(r => r.IsSuccess));
        Assert.All(results.Where(r => r.IsFailure), r => Assert.Equal(HandErrors.NoneLeft, r.Error));
        var slots = await app.Seed.QueryAsync("select slot from hands where giver_id = @g order by slot", r => (int)r.GetInt16(0), ("g", giver.Value));
        Assert.Equal([1, 2, 3], slots);
    }

    [Fact]
    public async Task The_same_receiver_twice_at_once_is_held_once()
    {
        await using var app = await WorldAsync(postgres, 2);

        var results = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => app.Hands.GiveAsync(Ids.Numbered(1), Ids.Numbered(2), Noon, Ct)));

        Assert.Single(results, r => r.IsSuccess);
        Assert.All(results.Where(r => r.IsFailure), r => Assert.Equal(HandErrors.Duplicate, r.Error));
        Assert.Equal(1, await app.Seed.ScalarAsync<int>(
            "select received from player_days where player_id = @id", ("id", Ids.Numbered(2).Value)));
    }

    [Fact]
    public async Task Many_players_holding_one_at_once_are_all_counted()
    {
        await using var app = await WorldAsync(postgres, 30);
        var target = Ids.Numbered(30);

        var results = await Task.WhenAll(Enumerable.Range(1, 29)
            .Select(i => app.Hands.GiveAsync(Ids.Numbered(i), target, Noon, Ct)));

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Equal(29, await app.Seed.ScalarAsync<int>("select received from player_days where player_id = @id", ("id", target.Value)));
    }

    [Fact]
    public async Task Rules_are_enforced_against_the_stored_state()
    {
        await using var app = await WorldAsync(postgres, 3);
        await app.Seed.BlockAsync(Ids.Numbered(2), Ids.Numbered(1));
        await app.Seed.PlayerAsync(Ids.Of("fallen"), status: "fallen");

        Assert.Equal(HandErrors.ReceiverNotFound, (await app.Hands.GiveAsync(Ids.Numbered(1), Ids.Numbered(2), Noon, Ct)).Error);
        Assert.Equal(HandErrors.ReceiverNotFound, (await app.Hands.GiveAsync(Ids.Numbered(1), Ids.Of("nobody"), Noon, Ct)).Error);
        Assert.Equal(HandErrors.ReceiverNotInPlay, (await app.Hands.GiveAsync(Ids.Numbered(1), Ids.Of("fallen"), Noon, Ct)).Error);
        Assert.Equal(HandErrors.Self, (await app.Hands.GiveAsync(Ids.Numbered(1), Ids.Numbered(1), Noon, Ct)).Error);

        // A fallen player who has not come back can still give.
        Assert.True((await app.Hands.GiveAsync(Ids.Of("fallen"), Ids.Numbered(3), Noon, Ct)).IsSuccess);
    }

    [Fact]
    public async Task Before_the_season_opens_no_hand_is_given()
    {
        await using var app = await TestApp.StartAsync(postgres, Noon);
        await app.Seed.SeasonAsync(Day.AddDays(3));
        await app.Seed.PlayerAsync(Ids.Numbered(1), status: "waitlisted");
        await app.Seed.PlayerAsync(Ids.Numbered(2), status: "waitlisted");

        var result = await app.Hands.GiveAsync(Ids.Numbered(1), Ids.Numbered(2), Noon, Ct);

        Assert.Equal(HandErrors.SeasonClosed, result.Error);
    }

    [Fact]
    public async Task No_hand_slips_through_midnight()
    {
        // Hands stamped in the last second race the resolution of that very night: each
        // one must either be counted by the resolution or be refused, never lost.
        await using var app = await WorldAsync(postgres, 40);
        var lastSecond = Day.MidnightAt.AddMilliseconds(-1);

        var gives = Enumerable.Range(1, 39)
            .Select(i => Task.Run(() => app.Hands.GiveAsync(Ids.Numbered(i), Ids.Numbered(i + 1), lastSecond, Ct)))
            .ToList();
        var resolution = Task.Run(() => app.Night.ResolveAsync(Day, Day.MidnightAt, Ct));
        var results = await Task.WhenAll(gives);
        Assert.Equal(NightRunStatus.Resolved, (await resolution).Status);

        Assert.All(results.Where(r => r.IsFailure), r => Assert.Equal(HandErrors.NightResolving, r.Error));
        var given = results.Where(r => r.IsSuccess).Select(r => r.Value.HandId).ToList();
        var unscored = await app.Seed.ScalarAsync<long>(
            "select count(*) from hands where id = any(@ids) and points is null", ("ids", given.ToArray()));
        Assert.Equal(0L, unscored);
    }
}
