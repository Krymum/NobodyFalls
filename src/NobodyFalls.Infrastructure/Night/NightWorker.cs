using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NobodyFalls.Application.Night;

namespace NobodyFalls.Infrastructure.Night;

/// <summary>
/// Ticks the night cycle every two seconds. Running on every instance is fine: the
/// work is idempotent and serialized by the database (ADR 0003).
/// </summary>
internal sealed partial class NightWorker(IServiceScopeFactory scopes, TimeProvider time, ILogger<NightWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<NightCycle>().TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failing tick must not stop the cycle; the next tick retries.
            catch (Exception e)
#pragma warning restore CA1031
            {
                LogTickFailed(e);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Night cycle tick failed; retrying at the next tick.")]
    private partial void LogTickFailed(Exception exception);
}
