using System.Text.Json;
using NobodyFalls.Application.Audit;
using NobodyFalls.Domain.Common;

namespace NobodyFalls.Application.Insights;

/// <summary>A request seen from a player: where from and which device (decision Q5).</summary>
public sealed record AccessSeen(PlayerId Player, string Ip, string DeviceId, string? UserAgentFamily, bool SignIn);

/// <summary>
/// Records daily activity (retention) and connection traces (linked accounts), and purges
/// traces after 90 days (GAME_DESIGN §9, decision Q5).
/// </summary>
public interface IActivityRecorder
{
    Task RecordAsync(AccessSeen access, DateTimeOffset now, CancellationToken cancellationToken);

    Task<int> PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);
}

public sealed record NightPoint(string Day, int NightNumber, int Falls, int AliveAtStart, bool Storm);

public sealed record RetentionPoint(int AfterDays, int Cohort, int Returned);

public sealed record InvitationPoint(string Day, int Joined);

public sealed record SharedDevice(string DeviceId, int Accounts, IReadOnlyList<string> Pseudos);

public sealed record SignUpBurst(string Ip, int Accounts, DateTimeOffset Since);

/// <summary>The team's dashboard (GAME_DESIGN §9).</summary>
public sealed record Dashboard(
    int Alive,
    int Players,
    int VisitedToday,
    int Streak,
    long DeathsAllTime,
    IReadOnlyList<NightPoint> Nights,
    IReadOnlyList<RetentionPoint> Retention,
    IReadOnlyList<InvitationPoint> Invitations,
    int OpenReports,
    int OpenTickets,
    IReadOnlyList<SharedDevice> SharedDevices,
    IReadOnlyList<SignUpBurst> SignUpBursts);

public interface IDashboardQueries
{
    Task<Dashboard> GetAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record SeenAddress(string Ip, int Days, DateTimeOffset LastAt);

public sealed record SeenDevice(string DeviceId, string? Agent, int Days, DateTimeOffset LastAt);

public sealed record LinkedAccount(Guid Id, string Pseudo, string Status, bool SharesDevice, bool SharesIp, DateTimeOffset LastAt, DateTimeOffset? BannedUntil);

public sealed record AccountLinks(IReadOnlyList<SeenAddress> Addresses, IReadOnlyList<SeenDevice> Devices, IReadOnlyList<LinkedAccount> Accounts);

/// <summary>Other accounts seen on the same device or network (GAME_DESIGN §9: suspicious linked accounts).</summary>
public interface IAccountLinks
{
    Task<AccountLinks> LinksAsync(PlayerId player, CancellationToken cancellationToken);
}

public static class PersonalDataErrors
{
    public static readonly DomainError NotFound = new("gdpr.not_found", "This player cannot be found.");
    public static readonly DomainError Staff = new("gdpr.staff", "Take the team role away before deleting this account.");
    public static readonly DomainError Reason = new("gdpr.reason", "Give the reason (the request) in 3 to 500 characters.");
}

/// <summary>The rights of GDPR: a copy of one's data, and erasure (GAME_DESIGN §9).</summary>
public interface IPersonalData
{
    Task<JsonDocument?> ExportAsync(PlayerId player, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Erases the account: the player leaves the game for good, their sign-ins, devices, friends,
    /// requests and votes are deleted, their name is replaced; hands stay, anonymous, because the
    /// world's counters are built on them; the audit keeps identifiers only.
    /// </summary>
    Task<Result> DeleteAsync(PlayerId player, string reason, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken);
}
