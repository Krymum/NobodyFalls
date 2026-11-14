using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NobodyFalls.Application.Push;

namespace NobodyFalls.Infrastructure.Push;

/// <summary>Drains the notification outbox every couple of seconds (ADR 0009).</summary>
internal sealed partial class NotificationWorker(IServiceScopeFactory scopes, TimeProvider time, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2), time);
        do
        {
            try
            {
                DispatchRun run;
                do
                {
                    await using var scope = scopes.CreateAsyncScope();
                    run = await scope.ServiceProvider.GetRequiredService<DispatchNotifications>().RunOnceAsync(stoppingToken);
                }
                while (run.Notifications == DispatchNotifications.BatchSize && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failing round must not stop the worker; the outbox keeps the work.
            catch (Exception e)
#pragma warning restore CA1031
            {
                LogFailed(e);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Sending notifications failed; retrying at the next tick.")]
    private partial void LogFailed(Exception exception);
}
