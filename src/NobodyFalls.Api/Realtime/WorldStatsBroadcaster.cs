using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Queries;
using NobodyFalls.Application.Realtime;

namespace NobodyFalls.Api.Realtime;

/// <summary>
/// Keeps the world counters (players alive, under the threshold) live on every screen,
/// with one cheap query every few seconds instead of an event per hand.
/// </summary>
internal sealed partial class WorldStatsBroadcaster(
    IGameQueries queries,
    IRealtimePublisher realtime,
    IClock clock,
    TimeProvider time,
    ILogger<WorldStatsBroadcaster> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        WorldStats? last = null;
        using var timer = new PeriodicTimer(Interval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var world = await queries.WorldAsync(clock.UtcNow, stoppingToken);
                var stats = new WorldStats(world.Alive, world.UnderThreshold);
                if (stats != last)
                {
                    realtime.Publish(Audience.All, stats);
                    last = stats;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Best effort; the next tick retries.
            catch (Exception e)
#pragma warning restore CA1031
            {
                LogFailed(e);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "World counters could not be broadcast.")]
    private partial void LogFailed(Exception exception);
}
