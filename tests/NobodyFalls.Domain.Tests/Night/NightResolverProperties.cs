using FsCheck.Xunit;
using NobodyFalls.Domain.Night;
using NobodyFalls.Domain.Players;
using NobodyFalls.Domain.Tests.Support;

namespace NobodyFalls.Domain.Tests.Night;

/// <summary>
/// Invariants that must hold for any valid day. Each property gets a seed and builds
/// a random but valid day from it, which keeps failures reproducible.
/// </summary>
public sealed class NightResolverProperties
{
    private const int Runs = 300;

    [Property(MaxTest = Runs)]
    public void Exactly_the_non_immune_players_short_of_hands_fall(int seed)
    {
        var snapshot = RandomDay.Generate(seed);
        var outcome = NightResolver.Resolve(snapshot);

        foreach (var entry in snapshot.Players)
        {
            var received = snapshot.Hands.Count(h => h.Receiver == entry.PlayerId);
            var result = outcome.Players.Single(p => p.PlayerId == entry.PlayerId);
            Assert.Equal(received, result.Received);
            Assert.Equal(!entry.Immune && received < entry.Required, result.Result == NightResult.Fell);
        }

        Assert.Equal(outcome.Players.Count(p => p.Result == NightResult.Fell), outcome.Falls);
    }

    [Property(MaxTest = Runs)]
    public void Points_go_only_to_human_non_reciprocal_hands_to_players_who_made_it(int seed)
    {
        var snapshot = RandomDay.Generate(seed);
        var outcome = NightResolver.Resolve(snapshot);
        var results = outcome.Players.ToDictionary(p => p.PlayerId, p => p.Result);

        foreach (var score in outcome.Hands.Where(s => s.Points > 0))
        {
            var hand = snapshot.Hands.Single(h => h.Id == score.HandId);
            Assert.Equal(PlayerKind.Human, hand.GiverKind);
            Assert.False(score.Reciprocal);
            Assert.Contains(results[hand.Receiver], new[] { NightResult.Survived, NightResult.Immune });
        }
    }

    [Property(MaxTest = Runs)]
    public void Reciprocity_is_symmetric(int seed)
    {
        var snapshot = RandomDay.Generate(seed);
        var outcome = NightResolver.Resolve(snapshot);

        foreach (var score in outcome.Hands)
        {
            var hand = snapshot.Hands.Single(h => h.Id == score.HandId);
            var inverse = snapshot.Hands.Any(h =>
                h.Giver == hand.Receiver && h.Receiver == hand.Giver && h.GiverKind == PlayerKind.Human);
            Assert.Equal(hand.GiverKind == PlayerKind.Human && inverse, score.Reciprocal);
        }
    }

    [Property(MaxTest = Runs)]
    public void Each_receiver_has_at_most_one_last_stand_and_it_is_in_the_window(int seed)
    {
        var snapshot = RandomDay.Generate(seed);
        var outcome = NightResolver.Resolve(snapshot);
        var windowStart = snapshot.Day.MidnightAt - snapshot.Rules.LastRampartWindow;

        var lastStands = outcome.Hands
            .Where(s => s.LastRampart)
            .Select(s => snapshot.Hands.Single(h => h.Id == s.HandId))
            .ToList();

        Assert.All(lastStands, h => Assert.True(h.GivenAt >= windowStart));
        Assert.Equal(lastStands.Count, lastStands.Select(h => h.Receiver).Distinct().Count());
    }

    [Property(MaxTest = Runs)]
    public void Awards_add_up_to_the_points_of_the_hands(int seed)
    {
        var outcome = NightResolver.Resolve(RandomDay.Generate(seed));

        Assert.Equal(outcome.Hands.Sum(h => h.Points), outcome.Awards.Sum(a => a.Points));
        Assert.All(outcome.Awards, a => Assert.True(a.Points > 0));
    }

    [Property(MaxTest = Runs)]
    public void Life_debts_only_exist_for_survivors_who_had_exactly_their_threshold(int seed)
    {
        var snapshot = RandomDay.Generate(seed);
        var outcome = NightResolver.Resolve(snapshot);

        foreach (var debt in outcome.LifeDebts)
        {
            var saved = outcome.Players.Single(p => p.PlayerId == debt.Saved);
            Assert.Equal(NightResult.Survived, saved.Result);
            Assert.Equal(saved.Required, saved.Received);
            Assert.Contains(snapshot.Hands, h =>
                h.Giver == debt.Saver && h.Receiver == debt.Saved && h.GiverKind == PlayerKind.Human);
        }
    }

    [Property(MaxTest = Runs)]
    public void World_counters_follow_the_falls(int seed)
    {
        var snapshot = RandomDay.Generate(seed);
        var outcome = NightResolver.Resolve(snapshot);

        Assert.Equal(outcome.Falls == 0 ? snapshot.PreviousStreak + 1 : 0, outcome.Streak);
        Assert.Equal(snapshot.PreviousDeathsAllTime + outcome.Falls, outcome.DeathsAllTime);
    }

    [Property(MaxTest = Runs)]
    public void The_outcome_does_not_depend_on_input_order(int seed)
    {
        var snapshot = RandomDay.Generate(seed);
        var shuffled = snapshot with
        {
            Players = snapshot.Players.Reverse().ToList(),
            Hands = snapshot.Hands.Reverse().ToList(),
        };

        var a = NightResolver.Resolve(snapshot);
        var b = NightResolver.Resolve(shuffled);

        Assert.Equal(a.Players, b.Players);
        Assert.Equal(a.Hands, b.Hands);
        Assert.Equal(a.Awards, b.Awards);
        Assert.Equal(a.LifeDebts, b.LifeDebts);
        Assert.Equal(a.HeldTogether, b.HeldTogether);
        Assert.Equal((a.Falls, a.Streak, a.DeathsAllTime), (b.Falls, b.Streak, b.DeathsAllTime));
    }
}
