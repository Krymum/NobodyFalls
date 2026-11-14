using Microsoft.AspNetCore.Authentication;
using NobodyFalls.Api.Auth;
using NobodyFalls.Api.Hosting;
using NobodyFalls.Api.Http;
using NobodyFalls.Application.Audit;
using NobodyFalls.Application.Staff;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Staff;

namespace NobodyFalls.Api.Admin;

public sealed record AdminCodeRequest(string? Code, string? RecoveryCode);

public sealed record StaffAddRequest(string? Pseudo, IReadOnlyList<string>? Roles);

public sealed record StaffRolesRequest(IReadOnlyList<string>? Roles);

/// <summary>The admin API, <c>/api/v1/admin</c> (ADR 0017). Every route declares its access.</summary>
internal static class AdminEndpoints
{
    public const string Prefix = "/api/v1/admin";

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup(Prefix)
            .AddEndpointFilter<RequireApiHeader>()
            .AddEndpointFilter<AdminAccessFilter>()
            .WithTags("Admin");

        MapSession(admin.MapGroup("/session"));
        MapStaff(admin.MapGroup("/staff"));
        MapAudit(admin.MapGroup("/audit"));
    }

    private static void MapSession(RouteGroupBuilder session)
    {
        session.MapGet("", async (HttpContext http, TimeProvider time) =>
        {
            var member = http.Member();
            var admin = await http.AuthenticateAsync(AdminPrincipal.Scheme);
            var signedIn = admin.Succeeded && admin.Principal.PlayerId() == member.PlayerId;
            return Results.Ok(new
            {
                playerId = member.PlayerId.Value,
                pseudo = member.Pseudo,
                roles = member.Roles.Select(r => r.Name()),
                permissions = PermissionMatrix.Of(member.Roles).Order().Select(p => p.Name()),
                totpEnrolled = member.TotpEnrolled,
                signedIn,
                reauthValidUntil = signedIn ? admin.Principal!.ReauthAt() + AdminPrincipal.ReauthWindow : null,
                serverNow = time.GetUtcNow(),
            });
        }).MemberOnly();

        session.MapPost("/totp/enroll", async (HttpContext http, AdminAuthentication auth, CancellationToken ct) =>
        {
            var result = await auth.BeginEnrollmentAsync(http.Member(), ct);
            return result.IsSuccess
                ? Results.Ok(new { secret = result.Value.Secret, uri = result.Value.ProvisioningUri })
                : Problems.From(result.Error!);
        }).MemberOnly().RequireRateLimiting(RateLimits.AdminSignIn);

        session.MapPost("/totp/confirm", async (AdminCodeRequest body, HttpContext http, AdminAuthentication auth, ISessionVersions sessions, TimeProvider time, CancellationToken ct) =>
        {
            var member = http.Member();
            var result = await auth.ConfirmEnrollmentAsync(member, body.Code, http.Actor(), ct);
            if (result.IsFailure)
            {
                return Problems.From(result.Error!);
            }

            await OpenSessionAsync(http, member.PlayerId, sessions, time, ct);
            return Results.Ok(new { recoveryCodes = result.Value });
        }).MemberOnly().RequireRateLimiting(RateLimits.AdminSignIn);

        session.MapPost("/login", async (AdminCodeRequest body, HttpContext http, AdminAuthentication auth, ISessionVersions sessions, TimeProvider time, CancellationToken ct) =>
        {
            var member = http.Member();
            var result = await auth.SignInAsync(member, body.Code, body.RecoveryCode, http.Actor(), ct);
            if (result.IsFailure)
            {
                return Problems.From(result.Error!);
            }

            await OpenSessionAsync(http, member.PlayerId, sessions, time, ct);
            return Results.Ok(new { method = result.Value == AdminSignInMethod.RecoveryCode ? "recovery_code" : "authenticator" });
        }).MemberOnly().RequireRateLimiting(RateLimits.AdminSignIn);

        session.MapPost("/reauth", async (AdminCodeRequest body, HttpContext http, AdminAuthentication auth, ISessionVersions sessions, TimeProvider time, CancellationToken ct) =>
        {
            var member = http.Member();
            var result = await auth.ReauthenticateAsync(member, body.Code, http.Actor(), ct);
            if (result.IsFailure)
            {
                return Problems.From(result.Error!);
            }

            var admin = await http.AuthenticateAsync(AdminPrincipal.Scheme);
            var now = time.GetUtcNow();
            var version = await sessions.GetAsync(member.PlayerId, ct);
            await AdminPrincipal.SignInAsync(http, member.PlayerId, version, admin.Principal?.AuthTime() ?? now, now);
            return Results.Ok(new { reauthValidUntil = now + AdminPrincipal.ReauthWindow });
        }).SignedInOnly().RequireRateLimiting(RateLimits.AdminSignIn);

        session.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(AdminPrincipal.Scheme);
            return Results.NoContent();
        }).MemberOnly();
    }

    private static void MapStaff(RouteGroupBuilder staff)
    {
        staff.MapGet("", async (IStaffStore store, CancellationToken ct) =>
            Results.Ok((await store.ListAsync(ct)).Select(ToView))).Requires(Permission.StaffManage);

        staff.MapPost("", async (StaffAddRequest body, HttpContext http, IStaffStore store, CancellationToken ct) =>
        {
            var roles = StaffRoles.ParseSet(body.Roles);
            if (roles.IsFailure)
            {
                return Problems.From(roles.Error!);
            }

            var result = await store.AddAsync(body.Pseudo ?? "", roles.Value, http.Actor(), ct);
            return result.IsSuccess ? Results.Ok(ToView(result.Value)) : Problems.From(result.Error!);
        }).Requires(Permission.StaffManage, sensitive: true);

        staff.MapPut("/{playerId:guid}", async (Guid playerId, StaffRolesRequest body, HttpContext http, IStaffStore store, CancellationToken ct) =>
        {
            var roles = StaffRoles.ParseSet(body.Roles);
            if (roles.IsFailure)
            {
                return Problems.From(roles.Error!);
            }

            var result = await store.ChangeRolesAsync(new PlayerId(playerId), roles.Value, http.Actor(), ct);
            return result.IsSuccess ? Results.Ok(ToView(result.Value)) : Problems.From(result.Error!);
        }).Requires(Permission.StaffManage, sensitive: true);

        staff.MapDelete("/{playerId:guid}", async (Guid playerId, HttpContext http, IStaffStore store, CancellationToken ct) =>
            Problems.Of(await store.RemoveAsync(new PlayerId(playerId), http.Actor(), ct))).Requires(Permission.StaffManage, sensitive: true);

        staff.MapPost("/{playerId:guid}/totp/reset", async (Guid playerId, HttpContext http, ITotpStore totp, CancellationToken ct) =>
            Problems.Of(await totp.ResetAsync(new PlayerId(playerId), http.Actor(), ct))).Requires(Permission.StaffManage, sensitive: true);
    }

    private static void MapAudit(RouteGroupBuilder audit)
    {
        audit.MapGet("", async (
            Guid? actor, string? action, string? targetType, string? targetId,
            DateTimeOffset? from, DateTimeOffset? to, long? before, int? limit,
            HttpContext http, IAuditLog log, CancellationToken ct) =>
        {
            var query = new AuditQuery(actor, Blank(action), Blank(targetType), Blank(targetId), from, to, before, limit ?? 50);
            var page = await log.ListAsync(query, ct);

            // Reading the log is itself recorded (first page only, so that scrolling stays quiet).
            if (before is null)
            {
                await log.AppendAsync(
                    new AuditRecord(http.Actor(), AuditActions.AuditViewed, AuditTargets.Audit, null, After: new { actor, action, targetType, targetId, from, to }),
                    ct);
            }

            return Results.Ok(page);
        }).Requires(Permission.AuditView);

        audit.MapGet("/verify", async (HttpContext http, IAuditLog log, CancellationToken ct) =>
        {
            var verification = await log.VerifyAsync(ct);
            await log.AppendAsync(new AuditRecord(http.Actor(), AuditActions.AuditVerified, AuditTargets.Audit, null, After: verification), ct);
            return Results.Ok(verification);
        }).Requires(Permission.AuditView);
    }

    private static async Task OpenSessionAsync(HttpContext http, PlayerId player, ISessionVersions sessions, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await AdminPrincipal.SignInAsync(http, player, await sessions.GetAsync(player, ct), now, now);
    }

    private static object ToView(StaffMember m) => new
    {
        playerId = m.PlayerId.Value,
        pseudo = m.Pseudo,
        roles = m.Roles.Select(r => r.Name()),
        totpEnrolled = m.TotpEnrolled,
        grantedAt = m.GrantedAt,
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
