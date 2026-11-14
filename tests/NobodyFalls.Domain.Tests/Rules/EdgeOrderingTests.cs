using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Rules;
using NobodyFalls.Domain.Tests.Support;

namespace NobodyFalls.Domain.Tests.Rules;

public sealed class EdgeOrderingTests
{
    private static readonly GameDay Day = GameDay.Parse("2026-11-13");

    [Fact]
    public void Only_players_below_their_threshold_are_on_the_edge()
    {
        var safe = new EdgeCandidate(Ids.Of("safe"), 2, 2, false);
        var danger = new EdgeCandidate(Ids.Of("danger"), 0, 2, false);

        Assert.Equal([danger], EdgeOrdering.Order(Day, [safe, danger]));
    }

    [Fact]
    public void Most_urgent_first_and_immune_newcomers_last()
    {
        var newcomer = new EdgeCandidate(Ids.Of("newcomer"), 0, 3, true);
        var oneShort = new EdgeCandidate(Ids.Of("one-short"), 1, 2, false);
        var twoShort = new EdgeCandidate(Ids.Of("two-short"), 0, 2, false);
        var twoShortFragile = new EdgeCandidate(Ids.Of("two-short-fragile"), 1, 3, false);

        var order = EdgeOrdering.Order(Day, [newcomer, oneShort, twoShortFragile, twoShort]);

        Assert.Equal([twoShort, twoShortFragile, oneShort, newcomer], order);
    }

    [Fact]
    public void Ties_are_shuffled_per_day_so_help_spreads_out()
    {
        var tied = Enumerable.Range(1, 40).Select(i => new EdgeCandidate(Ids.Numbered(i), 0, 2, false)).ToList();

        var today = EdgeOrdering.Order(Day, tied);
        var tomorrow = EdgeOrdering.Order(Day.Next(), tied);

        Assert.Equal(today, EdgeOrdering.Order(Day, tied.AsEnumerable().Reverse()));
        Assert.NotEqual(today, tomorrow);
        Assert.NotEqual(tied, today);
    }

    [Fact]
    public void The_shuffle_key_matches_the_sql_formula()
    {
        // printf '2026-11-13:00000000-0000-0000-0000-000000000001' | md5sum → c4897dcaf7c8a273…
        Assert.Equal(-4284755259772329357L, StableShuffle.Key(Day, Ids.Numbered(1)));
    }
}
