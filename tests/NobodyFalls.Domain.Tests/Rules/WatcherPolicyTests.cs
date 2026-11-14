using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Rules;
using NobodyFalls.Testing;

namespace NobodyFalls.Domain.Tests.Rules;

public sealed class WatcherPolicyTests
{
    private static readonly GameDay Day = GameDay.Parse("2026-11-13");

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 10)]
    [InlineData(20, 9)]
    [InlineData(100, 5)]
    [InlineData(199, 1)]
    [InlineData(200, 0)]
    [InlineData(5_000, 0)]
    public void Watchers_fade_out_as_the_population_grows(int alive, int expected) =>
        Assert.Equal(expected, WatcherPolicy.Count(GameRules.Default, alive));

    [Fact]
    public void Watchers_can_be_switched_off()
    {
        var rules = GameRules.Default with { Watchers = new WatcherRules { MaxCount = 0 } };
        Assert.Equal(0, WatcherPolicy.Count(rules, 3));
    }

    [Fact]
    public void Watchers_hold_only_players_with_no_hand_who_can_fall()
    {
        var candidates = new[]
        {
            new WatcherCandidate(Ids.Of("alone"), 0, false, PlayerStanding.Regular),
            new WatcherCandidate(Ids.Of("held"), 1, false, PlayerStanding.Regular),
            new WatcherCandidate(Ids.Of("newcomer"), 0, true, PlayerStanding.Newcomer),
        };

        var plan = WatcherPolicy.Plan(Day, candidates, [new WatcherSlot(Ids.Of("w1"), 3)]);

        Assert.Equal(Ids.Of("alone"), Assert.Single(plan).Receiver);
    }

    [Fact]
    public void Fragile_players_come_first_when_watchers_run_out_of_hands()
    {
        var regulars = Enumerable.Range(1, 5)
            .Select(i => new WatcherCandidate(Ids.Numbered(i), 0, false, PlayerStanding.Regular));
        var fragile = new WatcherCandidate(Ids.Of("fragile"), 0, false, PlayerStanding.Fragile);

        var plan = WatcherPolicy.Plan(Day, regulars.Append(fragile), [new WatcherSlot(Ids.Of("w1"), 2)]);

        Assert.Equal(2, plan.Count);
        Assert.Equal(Ids.Of("fragile"), plan[0].Receiver);
    }

    [Fact]
    public void Hands_are_spread_across_watchers_and_never_exceed_their_capacity()
    {
        var candidates = Enumerable.Range(1, 20)
            .Select(i => new WatcherCandidate(Ids.Numbered(i), 0, false, PlayerStanding.Regular))
            .ToList();
        var watchers = new[] { new WatcherSlot(Ids.Of("w1"), 3), new WatcherSlot(Ids.Of("w2"), 3), new WatcherSlot(Ids.Of("w3"), 1) };

        var plan = WatcherPolicy.Plan(Day, candidates, watchers);

        Assert.Equal(7, plan.Count);
        Assert.Equal(7, plan.Select(h => h.Receiver).Distinct().Count());
        Assert.Equal(3, plan.Count(h => h.Watcher == Ids.Of("w1")));
        Assert.Equal(1, plan.Count(h => h.Watcher == Ids.Of("w3")));
    }

    [Fact]
    public void The_plan_is_stable_for_a_day_and_changes_from_one_day_to_the_next()
    {
        var candidates = Enumerable.Range(1, 30)
            .Select(i => new WatcherCandidate(Ids.Numbered(i), 0, false, PlayerStanding.Regular))
            .ToList();
        WatcherSlot[] watchers = [new(Ids.Of("w1"), 3)];

        var today = WatcherPolicy.Plan(Day, candidates, watchers).Select(h => h.Receiver).ToList();
        var again = WatcherPolicy.Plan(Day, candidates.AsEnumerable().Reverse(), watchers).Select(h => h.Receiver).ToList();
        var tomorrow = WatcherPolicy.Plan(Day.Next(), candidates, watchers).Select(h => h.Receiver).ToList();

        Assert.Equal(today, again);
        Assert.NotEqual(today, tomorrow);
    }
}
