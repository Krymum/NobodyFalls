using NobodyFalls.Domain.Common;

namespace NobodyFalls.Domain.Rules;

/// <summary>A player the Watchers could hold.</summary>
public sealed record WatcherCandidate(PlayerId PlayerId, int Received, bool Immune, PlayerStanding Standing);

/// <summary>A Watcher and the hands it can still give today.</summary>
public sealed record WatcherSlot(PlayerId Watcher, int HandsLeft);

public sealed record WatcherHand(PlayerId Watcher, PlayerId Receiver);

/// <summary>
/// Watchers hold the players nobody holds while the population is small
/// (GAME_DESIGN §3). They are always labelled as Watchers and earn no points.
/// </summary>
public static class WatcherPolicy
{
    /// <summary>
    /// <c>ceil(max × (1 − alive / disappearAt))</c>, clamped to [0, max]: many Watchers
    /// when the world is empty, none once <see cref="WatcherRules.DisappearAtPopulation"/> is reached.
    /// </summary>
    public static int Count(GameRules rules, int alivePopulation)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var w = rules.Watchers;
        if (w.DisappearAtPopulation <= 0 || alivePopulation >= w.DisappearAtPopulation)
        {
            return 0;
        }

        var share = 1.0 - ((double)Math.Max(0, alivePopulation) / w.DisappearAtPopulation);
        return Math.Clamp((int)Math.Ceiling(w.MaxCount * share), 0, w.MaxCount);
    }

    /// <summary>
    /// Plans the Watcher pass: one hand for each player with no hand at all, fragile
    /// players first (they need more), then in a per-day shuffled order. Immune players
    /// are skipped since they cannot fall tonight. Re-running the pass after it happened
    /// plans nothing new, because those players are no longer at zero.
    /// </summary>
    public static IReadOnlyList<WatcherHand> Plan(
        GameDay day,
        IEnumerable<WatcherCandidate> candidates,
        IEnumerable<WatcherSlot> watchers)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(watchers);

        var queue = candidates
            .Where(c => c.Received == 0 && !c.Immune)
            .OrderBy(c => c.Standing == PlayerStanding.Fragile ? 0 : 1)
            .ThenBy(c => StableShuffle.Key(day, c.PlayerId))
            .ThenBy(c => c.PlayerId);

        // Spread hands across Watchers, so that each one shows up holding a few people.
        var slots = watchers
            .Where(s => s.HandsLeft > 0)
            .OrderBy(s => s.Watcher)
            .Select(s => new MutableSlot(s.Watcher, s.HandsLeft))
            .ToList();

        var plan = new List<WatcherHand>();
        var next = 0;
        foreach (var candidate in queue)
        {
            var slot = NextWithHands(slots, ref next);
            if (slot is null)
            {
                break;
            }

            plan.Add(new WatcherHand(slot.Watcher, candidate.PlayerId));
            slot.HandsLeft--;
        }

        return plan;
    }

    private static MutableSlot? NextWithHands(List<MutableSlot> slots, ref int next)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[(next + i) % slots.Count];
            if (slot.HandsLeft > 0)
            {
                next = (next + i + 1) % slots.Count;
                return slot;
            }
        }

        return null;
    }

    private sealed class MutableSlot(PlayerId watcher, int handsLeft)
    {
        public PlayerId Watcher { get; } = watcher;

        public int HandsLeft { get; set; } = handsLeft;
    }
}
