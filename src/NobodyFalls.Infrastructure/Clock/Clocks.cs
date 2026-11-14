using Microsoft.Extensions.Hosting;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Infrastructure.Persistence;
using Npgsql;

namespace NobodyFalls.Infrastructure.Clock;

/// <summary>Real time. The only clock outside development.</summary>
internal sealed class SystemClock(TimeProvider time) : IClock
{
    public DateTimeOffset UtcNow => time.GetUtcNow();
}

/// <summary>
/// Development clock: real time plus an offset stored in <c>dev_clock</c>, so that every
/// server instance and the midnight job agree on the simulated time (ADR 0006).
/// The offset only ever moves forward through the dev tools; moving it back is allowed
/// but resolved days are never replayed.
/// </summary>
internal sealed class SimulatedClock(TimeProvider time, NpgsqlDataSource dataSource) : IClock, IClockControl
{
    private long _offsetTicks;

    public DateTimeOffset UtcNow => time.GetUtcNow() + Offset;

    public TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    public async Task SetOffsetAsync(TimeSpan offset, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            insert into dev_clock (id, offset_ms, updated_at) values (1, @offset, @now)
            on conflict (id) do update set offset_ms = excluded.offset_ms, updated_at = excluded.updated_at
            """);
        command.Parameters.AddWithValue("offset", (long)offset.TotalMilliseconds);
        command.Parameters.AddWithValue("now", Sql.Utc(time.GetUtcNow()));
        await command.ExecuteNonQueryAsync(cancellationToken);
        Interlocked.Exchange(ref _offsetTicks, offset.Ticks);
    }

    /// <summary>Reads the shared offset; called at start-up and then every second.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("select offset_ms from dev_clock where id = 1");
        var value = await command.ExecuteScalarAsync(cancellationToken);
        var offset = value is long ms ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero;
        Interlocked.Exchange(ref _offsetTicks, offset.Ticks);
    }
}

/// <summary>Keeps the simulated clock of this instance in sync with the shared offset.</summary>
internal sealed class SimulatedClockSync(SimulatedClock clock, TimeProvider time) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await clock.RefreshAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await clock.RefreshAsync(stoppingToken);
            }
            catch (NpgsqlException)
            {
                // The database may restart in development; keep the last known offset.
            }
        }
    }
}
