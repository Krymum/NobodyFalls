using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NobodyFalls.Api.Tests.Support;
using NobodyFalls.Domain.Staff;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Api.Tests.Admin;

/// <summary>Two-factor admin sessions, re-authentication, team management and the audit trail (ADR 0017–0020).</summary>
public sealed class AdminSessionTests(PostgresFixture postgres)
{
    private const string Admin = StaffSupport.Admin;

    [Fact]
    public async Task A_new_member_enrols_an_authenticator_and_gets_ten_recovery_codes()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var member = await app.StaffAsync("newmod", [StaffRole.Moderator], enroll: false);

        var before = await SessionAsync(member.Client);
        Assert.False(before.GetProperty("totpEnrolled").GetBoolean());
        Assert.False(before.GetProperty("signedIn").GetBoolean());

        var codes = await member.EnrollAsync();

        Assert.Equal(10, codes.Count);
        var after = await SessionAsync(member.Client);
        Assert.True(after.GetProperty("totpEnrolled").GetBoolean());
        Assert.True(after.GetProperty("signedIn").GetBoolean());
        Assert.Equal(["moderator"], after.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.Contains("reports.handle", after.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(HttpStatusCode.Conflict, (await member.Client.PostAsync(new Uri($"{Admin}/session/totp/enroll", UriKind.Relative), null)).StatusCode);
    }

    [Fact]
    public async Task Signing_in_needs_a_valid_code_or_a_recovery_code()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var member = await app.StaffAsync("signer", [StaffRole.Support], enroll: false);
        var recovery = await member.EnrollAsync();
        await member.Client.PostAsync(new Uri($"{Admin}/session/logout", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.Forbidden, (await member.Client.GetAsync(new Uri($"{Admin}/staff", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await member.LoginAsync("000000")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.LoginAsync()).StatusCode);
        Assert.True((await SessionAsync(member.Client)).GetProperty("signedIn").GetBoolean());

        await member.Client.PostAsync(new Uri($"{Admin}/session/logout", UriKind.Relative), null);
        var withRecovery = await member.Client.PostAsJsonAsync($"{Admin}/session/login", new { recoveryCode = recovery[0] });
        Assert.Equal(HttpStatusCode.OK, withRecovery.StatusCode);
        Assert.Equal("recovery_code", (await Json(withRecovery)).GetProperty("method").GetString());
    }

    [Fact]
    public async Task Sensitive_actions_ask_for_a_fresh_code_after_five_minutes()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var boss = await app.StaffAsync("bossy", [StaffRole.Admin]);
        await app.SignUpAsync("helper");

        app.Time.Advance(TimeSpan.FromMinutes(6));
        var refused = await boss.Client.PostAsJsonAsync($"{Admin}/staff", new { pseudo = "helper", roles = new[] { "support" } });
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal("admin.reauth_required", (await Json(refused)).GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.OK, (await boss.ReauthAsync()).StatusCode);
        var added = await boss.Client.PostAsJsonAsync($"{Admin}/staff", new { pseudo = "helper", roles = new[] { "support" } });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
    }

    [Fact]
    public async Task The_admin_session_ends_after_thirty_idle_minutes()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var boss = await app.StaffAsync("idleboss", [StaffRole.Admin]);

        app.Time.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(HttpStatusCode.OK, (await boss.Client.GetAsync(new Uri($"{Admin}/staff", UriKind.Relative))).StatusCode);
        app.Time.Advance(TimeSpan.FromMinutes(31));

        var expired = await boss.Client.GetAsync(new Uri($"{Admin}/staff", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, expired.StatusCode);
        Assert.Equal("admin.totp_required", (await Json(expired)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_admin_session_lasts_eight_hours_at_most_even_when_active()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var boss = await app.StaffAsync("busyboss", [StaffRole.Admin]);

        for (var minutes = 0; minutes < 8 * 60 - 20; minutes += 20)
        {
            app.Time.Advance(TimeSpan.FromMinutes(20));
            Assert.Equal(HttpStatusCode.OK, (await boss.Client.GetAsync(new Uri($"{Admin}/staff", UriKind.Relative))).StatusCode);
        }

        app.Time.Advance(TimeSpan.FromMinutes(25));
        Assert.Equal(HttpStatusCode.Forbidden, (await boss.Client.GetAsync(new Uri($"{Admin}/staff", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Taking_a_role_away_applies_at_once()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var boss = await app.StaffAsync("chief", [StaffRole.Admin]);
        var other = await app.StaffAsync("deputy", [StaffRole.Admin]);
        Assert.Equal(HttpStatusCode.OK, (await other.Client.GetAsync(new Uri($"{Admin}/staff", UriKind.Relative))).StatusCode);

        var changed = await boss.Client.PutAsJsonAsync($"{Admin}/staff/{other.Id}", new { roles = new[] { "support" } });

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var refused = await other.Client.GetAsync(new Uri($"{Admin}/staff", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("admin.forbidden", (await Json(refused)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Resetting_an_authenticator_signs_the_member_out_everywhere()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var boss = await app.StaffAsync("resetter", [StaffRole.Admin]);
        var other = await app.StaffAsync("lostphone", [StaffRole.Moderator]);

        var reset = await boss.Client.PostAsync(new Uri($"{Admin}/staff/{other.Id}/totp/reset", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.Client.GetAsync(new Uri("/api/v1/me/profile", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.Client.GetAsync(new Uri($"{Admin}/session", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Team_rules_hold_through_the_api()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var boss = await app.StaffAsync("onlyboss", [StaffRole.Admin]);
        var support = await app.StaffAsync("helpdesk", [StaffRole.Support]);

        var self = await boss.Client.PutAsJsonAsync($"{Admin}/staff/{boss.Id}", new { roles = new[] { "support" } });
        var unknownRole = await boss.Client.PutAsJsonAsync($"{Admin}/staff/{support.Id}", new { roles = new[] { "owner" } });
        var nobody = await boss.Client.PostAsJsonAsync($"{Admin}/staff", new { pseudo = "ghost", roles = new[] { "support" } });

        Assert.Equal("staff.self", (await Json(self)).GetProperty("code").GetString());
        Assert.Equal("staff.unknown_role", (await Json(unknownRole)).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, nobody.StatusCode);
    }

    [Fact]
    public async Task Every_team_action_appears_in_the_audit_with_its_author_and_the_change()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var boss = await app.StaffAsync("auditor", [StaffRole.Admin]);
        var (_, helperId) = await app.SignUpAsync("newhelper");

        await boss.Client.PostAsJsonAsync($"{Admin}/staff", new { pseudo = "newhelper", roles = new[] { "support" } });
        await boss.Client.PutAsJsonAsync($"{Admin}/staff/{helperId}", new { roles = new[] { "moderator", "support" } });
        await boss.Client.DeleteAsync(new Uri($"{Admin}/staff/{helperId}", UriKind.Relative));

        var page = await Json(await boss.Client.GetAsync(new Uri($"{Admin}/audit?action=staff&targetId={helperId}", UriKind.Relative)));
        var entries = page.GetProperty("entries").EnumerateArray().Reverse().ToList();

        Assert.Equal(["staff.added", "staff.roles_changed", "staff.removed"], entries.Select(e => e.GetProperty("action").GetString()));
        Assert.All(entries, e => Assert.Equal("auditor", e.GetProperty("actorPseudo").GetString()));
        Assert.All(entries, e => Assert.Equal(["admin"], e.GetProperty("actorRoles").EnumerateArray().Select(r => r.GetString())));
        Assert.Equal("support", entries[1].GetProperty("before").GetProperty("roles")[0].GetString());
        Assert.Equal(2, entries[1].GetProperty("after").GetProperty("roles").GetArrayLength());

        var verify = await Json(await boss.Client.GetAsync(new Uri($"{Admin}/audit/verify", UriKind.Relative)));
        Assert.True(verify.GetProperty("valid").GetBoolean());

        // Sign-in, reading the log and verifying it are recorded too.
        var all = await Json(await boss.Client.GetAsync(new Uri($"{Admin}/audit?limit=200", UriKind.Relative)));
        var actions = all.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Contains("admin.totp_enrolled", actions);
        Assert.Contains("audit.viewed", actions);
        Assert.Contains("audit.verified", actions);
    }

    private static async Task<JsonElement> SessionAsync(HttpClient client) =>
        await Json(await client.GetAsync(new Uri($"{Admin}/session", UriKind.Relative)));

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
