using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Insights;

namespace NobodyFalls.Infrastructure.Insights;

/// <summary>
/// Forgets connection traces (IP addresses, devices) after 90 days (decision Q5), once an
/// hour. Idempotent: several instances may run it.
/// </summary>
internal sealed partial class RetentionWorker(IActivityRecorder activity, IClock clock, TimeProvider time, ILogger<RetentionWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Keep = TimeSpan.FromDays(90);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), time);
        do
        {
            try
            {
                var purged = await activity.PurgeAsync(clock.UtcNow - Keep, stoppingToken);
                if (purged > 0)
                {
                    LogPurged(purged);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // A failing round must not stop the worker; the next one retries.
            catch (Exception e)
#pragma warning restore CA1031
            {
                LogFailed(e);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Forgot {Count} connection traces older than 90 days.")]
    private partial void LogPurged(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Purging connection traces failed; retrying in an hour.")]
    private partial void LogFailed(Exception exception);
}
