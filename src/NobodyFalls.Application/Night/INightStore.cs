using NobodyFalls.Domain.Common;

namespace NobodyFalls.Application.Night;

public enum NightRunStatus
{
    Resolved,

    /// <summary>Another run already resolved this day: nothing was changed.</summary>
    AlreadyResolved,

    /// <summary>The day was never started (no season was open).</summary>
    NotStarted,

    /// <summary>Its midnight has not been reached yet.</summary>
    TooEarly,
}

public sealed record NightRunResult(
    GameDay Day,
    NightRunStatus Status,
    int Falls = 0,
    int Streak = 0,
    long DeathsAllTime = 0,
    bool NextDayStarted = false);

public sealed record WatcherPassResult(GameDay Day, bool Ran, int HandsGiven);

/// <summary>
/// Persistence side of the night cycle. Every method is idempotent and safe to call
/// from several server instances at once: they serialize on a PostgreSQL advisory lock.
/// </summary>
public interface INightStore
{
    /// <summary>Started days whose midnight is at or before <paramref name="now"/> and that are not resolved, oldest first.</summary>
    Task<IReadOnlyList<GameDay>> GetDaysToResolveAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Resolves the midnight ending <paramref name="day"/> and starts the next day.</summary>
    Task<NightRunResult> ResolveAsync(GameDay day, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Starts <paramref name="day"/> if a season is open and it was not started by the
    /// previous midnight (season opening, or the very first day). Returns true if it did.
    /// </summary>
    Task<bool> EnsureDayStartedAsync(GameDay day, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Runs the Watcher pass of <paramref name="day"/> if its time has come and it has not run yet.</summary>
    Task<WatcherPassResult> RunWatcherPassAsync(GameDay day, DateTimeOffset now, CancellationToken cancellationToken);
}
