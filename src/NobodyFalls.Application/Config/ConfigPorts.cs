using NobodyFalls.Application.Audit;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Config;
using NobodyFalls.Domain.Rules;

namespace NobodyFalls.Application.Config;

/// <summary>The rules in force: the latest version, and the one today was started with.</summary>
public sealed record RulesState(int LatestVersion, int TodayVersion, GameRules Rules);

public sealed record RulesVersion(int Version, DateTimeOffset CreatedAt, string? Author, string Comment, GameRules Rules);

public static class RulesErrors
{
    public static readonly DomainError Missing = new("rules.missing", "Send the complete rules.");
    public static readonly DomainError Comment = new("rules.comment", "Say why the rules change (3 to 200 characters).");
    public static readonly DomainError Unchanged = new("rules.unchanged", "These rules are already the latest version.");
}

/// <summary>
/// Game rules (ADR 0005): every save is a new version, applied from the next game day, and
/// audited with what changed. Going back to an older version saves it again as a new one.
/// </summary>
public interface IRulesConfig
{
    Task<RulesState> GetAsync(GameDay today, CancellationToken cancellationToken);

    Task<IReadOnlyList<RulesVersion>> VersionsAsync(int limit, CancellationToken cancellationToken);

    Task<Result<int>> SaveAsync(GameRules? rules, string? comment, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record ScheduledStorm(string Day, DateTimeOffset CreatedAt, string? Author);

public static class StormErrors
{
    public static readonly DomainError Day = new("storm.day", "Give the day of the storm (YYYY-MM-DD).");
    public static readonly DomainError AlreadyScheduled = new("storm.already_scheduled", "A storm is already planned that day.");
    public static readonly DomainError NotFound = new("storm.not_found", "No storm is planned that day.");
}

/// <summary>Storms (GAME_DESIGN §4): planned or cancelled only for a day that has not started.</summary>
public interface IStormSchedule
{
    /// <summary>Storms from <paramref name="from"/> on, soonest first.</summary>
    Task<IReadOnlyList<ScheduledStorm>> ListAsync(GameDay from, CancellationToken cancellationToken);

    Task<Result> ScheduleAsync(GameDay day, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken);

    Task<Result> CancelAsync(GameDay day, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record SeasonView(int Number, string Name, string OpeningDay, string? LastDay, long Deaths);

/// <summary>The calendar of seasons; see <c>SeasonCalendar</c> for what may change.</summary>
public interface ISeasonConfig
{
    Task<IReadOnlyList<SeasonView>> ListAsync(CancellationToken cancellationToken);

    Task<Result<int>> PlanAsync(string? name, GameDay openingDay, GameDay? lastDay, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken);

    Task<Result> ChangeAsync(int number, string? name, GameDay openingDay, GameDay? lastDay, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record FlagState(string Key, string Description, bool Enabled, bool Default, DateTimeOffset? UpdatedAt, string? UpdatedBy);

/// <summary>Feature flags (ADR 0022), read through a short cache by every request that needs one.</summary>
public interface IFeatureFlags
{
    Task<bool> IsEnabledAsync(FeatureFlag flag, CancellationToken cancellationToken);

    Task<IReadOnlyList<FlagState>> ListAsync(CancellationToken cancellationToken);

    Task<Result> SetAsync(string key, bool? enabled, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Reads the flags again on next use, after a change made elsewhere (the dev reset).</summary>
    void Forget();
}
