using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NobodyFalls.Api.Admin;
using NobodyFalls.Api.Tests.Support;
using NobodyFalls.Domain.Staff;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Api.Tests.Admin;

/// <summary>
/// Every admin endpoint, tested without a session, without a role, with a role but no
/// two-factor session, with each role, and for sensitive actions without a recent code.
/// The table below must match the endpoints the app actually maps: adding an endpoint
/// without adding it here fails <see cref="The_table_lists_every_admin_endpoint_with_its_declared_access"/>.
/// </summary>
public sealed class AdminAuthorizationTests(PostgresFixture postgres)
{
    private static readonly Permission[] Tickets = [Permission.TicketsSupport, Permission.TicketsAppeal];

    /// <summary>Method, route, gate, permission, sensitive, any-of permissions.</summary>
    private static readonly (string Method, string Route, AdminGate Gate, Permission? Permission, bool Sensitive, Permission[]? AnyOf)[] Endpoints =
    [
        ("GET", "/api/v1/admin/session", AdminGate.Member, null, false, null),
        ("POST", "/api/v1/admin/session/totp/enroll", AdminGate.Member, null, false, null),
        ("POST", "/api/v1/admin/session/totp/confirm", AdminGate.Member, null, false, null),
        ("POST", "/api/v1/admin/session/login", AdminGate.Member, null, false, null),
        ("POST", "/api/v1/admin/session/reauth", AdminGate.SignedIn, null, false, null),
        ("POST", "/api/v1/admin/session/logout", AdminGate.Member, null, false, null),
        ("GET", "/api/v1/admin/staff", AdminGate.Permission, Permission.StaffManage, false, null),
        ("POST", "/api/v1/admin/staff", AdminGate.Permission, Permission.StaffManage, true, null),
        ("PUT", "/api/v1/admin/staff/{playerId:guid}", AdminGate.Permission, Permission.StaffManage, true, null),
        ("DELETE", "/api/v1/admin/staff/{playerId:guid}", AdminGate.Permission, Permission.StaffManage, true, null),
        ("POST", "/api/v1/admin/staff/{playerId:guid}/totp/reset", AdminGate.Permission, Permission.StaffManage, true, null),
        ("GET", "/api/v1/admin/audit", AdminGate.Permission, Permission.AuditView, false, null),
        ("GET", "/api/v1/admin/audit/verify", AdminGate.Permission, Permission.AuditView, false, null),
        ("GET", "/api/v1/admin/dashboard", AdminGate.Permission, Permission.DashboardView, false, null),
        ("GET", "/api/v1/admin/players/{id:guid}/links", AdminGate.Permission, Permission.PlayersViewLinks, false, null),
        ("GET", "/api/v1/admin/players/{id:guid}/export", AdminGate.Permission, Permission.GdprExport, false, null),
        ("DELETE", "/api/v1/admin/players/{id:guid}", AdminGate.Permission, Permission.GdprDelete, true, null),
        ("GET", "/api/v1/admin/players", AdminGate.Permission, Permission.PlayersView, false, null),
        ("GET", "/api/v1/admin/players/{id:guid}", AdminGate.Permission, Permission.PlayersView, false, null),
        ("GET", "/api/v1/admin/players/{id:guid}/hands", AdminGate.Permission, Permission.PlayersView, false, null),
        ("POST", "/api/v1/admin/players/{id:guid}/sanctions", AdminGate.Permission, Permission.SanctionsWarn, false, null),
        ("POST", "/api/v1/admin/sanctions/{id:long}/revoke", AdminGate.Permission, Permission.SanctionsRevoke, false, null),
        ("GET", "/api/v1/admin/reports", AdminGate.Permission, Permission.ReportsView, false, null),
        ("GET", "/api/v1/admin/reports/{id:long}", AdminGate.Permission, Permission.ReportsView, false, null),
        ("POST", "/api/v1/admin/reports/{id:long}/assign", AdminGate.Permission, Permission.ReportsHandle, false, null),
        ("POST", "/api/v1/admin/reports/{id:long}/resolve", AdminGate.Permission, Permission.ReportsHandle, false, null),
        ("GET", "/api/v1/admin/tickets", AdminGate.Permission, null, false, Tickets),
        ("GET", "/api/v1/admin/tickets/{id:long}", AdminGate.Permission, null, false, Tickets),
        ("POST", "/api/v1/admin/tickets/{id:long}/messages", AdminGate.Permission, null, false, Tickets),
        ("POST", "/api/v1/admin/tickets/{id:long}/status", AdminGate.Permission, null, false, Tickets),
        ("POST", "/api/v1/admin/tickets/{id:long}/assign", AdminGate.Permission, null, false, Tickets),
        ("GET", "/api/v1/admin/ideas", AdminGate.Permission, Permission.IdeasModerate, false, null),
        ("PATCH", "/api/v1/admin/ideas/{id:long}", AdminGate.Permission, Permission.IdeasModerate, false, null),
        ("POST", "/api/v1/admin/ideas/{id:long}/merge", AdminGate.Permission, Permission.IdeasModerate, false, null),
    ];

    public static TheoryData<string, string> Routes()
    {
        var data = new TheoryData<string, string>();
        foreach (var e in Endpoints)
        {
            data.Add(e.Method, e.Route);
        }

        return data;
    }

    [Fact]
    public async Task The_table_lists_every_admin_endpoint_with_its_declared_access()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var mapped = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith(AdminEndpoints.Prefix, StringComparison.Ordinal) == true)
            .SelectMany(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Select(method =>
            {
                var access = e.Metadata.GetMetadata<AdminAccessMetadata>();
                return $"{method} {e.RoutePattern.RawText!.TrimEnd('/')} {access?.Describe()}";
            }))
            .Order()
            .ToList();

        var expected = Endpoints.Select(e => $"{e.Method} {e.Route} {new AdminAccessMetadata(e.Gate, e.Permission, e.Sensitive, e.AnyOf).Describe()}").Order().ToList();

        Assert.Equal(expected, mapped);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Access_follows_the_permission_matrix(string method, string route)
    {
        var endpoint = Endpoints.Single(e => e.Method == method && e.Route == route);
        await using var app = await ApiApp.StartAsync(postgres);
        var player = (await app.SignUpAsync("justplayer")).Client;
        var unenrolled = await app.StaffAsync("noauthapp", [StaffRole.Admin], enroll: false);
        var members = new Dictionary<StaffRole, StaffMember>
        {
            [StaffRole.Admin] = await app.StaffAsync("theadmin", [StaffRole.Admin]),
            [StaffRole.Moderator] = await app.StaffAsync("themod", [StaffRole.Moderator]),
            [StaffRole.Support] = await app.StaffAsync("thesupport", [StaffRole.Support]),
        };

        // No session at all.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(app.Browser(), endpoint)).StatusCode);

        // A player without any role.
        await AssertRefusedAsync(await SendAsync(player, endpoint), HttpStatusCode.Forbidden, "admin.forbidden");

        // A role, but no two-factor session yet.
        var beforeTotp = await SendAsync(unenrolled.Client, endpoint);
        if (endpoint.Gate == AdminGate.Member)
        {
            AssertAllowed(beforeTotp);
        }
        else
        {
            await AssertRefusedAsync(beforeTotp, HttpStatusCode.Forbidden, "admin.totp_required");
        }

        // Each role, with a fresh two-factor session.
        foreach (var (role, member) in members)
        {
            var response = await SendAsync(member.Client, endpoint);
            var allowed = endpoint.Permission is { } permission
                ? PermissionMatrix.Allows([role], permission)
                : endpoint.AnyOf is not { } any || any.Any(p => PermissionMatrix.Allows([role], p));
            if (!allowed)
            {
                await AssertRefusedAsync(response, HttpStatusCode.Forbidden, "admin.forbidden");
            }
            else
            {
                AssertAllowed(response);
            }
        }

        // Sensitive actions want a code confirmed less than five minutes ago.
        app.Time.Advance(TimeSpan.FromMinutes(6));
        var stale = await SendAsync(members[StaffRole.Admin].Client, endpoint);
        if (endpoint.Sensitive)
        {
            await AssertRefusedAsync(stale, HttpStatusCode.Unauthorized, "admin.reauth_required");
        }
        else
        {
            AssertAllowed(stale);
        }
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, (string Method, string Route, AdminGate, Permission?, bool, Permission[]?) endpoint)
    {
        var url = endpoint.Route
            .Replace("{playerId:guid}", Guid.Empty.ToString(), StringComparison.Ordinal)
            .Replace("{id:guid}", Guid.Empty.ToString(), StringComparison.Ordinal)
            .Replace("{id:long}", "1", StringComparison.Ordinal);
        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), new Uri(url, UriKind.Relative));
        if (endpoint.Method is "POST" or "PUT" or "PATCH" or "DELETE")
        {
            request.Content = JsonContent.Create(new { });
        }

        return client.SendAsync(request);
    }

    private static void AssertAllowed(HttpResponseMessage response) =>
        Assert.True(
            response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden),
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} was refused: {(int)response.StatusCode}");

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }
}
