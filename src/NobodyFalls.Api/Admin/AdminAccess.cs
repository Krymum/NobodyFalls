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
internal sealed record AdminAccessMetadata(AdminGate Gate, Permission? Permission = null, bool Sensitive = false);

internal static class AdminAccess
{
    public static RouteHandlerBuilder Requires(this RouteHandlerBuilder builder, Permission permission, bool sensitive = false) =>
        builder.WithMetadata(new AdminAccessMetadata(AdminGate.Permission, permission, sensitive));

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
/// Guards every admin endpoint (ADR 0017, 0018), in this order: a player session (401),
/// a team role read from the database now (403), an admin session opened with a TOTP code
/// (403 <c>admin.totp_required</c>), the endpoint's permission (403), and for sensitive
/// actions a code confirmed less than five minutes ago (401 <c>admin.reauth_required</c>).
/// An endpoint that declares nothing is refused: the gate fails closed.
/// </summary>
internal sealed class AdminAccessFilter(IStaffStore staff, TimeProvider time) : IEndpointFilter
{
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

        if (metadata.Gate == AdminGate.Permission
            && (metadata.Permission is not { } permission || !PermissionMatrix.Allows(member.Roles, permission)))
        {
            return Problems.From(AdminErrors.Forbidden);
        }

        if (metadata.Sensitive && (admin.Principal.ReauthAt() is not { } reauthAt || time.GetUtcNow() - reauthAt > AdminPrincipal.ReauthWindow))
        {
            return Problems.From(AdminErrors.ReauthRequired);
        }

        return await next(context);
    }
}
