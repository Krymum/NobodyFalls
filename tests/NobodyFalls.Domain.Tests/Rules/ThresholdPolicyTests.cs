using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Rules;

namespace NobodyFalls.Domain.Tests.Rules;

public sealed class ThresholdPolicyTests
{
    private static readonly GameRules Rules = GameRules.Default;

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(49, 1)]
    [InlineData(50, 2)]
    [InlineData(10_000, 2)]
    public void The_base_threshold_is_lower_while_the_world_is_small(int alive, int expected) =>
        Assert.Equal(expected, ThresholdPolicy.BaseRequired(Rules, alive));

    [Fact]
    public void Without_the_low_population_rule_a_threshold_never_exceeds_the_others_who_could_hold_you()
    {
        var rules = Rules with { LowPopulationBelow = 0 };

        Assert.Equal(0, ThresholdPolicy.BaseRequired(rules, 1));
        Assert.Equal(1, ThresholdPolicy.BaseRequired(rules, 2));
        Assert.Equal(2, ThresholdPolicy.BaseRequired(rules, 3));
    }

    [Theory]
    [InlineData(false, PlayerStanding.Regular, 2)]
    [InlineData(true, PlayerStanding.Regular, 3)]
    [InlineData(false, PlayerStanding.Fragile, 3)]
    [InlineData(true, PlayerStanding.Fragile, 4)]
    [InlineData(false, PlayerStanding.Newcomer, 2)]
    public void Storm_and_fragility_add_up(bool storm, PlayerStanding standing, int expected) =>
        Assert.Equal(expected, ThresholdPolicy.Required(Rules, 2, storm, standing));

    [Fact]
    public void Standing_follows_the_nights_since_joining()
    {
        var joined = GameDay.Parse("2026-11-13");

        Assert.Equal(PlayerStanding.Newcomer, ThresholdPolicy.StandingOn(joined, joined));
        Assert.Equal(PlayerStanding.Fragile, ThresholdPolicy.StandingOn(joined.Next(), joined));
        Assert.Equal(PlayerStanding.Regular, ThresholdPolicy.StandingOn(joined.AddDays(2), joined));
        Assert.Equal(PlayerStanding.Regular, ThresholdPolicy.StandingOn(joined.AddDays(300), joined));
    }

    [Fact]
    public void Default_rules_are_valid_and_broken_ones_are_reported()
    {
        Assert.Empty(GameRules.Default.Validate());

        var broken = GameRules.Default with
        {
            HandsPerDay = 0,
            StormNotice = TimeSpan.FromHours(30),
            Watchers = new WatcherRules { MaxCount = -1 },
        };

        var codes = broken.Validate().Select(e => e.Code).ToList();
        Assert.Contains("rules.hands_per_day", codes);
        Assert.Contains("rules.storm_notice", codes);
        Assert.Contains("rules.watchers.max_count", codes);
    }
}
