using NobodyFalls.Application.Audit;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Security;

namespace NobodyFalls.Application.Staff;

public static class AdminErrors
{
    public static readonly DomainError Forbidden = new("admin.forbidden", "Your roles do not allow this.");
    public static readonly DomainError TotpRequired = new("admin.totp_required", "Sign in to the admin with your authenticator first.");
    public static readonly DomainError ReauthRequired = new("admin.reauth_required", "Confirm with a fresh code to do this.");
    public static readonly DomainError TotpNotEnrolled = new("admin.totp_not_enrolled", "Set up your authenticator first.");
    public static readonly DomainError TotpAlreadyEnrolled = new("admin.totp_already_enrolled", "Your authenticator is already set up.");
    public static readonly DomainError TotpInvalid = new("admin.totp_invalid", "This code is not valid.");
    public static readonly DomainError TotpLocked = new("admin.totp_locked", "Too many wrong codes. Try again in a few minutes.");
}

public enum AdminSignInMethod
{
    Authenticator,
    RecoveryCode,
}

public sealed record TotpEnrollment(string Secret, string ProvisioningUri);

/// <summary>
/// Two-factor sign-in to the admin (ADR 0018): enrolment of an authenticator, sign-in
/// with a code or a recovery code, and re-authentication before sensitive actions.
/// Five wrong codes lock the authenticator for 15 minutes. Times are real time.
/// </summary>
public sealed class AdminAuthentication(
    ITotpStore totp,
    ISecretProtector protector,
    IAuditLog audit,
    TimeProvider time)
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public async Task<Result<TotpEnrollment>> BeginEnrollmentAsync(StaffAccess member, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(member);
        var secret = Totp.NewSecret();
        if (!await totp.SavePendingAsync(member.PlayerId, protector.Protect(secret), cancellationToken))
        {
            return AdminErrors.TotpAlreadyEnrolled;
        }

        return Result.Success(new TotpEnrollment(Base32.Encode(secret), Totp.ProvisioningUri(secret, member.Pseudo)));
    }

    /// <summary>Confirms the authenticator with a first code; returns the recovery codes, shown once.</summary>
    public async Task<Result<IReadOnlyList<string>>> ConfirmEnrollmentAsync(StaffAccess member, string? code, AuditActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(member);
        var credential = await totp.GetAsync(member.PlayerId, cancellationToken);
        if (credential is null)
        {
            return AdminErrors.TotpNotEnrolled;
        }

        if (credential.Confirmed)
        {
            return AdminErrors.TotpAlreadyEnrolled;
        }

        if (IsLocked(credential))
        {
            return AdminErrors.TotpLocked;
        }

        var step = Totp.Verify(protector.Unprotect(credential.ProtectedSecret), code, time.GetUtcNow(), credential.LastStep);
        if (step is null)
        {
            return await FailAsync(member, actor, cancellationToken);
        }

        var codes = RecoveryCodes.Generate();
        var confirmed = await totp.ConfirmAsync(member.PlayerId, step.Value, [.. codes.Select(RecoveryCodes.Protect)], actor, cancellationToken);
        return confirmed ? Result.Success(codes) : AdminErrors.TotpInvalid;
    }

    /// <summary>Checks a code (or a recovery code) to open an admin session.</summary>
    public async Task<Result<AdminSignInMethod>> SignInAsync(StaffAccess member, string? code, string? recoveryCode, AuditActor actor, CancellationToken cancellationToken)
    {
        var result = await CheckAsync(member, code, recoveryCode, actor, cancellationToken);
        if (result.IsSuccess)
        {
            await audit.AppendAsync(
                new AuditRecord(actor, AuditActions.SignedIn, AuditTargets.Player, member.PlayerId.ToString(), After: new { method = result.Value.ToString().ToLowerInvariant() }),
                cancellationToken);
        }

        return result;
    }

    /// <summary>Checks a fresh code before a sensitive action. Recovery codes are not accepted here.</summary>
    public async Task<Result> ReauthenticateAsync(StaffAccess member, string? code, AuditActor actor, CancellationToken cancellationToken)
    {
        var result = await CheckAsync(member, code, recoveryCode: null, actor, cancellationToken);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error!);
    }

    private async Task<Result<AdminSignInMethod>> CheckAsync(StaffAccess member, string? code, string? recoveryCode, AuditActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(member);
        var credential = await totp.GetAsync(member.PlayerId, cancellationToken);
        if (credential is not { Confirmed: true })
        {
            return AdminErrors.TotpNotEnrolled;
        }

        if (IsLocked(credential))
        {
            return AdminErrors.TotpLocked;
        }

        if (!string.IsNullOrWhiteSpace(recoveryCode))
        {
            foreach (var stored in await totp.UnusedRecoveryCodesAsync(member.PlayerId, cancellationToken))
            {
                if (RecoveryCodes.Matches(stored.Code, recoveryCode) && await totp.UseRecoveryCodeAsync(stored.Id, time.GetUtcNow(), cancellationToken))
                {
                    return Result.Success(AdminSignInMethod.RecoveryCode);
                }
            }

            return await FailAsync(member, actor, cancellationToken);
        }

        var step = Totp.Verify(protector.Unprotect(credential.ProtectedSecret), code, time.GetUtcNow(), credential.LastStep);
        if (step is not null && await totp.TryUseStepAsync(member.PlayerId, step.Value, cancellationToken))
        {
            return Result.Success(AdminSignInMethod.Authenticator);
        }

        return await FailAsync(member, actor, cancellationToken);
    }

    private bool IsLocked(TotpCredential credential) => credential.LockedUntil is { } until && until > time.GetUtcNow();

    private async Task<DomainError> FailAsync(StaffAccess member, AuditActor actor, CancellationToken cancellationToken)
    {
        var (attempts, locked) = await totp.RecordFailureAsync(member.PlayerId, MaxFailedAttempts, time.GetUtcNow() + LockDuration, cancellationToken);
        await audit.AppendAsync(
            new AuditRecord(actor, locked ? AuditActions.SignInLocked : AuditActions.SignInFailed, AuditTargets.Player, member.PlayerId.ToString(), After: new { attempts }),
            cancellationToken);
        return locked ? AdminErrors.TotpLocked : AdminErrors.TotpInvalid;
    }
}
