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
    /// <summary>Method, route, gate, permission, sensitive.</summary>
    private static readonly (string Method, string Route, AdminGate Gate, Permission? Permission, bool Sensitive)[] Endpoints =
    [
        ("GET", "/api/v1/admin/session", AdminGate.Member, null, false),
        ("POST", "/api/v1/admin/session/totp/enroll", AdminGate.Member, null, false),
        ("POST", "/api/v1/admin/session/totp/confirm", AdminGate.Member, null, false),
        ("POST", "/api/v1/admin/session/login", AdminGate.Member, null, false),
        ("POST", "/api/v1/admin/session/reauth", AdminGate.SignedIn, null, false),
        ("POST", "/api/v1/admin/session/logout", AdminGate.Member, null, false),
        ("GET", "/api/v1/admin/staff", AdminGate.Permission, Permission.StaffManage, false),
        ("POST", "/api/v1/admin/staff", AdminGate.Permission, Permission.StaffManage, true),
        ("PUT", "/api/v1/admin/staff/{playerId:guid}", AdminGate.Permission, Permission.StaffManage, true),
        ("DELETE", "/api/v1/admin/staff/{playerId:guid}", AdminGate.Permission, Permission.StaffManage, true),
        ("POST", "/api/v1/admin/staff/{playerId:guid}/totp/reset", AdminGate.Permission, Permission.StaffManage, true),
        ("GET", "/api/v1/admin/audit", AdminGate.Permission, Permission.AuditView, false),
        ("GET", "/api/v1/admin/audit/verify", AdminGate.Permission, Permission.AuditView, false),
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
                return $"{method} {e.RoutePattern.RawText!.TrimEnd('/')} {access?.Gate} {access?.Permission} {access?.Sensitive}";
            }))
            .Order()
            .ToList();

        var expected = Endpoints.Select(e => $"{e.Method} {e.Route} {e.Gate} {e.Permission} {e.Sensitive}").Order().ToList();

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
            if (endpoint.Permission is { } permission && !PermissionMatrix.Allows([role], permission))
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

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, (string Method, string Route, AdminGate, Permission?, bool) endpoint)
    {
        var url = endpoint.Route.Replace("{playerId:guid}", Guid.Empty.ToString(), StringComparison.Ordinal);
        var request = new HttpRequestMessage(new HttpMethod(endpoint.Method), new Uri(url, UriKind.Relative));
        if (endpoint.Method is "POST" or "PUT")
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
