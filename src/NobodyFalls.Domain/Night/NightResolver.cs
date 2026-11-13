using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Friends;
using NobodyFalls.Domain.Players;

namespace NobodyFalls.Domain.Night;

/// <summary>
/// Reference implementation of the midnight resolution (GAME_DESIGN §2).
/// </summary>
/// <remarks>
/// Production runs the same rules as set-based SQL; differential tests keep both in
/// sync (see ADR 0003). Keep this class obvious rather than fast: it is the
/// executable specification.
/// </remarks>
public static class NightResolver
{
    public static NightOutcome Resolve(DaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureConsistent(snapshot);

        var day = snapshot.Day;
        var rules = snapshot.Rules;

        // Hands in the order they were given: the order decides which hand reached the threshold.
        var hands = snapshot.Hands
            .OrderBy(h => h.GivenAt)
            .ThenBy(h => h.Id)
            .ToList();

        var handsByReceiver = hands
            .GroupBy(h => h.Receiver)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 1. Who falls.
        var results = snapshot.Players
            .OrderBy(p => p.PlayerId)
            .Select(p =>
            {
                var received = handsByReceiver.TryGetValue(p.PlayerId, out var list) ? list.Count : 0;
                var result = p.Immune ? NightResult.Immune
                    : received >= p.Required ? NightResult.Survived
                    : NightResult.Fell;
                return new PlayerNightResult(p.PlayerId, received, p.Required, result);
            })
            .ToList();
        var resultByPlayer = results.ToDictionary(r => r.PlayerId);

        // 2. Guardian points, hand by hand.
        var humanLinks = hands
            .Where(h => h.GiverKind == PlayerKind.Human)
            .Select(h => (h.Giver, h.Receiver))
            .ToHashSet();
        var lastRampartFrom = day.MidnightAt - rules.LastRampartWindow;

        var scores = new List<HandScore>(hands.Count);
        foreach (var hand in hands)
        {
            var isHuman = hand.GiverKind == PlayerKind.Human;
            var reciprocal = isHuman && humanLinks.Contains((hand.Receiver, hand.Giver));
            resultByPlayer.TryGetValue(hand.Receiver, out var receiver);
            var receiverMadeIt = receiver is { Result: NightResult.Survived or NightResult.Immune };

            var lastRampart = isHuman
                && !reciprocal
                && receiver is { Result: NightResult.Survived }
                && hand.GivenAt >= lastRampartFrom
                && RankOf(hand, handsByReceiver[hand.Receiver]) == receiver.Required;

            var points = isHuman && receiverMadeIt && !reciprocal
                ? (hand.GiverWasFriend ? rules.PointsFriend : rules.PointsStranger)
                  + (lastRampart ? rules.LastRampartBonus : 0)
                : 0;

            scores.Add(new HandScore(hand.Id, points, reciprocal, lastRampart));
        }

        var pointsByHand = scores.ToDictionary(s => s.HandId, s => s.Points);
        var awards = hands
            .GroupBy(h => h.Giver)
            .Select(g => new GuardianAward(g.Key, g.Sum(h => pointsByHand[h.Id])))
            .Where(a => a.Points > 0)
            .OrderBy(a => a.PlayerId)
            .ToList();

        // 3. Life debts: when a survivor had exactly the hands they needed, every one of them was decisive.
        var lifeDebts = results
            .Where(r => r.Result == NightResult.Survived && r.Required > 0 && r.Received == r.Required)
            .SelectMany(r => handsByReceiver[r.PlayerId]
                .Where(h => h.GiverKind == PlayerKind.Human)
                .Select(h => new LifeDebt(r.PlayerId, h.Giver)))
            .Distinct()
            .OrderBy(d => d.Saved)
            .ThenBy(d => d.Saver)
            .ToList();

        // 4. Pairs who held each other (either direction) feed friend suggestions.
        var heldTogether = hands
            .Where(h => h.GiverKind == PlayerKind.Human)
            .Select(h => PlayerPair.Of(h.Giver, h.Receiver))
            .Distinct()
            .OrderBy(p => p.Low)
            .ThenBy(p => p.High)
            .ToList();

        // 5. World counters.
        var falls = results.Count(r => r.Result == NightResult.Fell);
        var streak = falls == 0 ? snapshot.PreviousStreak + 1 : 0;

        return new NightOutcome(
            day,
            results,
            scores.OrderBy(s => s.HandId).ToList(),
            awards,
            lifeDebts,
            heldTogether,
            falls,
            streak,
            snapshot.PreviousDeathsAllTime + falls);
    }

    /// <summary>1-based position of <paramref name="hand"/> among the hands its receiver got, in giving order.</summary>
    private static int RankOf(HandRecord hand, List<HandRecord> receivedInOrder) =>
        receivedInOrder.IndexOf(hand) + 1;

    /// <summary>
    /// The database guarantees these invariants; checking them here keeps the reference
    /// implementation honest and makes broken test data fail loudly.
    /// </summary>
    private static void EnsureConsistent(DaySnapshot snapshot)
    {
        var players = new HashSet<PlayerId>();
        foreach (var player in snapshot.Players)
        {
            if (!players.Add(player.PlayerId))
            {
                throw new ArgumentException($"Player {player.PlayerId} appears twice.", nameof(snapshot));
            }
        }

        var handIds = new HashSet<long>();
        var links = new HashSet<(PlayerId, PlayerId)>();
        foreach (var hand in snapshot.Hands)
        {
            if (!handIds.Add(hand.Id))
            {
                throw new ArgumentException($"Hand {hand.Id} appears twice.", nameof(snapshot));
            }

            if (hand.Giver == hand.Receiver)
            {
                throw new ArgumentException($"Hand {hand.Id}: a player cannot hold themselves.", nameof(snapshot));
            }

            if (!links.Add((hand.Giver, hand.Receiver)))
            {
                throw new ArgumentException($"Hand {hand.Id}: same giver and receiver twice in a day.", nameof(snapshot));
            }

            if (!snapshot.Day.Contains(hand.GivenAt))
            {
                throw new ArgumentException($"Hand {hand.Id} was not given on {snapshot.Day}.", nameof(snapshot));
            }
        }
    }
}
