using NobodyFalls.Application.Night;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Friends;
using NobodyFalls.Domain.Night;
using NobodyFalls.Domain.Players;
using NobodyFalls.Infrastructure.Persistence;
using NobodyFalls.Infrastructure.Tests.Support;
using NobodyFalls.Testing;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Infrastructure.Tests.Night;

/// <summary>
/// The SQL midnight must agree with the domain's reference implementation on every
/// random day (ADR 0003). A failure prints the seed: reproduce it with
/// <c>RandomDay.Generate(seed)</c> in a domain test.
/// </summary>
public sealed class DifferentialNightTests(PostgresFixture postgres)
{
    public static TheoryData<int> Seeds => new(Enumerable.Range(1, 60));

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task Sql_resolution_matches_the_reference(int seed)
    {
        var snapshot = RandomDay.Generate(seed);
        var day = snapshot.Day;
        await using var app = await TestApp.StartAsync(postgres, day.MidnightAt);
        await LoadAsync(app.Seed, snapshot);

        var run = await app.Night.ResolveAsync(day, day.MidnightAt, CancellationToken.None);

        Assert.Equal(NightRunStatus.Resolved, run.Status);
        var expected = NightResolver.Resolve(snapshot);
        var actual = await ReadOutcomeAsync(app.Seed, day);

        Assert.Equal(expected.Players.OrderBy(p => p.PlayerId), actual.Players);
        Assert.Equal(expected.Hands, actual.Hands);
        Assert.Equal(expected.Awards.OrderBy(a => a.PlayerId), actual.Awards);
        Assert.Equal(expected.LifeDebts.OrderBy(d => d.Saved).ThenBy(d => d.Saver), actual.LifeDebts);
        Assert.Equal(expected.HeldTogether.OrderBy(p => p.Low).ThenBy(p => p.High), actual.HeldTogether);
        Assert.Equal((expected.Falls, expected.Streak, expected.DeathsAllTime), (actual.Falls, actual.Streak, actual.DeathsAllTime));
        Assert.Equal((expected.Falls, expected.Streak, expected.DeathsAllTime), (run.Falls, run.Streak, run.DeathsAllTime));
    }

    private static async Task LoadAsync(Seeder seed, DaySnapshot snapshot)
    {
        var day = snapshot.Day;
        await seed.SeasonAsync(day.AddDays(-30));
        await seed.GameDayAsync(day.Previous(), resolved: true, streakAfter: snapshot.PreviousStreak, deathsAfter: snapshot.PreviousDeathsAllTime);
        await seed.GameDayAsync(day);

        var inPlay = snapshot.Players.Select(p => p.PlayerId).ToHashSet();
        foreach (var entry in snapshot.Players)
        {
            await seed.PlayerAsync(entry.PlayerId);
            await seed.PlayerDayAsync(day, entry.PlayerId, entry.Required, entry.Immune);
        }

        // Givers who are not in play today: fallen players who have not come back, and Watchers.
        foreach (var giver in snapshot.Hands.Where(h => !inPlay.Contains(h.Giver)).GroupBy(h => h.Giver))
        {
            var kind = giver.First().GiverKind;
            await seed.PlayerAsync(
                giver.Key,
                status: kind == PlayerKind.Watcher ? "active" : "fallen",
                kind: DbValues.Of(kind));
        }

        foreach (var byGiver in snapshot.Hands.GroupBy(h => h.Giver))
        {
            short slot = 0;
            foreach (var hand in byGiver.OrderBy(h => h.Id))
            {
                await seed.HandAsync(hand.Id, day, hand.Giver, hand.Receiver, ++slot, hand.GivenAt, DbValues.Of(hand.GiverKind), hand.GiverWasFriend);
            }
        }
    }

    private static async Task<NightOutcome> ReadOutcomeAsync(Seeder seed, GameDay day)
    {
        var d = ("day", (object?)day.Date);

        var players = await seed.QueryAsync(
            "select player_id, received, required, outcome from player_days where day = @day",
            r => new PlayerNightResult(new PlayerId(r.GetGuid(0)), r.GetInt32(1), r.GetInt32(2), DbValues.ToNightResult(r.GetString(3))),
            d);
        var hands = await seed.QueryAsync(
            "select id, points, reciprocal, last_rampart from hands where day = @day order by id",
            r => new HandScore(r.GetInt64(0), r.GetInt32(1), r.GetBoolean(2), r.GetBoolean(3)),
            d);
        var awards = await seed.QueryAsync(
            "select giver_id, sum(points)::int from hands where day = @day group by giver_id having sum(points) > 0",
            r => new GuardianAward(new PlayerId(r.GetGuid(0)), r.GetInt32(1)),
            d);
        var debts = await seed.QueryAsync(
            "select saved_id, saver_id from life_debts where day = @day",
            r => new LifeDebt(new PlayerId(r.GetGuid(0)), new PlayerId(r.GetGuid(1))),
            d);
        var pairs = await seed.QueryAsync(
            "select player_low, player_high from hold_pairs where last_day = @day",
            r => PlayerPair.Of(new PlayerId(r.GetGuid(0)), new PlayerId(r.GetGuid(1))),
            d);
        var counters = await seed.QueryAsync(
            "select falls, streak_after, deaths_all_time_after from game_days where day = @day",
            r => (Falls: r.GetInt32(0), Streak: r.GetInt32(1), Deaths: r.GetInt64(2)),
            d);

        return new NightOutcome(
            day,
            players.OrderBy(p => p.PlayerId).ToList(),
            hands,
            awards.OrderBy(a => a.PlayerId).ToList(),
            debts.OrderBy(x => x.Saved).ThenBy(x => x.Saver).ToList(),
            pairs.OrderBy(p => p.Low).ThenBy(p => p.High).ToList(),
            counters[0].Falls,
            counters[0].Streak,
            counters[0].Deaths);
    }
}
