using Microsoft.AspNetCore.Authentication;
using NobodyFalls.Api.Auth;
using NobodyFalls.Api.Http;
using NobodyFalls.Application.Audit;
using NobodyFalls.Application.Staff;
using NobodyFalls.Domain.Staff;

namespace NobodyFalls.Api.Admin;

/// <summary>How much of the admin session an endpoint needs.</summary>
internal enum AdminGate
{
    /// <summary>A team role is enough (before two-factor sign-in: session, enrolment, sign-in).</summary>
    Member,

    /// <summary>A role and an open admin session, no particular permission (re-authentication).</summary>
    SignedIn,

    /// <summary>A role, an open admin session and the endpoint's permission.</summary>
    Permission,
}

/// <summary>The access an admin endpoint declares; read by <see cref="AdminAccessFilter"/> and the tests.</summary>
/// <param name="AnyOf">Allowed with any of these permissions (requests: support or appeals); the handler narrows further.</param>
internal sealed record AdminAccessMetadata(AdminGate Gate, Permission? Permission = null, bool Sensitive = false, IReadOnlyList<Permission>? AnyOf = null)
{
    public bool Allows(IReadOnlyCollection<StaffRole> roles) =>
        Permission is { } permission
            ? PermissionMatrix.Allows(roles, permission)
            : AnyOf is { Count: > 0 } any && any.Any(p => PermissionMatrix.Allows(roles, p));

    /// <summary>How the authorization tests print an endpoint's access.</summary>
    public string Describe() =>
        $"{Gate} {(Permission is { } p ? p.ToString() : AnyOf is { } any ? string.Join('|', any) : "")} {Sensitive}";
}

internal static class AdminAccess
{
    public static RouteHandlerBuilder Requires(this RouteHandlerBuilder builder, Permission permission, bool sensitive = false) =>
        builder.WithMetadata(new AdminAccessMetadata(AdminGate.Permission, permission, sensitive));

    public static RouteHandlerBuilder RequiresAny(this RouteHandlerBuilder builder, params Permission[] permissions) =>
        builder.WithMetadata(new AdminAccessMetadata(AdminGate.Permission, AnyOf: permissions));

    public static RouteHandlerBuilder MemberOnly(this RouteHandlerBuilder builder) =>
        builder.WithMetadata(new AdminAccessMetadata(AdminGate.Member));

    public static RouteHandlerBuilder SignedInOnly(this RouteHandlerBuilder builder) =>
        builder.WithMetadata(new AdminAccessMetadata(AdminGate.SignedIn));

    private const string MemberKey = "nf.admin.member";

    /// <summary>The team member making this request (set by the filter).</summary>
    public static StaffAccess Member(this HttpContext context) =>
        context.Items[MemberKey] as StaffAccess ?? throw new InvalidOperationException("Admin endpoints run behind AdminAccessFilter.");

    /// <summary>Who to write in the audit log for this request.</summary>
    public static AuditActor Actor(this HttpContext context)
    {
        var member = context.Member();
        return new AuditActor(member.PlayerId, member.Roles, context.Connection.RemoteIpAddress?.ToString(), context.TraceIdentifier);
    }

    internal static void SetMember(HttpContext context, StaffAccess member) => context.Items[MemberKey] = member;
}

/// <summary>
/// Guards every admin endpoint (ADR 0018, 0019), in this order: a player session (401),
/// a team role read from the database now (403), an admin session opened with a TOTP code
/// (403 <c>admin.totp_required</c>), the endpoint's permission (403), and for sensitive
/// actions a code confirmed less than five minutes ago (401 <c>admin.reauth_required</c>).
/// An endpoint that declares nothing is refused: the gate fails closed.
/// </summary>
internal sealed class AdminAccessFilter(IStaffStore staff) : IEndpointFilter
{
    private const string PrincipalKey = "nf.admin.principal";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var metadata = http.GetEndpoint()?.Metadata.GetMetadata<AdminAccessMetadata>();

        if (http.User.PlayerId() is not { } player)
        {
            return Problems.Code(StatusCodes.Status401Unauthorized, "auth.required", "Sign in first.");
        }

        var member = await staff.GetAccessAsync(player, http.RequestAborted);
        if (member is null || metadata is null)
        {
            return Problems.From(AdminErrors.Forbidden);
        }

        AdminAccess.SetMember(http, member);
        if (metadata.Gate == AdminGate.Member)
        {
            return await next(context);
        }

        var admin = await http.AuthenticateAsync(AdminPrincipal.Scheme);
        if (!admin.Succeeded || admin.Principal.PlayerId() != player)
        {
            return Problems.From(AdminErrors.TotpRequired);
        }

        http.Items[PrincipalKey] = admin.Principal;

        if (metadata.Gate == AdminGate.Permission && !metadata.Allows([.. member.Roles]))
        {
            return Problems.From(AdminErrors.Forbidden);
        }

        if (metadata.Sensitive && !HasFreshCode(http))
        {
            return Problems.From(AdminErrors.ReauthRequired);
        }

        return await next(context);
    }

    /// <summary>
    /// Whether the member confirmed a code less than five minutes ago. Also used by handlers
    /// whose sensitivity depends on the request (a permanent ban).
    /// </summary>
    public static bool HasFreshCode(HttpContext http)
    {
        var principal = http.Items[PrincipalKey] as System.Security.Claims.ClaimsPrincipal;
        var time = http.RequestServices.GetRequiredService<TimeProvider>();
        return principal?.ReauthAt() is { } reauthAt && time.GetUtcNow() - reauthAt <= AdminPrincipal.ReauthWindow;
    }
}
