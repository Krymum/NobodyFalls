using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NobodyFalls.Api.Tests.Support;
using NobodyFalls.Testing.Postgres;

namespace NobodyFalls.Api.Tests;

/// <summary>Authorization of every player endpoint, the CSRF header, and dev tools exposure.</summary>
public sealed class SecurityTests(PostgresFixture postgres)
{
    public static TheoryData<string, string> PlayerEndpoints => new()
    {
        { "GET", "/api/v1/tonight" },
        { "GET", "/api/v1/edge" },
        { "POST", "/api/v1/hands" },
        { "GET", "/api/v1/players/00000000-0000-0000-0000-000000000001" },
        { "GET", "/api/v1/me/profile" },
        { "PATCH", "/api/v1/me/settings" },
        { "PUT", "/api/v1/me/appearance" },
        { "GET", "/api/v1/friends" },
        { "DELETE", "/api/v1/friends/00000000-0000-0000-0000-000000000001" },
        { "GET", "/api/v1/friends/requests" },
        { "POST", "/api/v1/friends/requests" },
        { "POST", "/api/v1/friends/requests/1/accept" },
        { "POST", "/api/v1/friends/requests/1/decline" },
        { "DELETE", "/api/v1/friends/requests/1" },
        { "GET", "/api/v1/friends/suggestions" },
        { "GET", "/api/v1/blocks" },
        { "PUT", "/api/v1/blocks/00000000-0000-0000-0000-000000000001" },
        { "DELETE", "/api/v1/blocks/00000000-0000-0000-0000-000000000001" },
    };

    [Theory]
    [MemberData(nameof(PlayerEndpoints))]
    public async Task Player_endpoints_refuse_anonymous_requests(string method, string url)
    {
        await using var app = await ApiApp.StartAsync(postgres);
        using var request = new HttpRequestMessage(new HttpMethod(method), url) { Content = JsonContent.Create(new { }) };

        var response = await app.Browser().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_pending_registration_is_not_a_player()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var client = app.Browser();
        await ApiApp.DevLoginAsync(client, "not-yet");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(new Uri("/api/v1/tonight", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task State_changing_calls_need_the_api_header()
    {
        // A forged cross-site request carries the session cookie but not the header.
        await using var app = await ApiApp.StartAsync(postgres);
        await app.SignUpAsync("moi");
        var forged = app.Browser(withApiHeader: false);
        await ApiApp.DevLoginAsync(forged, "moi");

        var response = await forged.PostAsJsonAsync("/api/v1/hands", new { receiverId = Guid.NewGuid(), turnstileToken = "ok" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("request.missing_header", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await forged.GetAsync(new Uri("/api/v1/tonight", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Session_cookies_are_http_only_lax_and_host_bound()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var client = app.Browser();

        var login = await client.GetAsync(new Uri("/auth/login/dev?name=cookie", UriKind.Relative));
        var setCookie = string.Join("\n", login.Headers.GetValues("Set-Cookie"));

        Assert.Contains("__Host-nf-ext=", setCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Without_dev_tools_there_is_no_dev_login_nor_dev_endpoint()
    {
        await using var app = await ApiApp.StartAsync(postgres, s => s["DevTools:Enabled"] = "false");
        var client = app.Browser();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(new Uri("/auth/login/dev?name=x", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(new Uri("/dev/seed", UriKind.Relative), JsonContent.Create(new { }))).StatusCode);
        var session = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/session", UriKind.Relative));
        Assert.False(session.GetProperty("devTools").GetBoolean());
        Assert.Empty(session.GetProperty("providers").EnumerateArray());
    }

    [Fact]
    public async Task Settings_and_appearance_are_validated()
    {
        await using var app = await ApiApp.StartAsync(postgres);
        var (me, _) = await app.SignUpAsync("moi");

        var locale = await me.PatchAsJsonAsync("/api/v1/me/settings", new { locale = "de" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, locale.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await me.PatchAsJsonAsync("/api/v1/me/settings", new { locale = "en", mapNameVisibility = "everyone" })).StatusCode);

        var profile = await me.GetFromJsonAsync<JsonElement>(new Uri("/api/v1/me/profile", UriKind.Relative));
        Assert.Equal("en", profile.GetProperty("locale").GetString());
        Assert.Equal("everyone", profile.GetProperty("mapNameVisibility").GetString());
    }
}
