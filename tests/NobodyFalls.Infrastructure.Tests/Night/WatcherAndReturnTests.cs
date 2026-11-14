using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Players;
using NobodyFalls.Infrastructure.Tests.Support;
using NobodyFalls.Testing;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Infrastructure.Tests.Night;

public sealed class WatcherAndReturnTests(PostgresFixture postgres)
{
    private static readonly GameDay Day = GameDay.Parse("2026-11-13");
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task Watchers_hold_the_players_nobody_holds_once_per_day()
    {
        await using var app = await TestApp.StartAsync(postgres, Day.StartsAt);
        await app.Seed.SeasonAsync(Day.AddDays(-10));
        await app.Seed.GameDayAsync(Day, watcherCount: 2);
        for (var i = 1; i <= 5; i++)
        {
            await app.Seed.PlayerAsync(Ids.Numbered(i));
            await app.Seed.PlayerDayAsync(Day, Ids.Numbered(i), 1, immune: i == 5);
        }

        await app.Seed.HandAsync(1, Day, Ids.Numbered(1), Ids.Numbered(2), 1, Day.StartsAt.AddHours(8), "human", false);

        var early = await app.Night.RunWatcherPassAsync(Day, Day.StartsAt.AddHours(21), Ct);
        var pass = await app.Night.RunWatcherPassAsync(Day, Day.StartsAt.AddHours(22), Ct);
        var again = await app.Night.RunWatcherPassAsync(Day, Day.StartsAt.AddHours(23), Ct);

        Assert.False(early.Ran);
        Assert.True(pass.Ran);
        Assert.False(again.Ran);

        // Players 1, 3 and 4 had no hand; 2 was held and 5 is immune.
        Assert.Equal(3, pass.HandsGiven);
        var held = await app.Seed.QueryAsync(
            "select receiver_id from hands where giver_kind = 'watcher' order by receiver_id",
            r => new PlayerId(r.GetGuid(0)));
        Assert.Equal([Ids.Numbered(1), Ids.Numbered(3), Ids.Numbered(4)], held);
        Assert.Equal(2L, await app.Seed.ScalarAsync<long>("select count(*) from players where kind = 'watcher'"));
        Assert.Equal(0L, await app.Seed.ScalarAsync<long>("select count(*) from player_days where received = 0 and not immune"));
    }

    [Fact]
    public async Task A_fallen_player_coming_back_early_plays_today_at_the_normal_threshold()
    {
        await using var app = await TestApp.StartAsync(postgres, Day.StartsAt);
        await app.Seed.SeasonAsync(Day.AddDays(-10));
        await app.Seed.GameDayAsync(Day, baseRequired: 2);
        await app.Seed.PlayerAsync(Ids.Of("yuki"), status: "fallen", joinedDay: Day.AddDays(-5));

        var outcome = await app.Lifecycle.ReturnAsync(Ids.Of("yuki"), Day.StartsAt.AddHours(10), Ct);
        var again = await app.Lifecycle.ReturnAsync(Ids.Of("yuki"), Day.StartsAt.AddHours(11), Ct);

        Assert.Equal(ReturnOutcome.BackToday, outcome);
        Assert.Equal(ReturnOutcome.NotFallen, again);
        var id = ("id", (object?)Ids.Of("yuki").Value);
        Assert.Equal("active", await app.Seed.ScalarAsync<string>("select status from players where id = @id", id));
        Assert.Equal(2, await app.Seed.ScalarAsync<int>("select required from player_days where player_id = @id", id));
        Assert.False(await app.Seed.ScalarAsync<bool>("select immune from player_days where player_id = @id", id));
    }

    [Fact]
    public async Task A_fallen_player_coming_back_late_plays_from_the_next_day()
    {
        await using var app = await TestApp.StartAsync(postgres, Day.StartsAt);
        await app.Seed.SeasonAsync(Day.AddDays(-10));
        await app.Seed.GameDayAsync(Day);
        await app.Seed.PlayerAsync(Ids.Of("yuki"), status: "fallen", joinedDay: Day.AddDays(-5));

        var outcome = await app.Lifecycle.ReturnAsync(Ids.Of("yuki"), Day.StartsAt.AddHours(23), Ct);
        await app.Night.ResolveAsync(Day, Day.MidnightAt, Ct);

        Assert.Equal(ReturnOutcome.BackTomorrow, outcome);
        var id = ("id", (object?)Ids.Of("yuki").Value);
        Assert.Equal("active", await app.Seed.ScalarAsync<string>("select status from players where id = @id", id));
        Assert.Equal(0L, await app.Seed.ScalarAsync<long>("select count(*) from player_falls where player_id = @id", id));
        Assert.True(await app.Seed.ScalarAsync<bool>(
            "select exists (select 1 from player_days where day = @day and player_id = @id)",
            ("day", Day.Next().Date),
            id));
    }
}
