using NobodyFalls.Application.Audit;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Security;
using NobodyFalls.Domain.Staff;

namespace NobodyFalls.Application.Staff;

/// <summary>What the server needs on every admin request: the roles held now, and 2FA status.</summary>
public sealed record StaffAccess(PlayerId PlayerId, string Pseudo, IReadOnlyList<StaffRole> Roles, bool TotpEnrolled);

public sealed record StaffMember(PlayerId PlayerId, string Pseudo, IReadOnlyList<StaffRole> Roles, bool TotpEnrolled, DateTimeOffset GrantedAt);

/// <summary>The team (ADR 0018). Every change is audited in its own transaction.</summary>
public interface IStaffStore
{
    /// <summary>The member's access, or null for a player without a role. Read on every admin request.</summary>
    Task<StaffAccess?> GetAccessAsync(PlayerId player, CancellationToken cancellationToken);

    Task<IReadOnlyList<StaffMember>> ListAsync(CancellationToken cancellationToken);

    Task<Result<StaffMember>> AddAsync(string pseudo, IReadOnlyList<StaffRole> roles, AuditActor actor, CancellationToken cancellationToken);

    Task<Result<StaffMember>> ChangeRolesAsync(PlayerId target, IReadOnlyList<StaffRole> roles, AuditActor actor, CancellationToken cancellationToken);

    Task<Result> RemoveAsync(PlayerId target, AuditActor actor, CancellationToken cancellationToken);

    /// <summary>Makes the first admin; refused once an admin exists (operator command).</summary>
    Task<Result<StaffMember>> BootstrapAdminAsync(string pseudo, string reason, CancellationToken cancellationToken);
}

/// <summary>A member's authenticator, as stored.</summary>
public sealed record TotpCredential(byte[] ProtectedSecret, bool Confirmed, long? LastStep, int FailedAttempts, DateTimeOffset? LockedUntil);

public sealed record StoredRecovery(long Id, StoredRecoveryCode Code);

public interface ITotpStore
{
    Task<TotpCredential?> GetAsync(PlayerId player, CancellationToken cancellationToken);

    /// <summary>Stores a new secret awaiting confirmation; refused (false) once one is confirmed.</summary>
    Task<bool> SavePendingAsync(PlayerId player, byte[] protectedSecret, CancellationToken cancellationToken);

    /// <summary>Confirms the pending secret, consumes <paramref name="timeStep"/>, stores the recovery codes and audits it.</summary>
    Task<bool> ConfirmAsync(PlayerId player, long timeStep, IReadOnlyList<StoredRecoveryCode> recoveryCodes, AuditActor actor, CancellationToken cancellationToken);

    /// <summary>
    /// Consumes a time step if it is newer than the last one used, and clears failures.
    /// Atomic: two requests with the same code cannot both succeed.
    /// </summary>
    Task<bool> TryUseStepAsync(PlayerId player, long timeStep, CancellationToken cancellationToken);

    /// <summary>Counts a failure; locks the credential until <paramref name="lockedUntil"/> once <paramref name="maxAttempts"/> is reached.</summary>
    Task<(int Attempts, bool Locked)> RecordFailureAsync(PlayerId player, int maxAttempts, DateTimeOffset lockedUntil, CancellationToken cancellationToken);

    Task<IReadOnlyList<StoredRecovery>> UnusedRecoveryCodesAsync(PlayerId player, CancellationToken cancellationToken);

    /// <summary>Marks a recovery code used; false if it was used meanwhile.</summary>
    Task<bool> UseRecoveryCodeAsync(long id, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Removes the authenticator and its codes, ends the member's sessions, and audits it.</summary>
    Task<Result> ResetAsync(PlayerId target, AuditActor actor, CancellationToken cancellationToken);
}

/// <summary>Encrypts secrets at rest (Data Protection).</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] secret);

    byte[] Unprotect(byte[] protectedSecret);
}

/// <summary>
/// Each player has a session version written into their cookies. Bumping it ends every
/// session of that player at once (ban, authenticator reset) (ADR 0021).
/// </summary>
public interface ISessionVersions
{
    Task<int> GetAsync(PlayerId player, CancellationToken cancellationToken);
}
