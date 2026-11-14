using System.Text.Json;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Staff;

namespace NobodyFalls.Application.Audit;

/// <summary>Who acts: a team member with the roles they held at that moment, or the system.</summary>
public sealed record AuditActor(PlayerId? Id, IReadOnlyList<StaffRole> Roles, string? Ip, string? RequestId)
{
    /// <summary>The game itself, or an operator command (bootstrap, dev tools).</summary>
    public static AuditActor System { get; } = new(null, [], null, null);
}

/// <summary>
/// One line of the audit log (ADR 0019): who, what (a stable action code), on what,
/// the state before and after, and why. <see cref="Before"/> and <see cref="After"/>
/// are serialized as JSON; keep them to the fields that changed.
/// </summary>
public sealed record AuditRecord(
    AuditActor Actor,
    string Action,
    string TargetType,
    string? TargetId,
    object? Before = null,
    object? After = null,
    string? Reason = null);

/// <summary>Stable action codes. Never reword one: the log is read by them.</summary>
public static class AuditActions
{
    public const string StaffBootstrapped = "staff.bootstrapped";
    public const string StaffAdded = "staff.added";
    public const string StaffRolesChanged = "staff.roles_changed";
    public const string StaffRemoved = "staff.removed";
    public const string StaffTotpReset = "staff.totp_reset";
    public const string TotpEnrolled = "admin.totp_enrolled";
    public const string SignedIn = "admin.signed_in";
    public const string SignInFailed = "admin.sign_in_failed";
    public const string SignInLocked = "admin.sign_in_locked";
    public const string AuditViewed = "audit.viewed";
    public const string AuditVerified = "audit.verified";
}

/// <summary>Stable target types.</summary>
public static class AuditTargets
{
    public const string Player = "player";
    public const string Audit = "audit";
}

public sealed record AuditQuery(
    Guid? ActorId = null,
    string? Action = null,
    string? TargetType = null,
    string? TargetId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    long? BeforeId = null,
    int Limit = 50);

public sealed record AuditEntry(
    long Id,
    DateTimeOffset At,
    Guid? ActorId,
    string? ActorPseudo,
    IReadOnlyList<string> ActorRoles,
    string Action,
    string TargetType,
    string? TargetId,
    JsonElement? Before,
    JsonElement? After,
    string? Reason,
    string? Ip);

/// <summary>A page of entries, newest first; <see cref="NextBeforeId"/> continues it.</summary>
public sealed record AuditPage(IReadOnlyList<AuditEntry> Entries, long? NextBeforeId);

/// <summary>Result of recomputing the hash chain: the first entry that does not match, if any.</summary>
public sealed record AuditVerification(bool Valid, long Checked, long? FirstBrokenId);

/// <summary>
/// The append-only audit log. Writes that belong to a change happen inside the store
/// that makes the change, in the same transaction; this port appends standalone entries
/// (sign-ins, reads of sensitive data) and reads the log.
/// </summary>
public interface IAuditLog
{
    Task AppendAsync(AuditRecord record, CancellationToken cancellationToken);

    Task<AuditPage> ListAsync(AuditQuery query, CancellationToken cancellationToken);

    Task<AuditVerification> VerifyAsync(CancellationToken cancellationToken);
}
