using NobodyFalls.Application.Night;
using NobodyFalls.Domain.Common;
using NobodyFalls.Infrastructure.Tests.Support;
using NobodyFalls.Testing;

namespace NobodyFalls.Infrastructure.Tests.Night;

public sealed class NightStoreTests(PostgresFixture postgres)
{
    private static readonly GameDay Day = GameDay.Parse("2026-11-13");
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static async Task<TestApp> DayWithPlayersAsync(PostgresFixture postgres, params (string Name, int Required, string Status)[] players)
    {
        var app = await TestApp.StartAsync(postgres, Day.MidnightAt);
        await app.Seed.SeasonAsync(Day.AddDays(-10));
        await app.Seed.GameDayAsync(Day);
        foreach (var (name, required, status) in players)
        {
            await app.Seed.PlayerAsync(Ids.Of(name), status: status);
            if (status == "active")
            {
                await app.Seed.PlayerDayAsync(Day, Ids.Of(name), required);
            }
        }

        return app;
    }

    [Fact]
    public async Task Resolving_twice_changes_nothing_the_second_time()
    {
        await using var app = await DayWithPlayersAsync(postgres, ("a", 0, "active"), ("b", 1, "active"), ("c", 1, "active"));
        await app.Seed.HandAsync(1, Day, Ids.Of("a"), Ids.Of("b"), 1, Day.StartsAt.AddHours(9), "human", false);

        var first = await app.Night.ResolveAsync(Day, Day.MidnightAt, Ct);
        var score = await app.Seed.ScalarAsync<int>("select guardian_score from players where id = @id", ("id", Ids.Of("a").Value));
        var outbox = await app.Seed.ScalarAsync<long>("select count(*) from notification_outbox");
        var second = await app.Night.ResolveAsync(Day, Day.MidnightAt.AddMinutes(1), Ct);

        Assert.Equal(NightRunStatus.Resolved, first.Status);
        Assert.Equal(1, first.Falls);
        Assert.Equal(NightRunStatus.AlreadyResolved, second.Status);
        Assert.Equal(2, score);
        Assert.Equal(score, await app.Seed.ScalarAsync<int>("select guardian_score from players where id = @id", ("id", Ids.Of("a").Value)));
        Assert.Equal(outbox, await app.Seed.ScalarAsync<long>("select count(*) from notification_outbox"));
    }

    [Fact]
    public async Task Concurrent_resolutions_resolve_once()
    {
        await using var app = await DayWithPlayersAsync(postgres, ("a", 1, "active"), ("b", 1, "active"));

        var runs = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => app.Night.ResolveAsync(Day, Day.MidnightAt, Ct)));

        Assert.Single(runs, r => r.Status == NightRunStatus.Resolved);
        Assert.Equal(1, await app.Seed.ScalarAsync<int>("select falls_count from players where id = @id", ("id", Ids.Of("a").Value)));
        Assert.Equal(2L, await app.Seed.ScalarAsync<long>("select deaths from seasons where id = 1"));
    }

    [Fact]
    public async Task A_night_cannot_be_resolved_before_its_midnight()
    {
        await using var app = await DayWithPlayersAsync(postgres, ("a", 1, "active"));

        var run = await app.Night.ResolveAsync(Day, Day.MidnightAt.AddSeconds(-1), Ct);

        Assert.Equal(NightRunStatus.TooEarly, run.Status);
    }

    [Fact]
    public async Task Falls_are_recorded_and_notified_to_friends()
    {
        await using var app = await DayWithPlayersAsync(postgres, ("faller", 1, "active"), ("friend", 0, "active"));
        await app.Seed.FriendsAsync(Ids.Of("faller"), Ids.Of("friend"));

        await app.Night.ResolveAsync(Day, Day.MidnightAt, Ct);

        var faller = ("id", (object?)Ids.Of("faller").Value);
        Assert.Equal("fallen", await app.Seed.ScalarAsync<string>("select status from players where id = @id", faller));
        Assert.Equal(1, await app.Seed.ScalarAsync<int>("select falls_count from players where id = @id", faller));
        Assert.Equal(1L, await app.Seed.ScalarAsync<long>("select count(*) from player_falls where player_id = @id", faller));
        Assert.Equal(1L, await app.Seed.ScalarAsync<long>(
            "select count(*) from notification_outbox where kind = 'friends.fell' and player_id = @id",
            ("id", Ids.Of("friend").Value)));
    }

    [Fact]
    public async Task The_next_day_starts_with_its_thresholds_frozen()
    {
        await using var app = await DayWithPlayersAsync(postgres, ("veteran", 0, "active"), ("faller", 1, "active"));
        // Joined today: immune tonight, fragile tomorrow.
        await app.Seed.PlayerAsync(Ids.Of("newcomer"), joinedDay: Day);
        await app.Seed.PlayerDayAsync(Day, Ids.Of("newcomer"), 1, immune: true);
        // Fell earlier and came back late yesterday: back in play tomorrow.
        await app.Seed.PlayerAsync(Ids.Of("returning"), status: "fallen", returnsOnDay: Day.Next());
        await app.Seed.StormAsync(Day.Next());

        var run = await app.Night.ResolveAsync(Day, Day.MidnightAt, Ct);

        Assert.True(run.NextDayStarted);
        var next = ("day", (object?)Day.Next().Date);
        Assert.Equal(1, await app.Seed.ScalarAsync<int>("select base_required from game_days where day = @day", next));
        Assert.True(await app.Seed.ScalarAsync<bool>("select storm from game_days where day = @day", next));

        var required = await app.Seed.QueryAsync(
            "select player_id, required, immune from player_days where day = @day",
            r => (Id: r.GetGuid(0), Required: r.GetInt32(1), Immune: r.GetBoolean(2)),
            next);
        Assert.Equal(3, required.Count); // the faller is out
        Assert.Contains((Ids.Of("veteran").Value, 2, false), required);   // base 1 + storm
        Assert.Contains((Ids.Of("newcomer").Value, 3, false), required);  // base 1 + storm + fragile
        Assert.Contains((Ids.Of("returning").Value, 2, false), required); // normal threshold
    }

    [Fact]
    public async Task A_season_opening_lets_the_waiting_list_in_immune()
    {
        await using var app = await TestApp.StartAsync(postgres, Day.StartsAt.AddMinutes(1));
        await app.Seed.SeasonAsync(Day);
        await app.Seed.PlayerAsync(Ids.Of("early"), status: "waitlisted");

        Assert.True(await app.Night.EnsureDayStartedAsync(Day, Day.StartsAt.AddMinutes(1), Ct));
        Assert.False(await app.Night.EnsureDayStartedAsync(Day, Day.StartsAt.AddMinutes(2), Ct));

        Assert.Equal("active", await app.Seed.ScalarAsync<string>("select status from players where id = @id", ("id", Ids.Of("early").Value)));
        Assert.True(await app.Seed.ScalarAsync<bool>(
            "select immune from player_days where day = @day and player_id = @id",
            ("day", Day.Date),
            ("id", Ids.Of("early").Value)));
        Assert.Equal(1, await app.Seed.ScalarAsync<int>("select night_number from game_days where day = @day", ("day", Day.Date)));
    }

    [Fact]
    public async Task No_day_starts_outside_a_season()
    {
        await using var app = await TestApp.StartAsync(postgres, Day.StartsAt);
        await app.Seed.SeasonAsync(Day.AddDays(5));

        Assert.False(await app.Night.EnsureDayStartedAsync(Day, Day.StartsAt, Ct));
    }

    [Fact]
    public async Task The_cycle_resolves_missed_nights_in_order_and_starts_today()
    {
        await using var app = await DayWithPlayersAsync(postgres, ("a", 0, "active"));
        app.Clock.UtcNow = Day.AddDays(2).StartsAt.AddHours(1);

        var results = await app.Cycle.TickAsync(Ct);

        Assert.Equal([Day, Day.Next()], results.Select(r => r.Day));
        Assert.All(results, r => Assert.Equal(NightRunStatus.Resolved, r.Status));
        Assert.Equal(1, results[0].Streak);

        // Nobody held "a" on the second day: the cycle really played it.
        Assert.Equal(1, results[1].Falls);
        Assert.Equal(0, results[1].Streak);
        Assert.True(await app.Seed.ScalarAsync<bool>(
            "select exists (select 1 from game_days where day = @day and resolved_at is null)",
            ("day", Day.AddDays(2).Date)));
    }
}
