using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Night;
using NobodyFalls.Domain.Players;
using NobodyFalls.Domain.Rules;

namespace NobodyFalls.Domain.Tests.Support;

/// <summary>
/// Generates valid random days from a seed. Shared with the differential tests that
/// compare the SQL resolution with <see cref="NightResolver"/>.
/// </summary>
internal static class RandomDay
{
    public static DaySnapshot Generate(int seed)
    {
        var rng = new Random(seed);
        var day = GameDay.Parse("2026-11-13").AddDays(rng.Next(0, 400));

        var humans = Enumerable.Range(1, rng.Next(1, 30)).Select(Ids.Numbered).ToList();
        var watchers = Enumerable.Range(1000, rng.Next(0, 4)).Select(Ids.Numbered).ToList();

        // Some humans gave hands without being in play (e.g. fallen players who have not returned yet).
        var inPlay = humans.Where(_ => rng.NextDouble() < 0.9).ToList();
        var players = inPlay
            .Select(id => new PlayerDayEntry(id, rng.Next(0, 5), rng.NextDouble() < 0.1))
            .ToList();

        var hands = new List<HandRecord>();
        long nextId = rng.Next(1, 1000);
        foreach (var giver in humans.Concat(watchers))
        {
            var kind = watchers.Contains(giver) ? PlayerKind.Watcher : PlayerKind.Human;
            var receivers = inPlay
                .Where(r => r != giver)
                .OrderBy(_ => rng.Next())
                .Take(rng.Next(0, 4));
            foreach (var receiver in receivers)
            {
                hands.Add(new HandRecord(
                    nextId += rng.Next(1, 5),
                    giver,
                    kind,
                    receiver,
                    RandomInstant(rng, day),
                    kind == PlayerKind.Human && rng.NextDouble() < 0.4));
            }
        }

        return new DaySnapshot(
            day,
            GameRules.Default,
            players.OrderBy(_ => rng.Next()).ToList(),
            hands.OrderBy(_ => rng.Next()).ToList(),
            rng.Next(0, 60),
            rng.Next(0, 5000));
    }

    /// <summary>A third of the hands land in the last hour, with deliberate ties, to exercise the "last stand" rule.</summary>
    private static DateTimeOffset RandomInstant(Random rng, GameDay day)
    {
        if (rng.NextDouble() < 0.33)
        {
            return day.MidnightAt - TimeSpan.FromMinutes(rng.Next(1, 75));
        }

        return day.StartsAt + TimeSpan.FromSeconds(rng.Next(0, 86_400));
    }
}
