using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Hands;
using NobodyFalls.Domain.Common;

namespace NobodyFalls.Application.Dev;

/// <summary>Development-only data tools (GAME_DESIGN §14: seed N fake players, simulated clock).</summary>
public interface IDevStore
{
    /// <summary>Opens season 1 on <paramref name="openingDay"/> if no season exists yet.</summary>
    Task<bool> EnsureSeasonAsync(GameDay openingDay, CancellationToken cancellationToken);

    /// <summary>Creates fake players (marked as bots) with random characters and friendships.</summary>
    Task<IReadOnlyList<PlayerId>> SeedAsync(int players, int friendsPerPlayer, DateTimeOffset now, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlayerId>> BotsAsync(CancellationToken cancellationToken);

    /// <summary>Players in play today, most in need first.</summary>
    Task<IReadOnlyList<PlayerId>> InPlayByNeedAsync(GameDay day, CancellationToken cancellationToken);
}

public sealed record BotRun(int Bots, int HandsGiven);

/// <summary>Bots give their hands, mostly to those who need them, through the real rules.</summary>
public sealed class SimulateBots(IDevStore store, IHandLedger ledger, IClock clock)
{
    public async Task<BotRun> ExecuteAsync(double ratio, int seed, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var day = GameDay.Of(now);
        var random = new Random(seed);
        var bots = (await store.BotsAsync(cancellationToken)).Where(_ => random.NextDouble() < ratio).ToList();
        var targets = await store.InPlayByNeedAsync(day, cancellationToken);
        if (targets.Count == 0)
        {
            return new BotRun(bots.Count, 0);
        }

        var given = 0;
        foreach (var bot in bots)
        {
            for (var hand = 0; hand < 3; hand++)
            {
                // Bias towards the top of the list (those who need hands), like real players on the Edge.
                var index = (int)(Math.Pow(random.NextDouble(), 2) * targets.Count);
                var result = await ledger.GiveAsync(bot, targets[index], now, cancellationToken);
                if (result.IsSuccess)
                {
                    given++;
                }
            }
        }

        return new BotRun(bots.Count, given);
    }
}
