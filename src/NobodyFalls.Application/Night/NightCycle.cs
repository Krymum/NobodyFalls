using Microsoft.Extensions.Logging;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Domain.Common;

namespace NobodyFalls.Application.Night;

/// <summary>
/// Drives the day/night cycle. Called every few seconds by a background worker on every
/// server instance; the store's locks make concurrent ticks harmless.
/// </summary>
public sealed partial class NightCycle(INightStore store, IClock clock, ILogger<NightCycle> logger)
{
    public async Task<IReadOnlyList<NightRunResult>> TickAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var results = new List<NightRunResult>();

        // Catch up in order. Each resolution starts the following day, which may itself be
        // overdue (after downtime), so look again until nothing is left to resolve.
        while (true)
        {
            var pending = await store.GetDaysToResolveAsync(now, cancellationToken);
            var progressed = false;
            foreach (var day in pending)
            {
                var result = await store.ResolveAsync(day, now, cancellationToken);
                results.Add(result);
                if (result.Status == NightRunStatus.Resolved)
                {
                    progressed = true;
                    LogResolved(day, result.Falls, result.Streak);
                }
            }

            if (!progressed)
            {
                break;
            }
        }

        var today = GameDay.Of(now);
        if (await store.EnsureDayStartedAsync(today, now, cancellationToken))
        {
            LogDayStarted(today);
        }

        var watchers = await store.RunWatcherPassAsync(today, now, cancellationToken);
        if (watchers.Ran)
        {
            LogWatcherPass(today, watchers.HandsGiven);
        }

        return results;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Night {Day} resolved: {Falls} falls, world streak {Streak}.")]
    private partial void LogResolved(GameDay day, int falls, int streak);

    [LoggerMessage(Level = LogLevel.Information, Message = "Day {Day} started.")]
    private partial void LogDayStarted(GameDay day);

    [LoggerMessage(Level = LogLevel.Information, Message = "Watchers gave {Hands} hands on {Day}.")]
    private partial void LogWatcherPass(GameDay day, int hands);
}
