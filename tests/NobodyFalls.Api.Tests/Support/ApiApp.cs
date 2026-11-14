using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Accounts;
using NobodyFalls.Application.Night;
using NobodyFalls.Domain.Common;
using NobodyFalls.Testing.Postgres;
using Npgsql;

namespace NobodyFalls.Api.Tests.Support;

public sealed class ManualClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

/// <summary>Turnstile stand-in: the token "ok" passes, anything else fails.</summary>
public sealed class FakeHumanVerifier : IHumanVerifier
{
    public const string Valid = "ok";

    public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken) =>
        Task.FromResult(token == Valid);
}

/// <summary>The real API on a fresh database, with a clock the test controls.</summary>
public sealed class ApiApp : WebApplicationFactory<Program>
{
    public static readonly GameDay Day = GameDay.Parse("2026-11-13");

    private readonly string _connectionString;
    private readonly Dictionary<string, string?> _settings;

    private ApiApp(string connectionString, Dictionary<string, string?> settings)
    {
        _connectionString = connectionString;
        _settings = settings;
    }

    public ManualClock Clock { get; } = new(Day.StartsAt.AddHours(12));

    public Seeder Seed => new(Services.GetRequiredService<NpgsqlDataSource>());

    /// <summary>A running season and a started day, as on any ordinary afternoon.</summary>
    public static async Task<ApiApp> StartAsync(PostgresFixture postgres, Action<Dictionary<string, string?>>? configure = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["DevTools:Enabled"] = "true",
            ["Night:RunWorker"] = "false",
            ["Database:MigrateOnStartup"] = "false",
            ["Auth:SecureCookies"] = "true",
            ["Turnstile:SiteKey"] = "site-key",
            ["Turnstile:SecretKey"] = "secret",
            ["RateLimits:HandsPerPlayerPerMinute"] = "100",
            ["RateLimits:FriendsPerPlayerPerMinute"] = "100",
            ["RateLimits:RegistrationsPerIpPerHour"] = "100",
            ["RateLimits:PerIpPerMinute"] = "10000",
            ["Realtime:FlushInterval"] = "00:00:00.05",
        };
        configure?.Invoke(settings);

        var app = new ApiApp(await postgres.CreateDatabaseAsync(), settings);
        await app.Seed.SeasonAsync(Day.AddDays(-10));
        await app.Services.GetRequiredService<INightStore>().EnsureDayStartedAsync(Day, app.Clock.UtcNow, CancellationToken.None);
        return app;
    }

    /// <summary>A browser-like client: keeps cookies, does not follow redirects, sends the API header.</summary>
    public HttpClient Browser(bool withApiHeader = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        if (withApiHeader)
        {
            client.DefaultRequestHeaders.Add("X-NF-Request", "1");
        }

        return client;
    }

    /// <summary>Signs up through the dev login and the registration endpoint, like the onboarding does.</summary>
    public async Task<(HttpClient Client, Guid Id)> SignUpAsync(string pseudo, string? inviteCode = null)
    {
        var client = Browser();
        await DevLoginAsync(client, pseudo);
        var response = await client.PostAsJsonAsync("/api/v1/registration", Registration(pseudo, inviteCode));
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<IdBody>();
        return (client, created!.Id);
    }

    /// <summary>
    /// Signs up with a cookie jar the test owns, so that a SignalR connection can share
    /// the session (long polling, the fallback transport, over the test server).
    /// </summary>
    public async Task<(HttpClient Client, Guid Id, HubConnection Hub)> ConnectAsync(string pseudo)
    {
        var cookies = new CookieContainer();
        var client = new HttpClient(new CookieContainerHandler(cookies) { InnerHandler = Server.CreateHandler() })
        {
            BaseAddress = new Uri("https://localhost"),
        };
        client.DefaultRequestHeaders.Add("X-NF-Request", "1");
        await DevLoginAsync(client, pseudo);
        var response = await client.PostAsJsonAsync("/api/v1/registration", Registration(pseudo));
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<IdBody>())!.Id;

        var hub = new HubConnectionBuilder()
            .WithUrl(new Uri("https://localhost/hubs/game"), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => new CookieContainerHandler(cookies) { InnerHandler = Server.CreateHandler() };
            })
            .Build();
        return (client, id, hub);
    }

    public static async Task DevLoginAsync(HttpClient client, string name)
    {
        var login = await client.GetAsync(new Uri($"/auth/login/dev?name={Uri.EscapeDataString(name)}&returnUrl=/fr/", UriKind.Relative));
        Assert.Equal(System.Net.HttpStatusCode.Redirect, login.StatusCode);
        await client.GetAsync(login.Headers.Location);
    }

    public static object Registration(string pseudo, string? inviteCode = null, string token = FakeHumanVerifier.Valid, string coat = "coat-long") => new
    {
        pseudo,
        locale = "fr",
        inviteCode,
        turnstileToken = token,
        appearance = new
        {
            skinTone = "#C9C3B9",
            slots = new Dictionary<string, object>
            {
                ["coat"] = new { itemId = coat, color = coat == "coat-storm" ? "#3A4A4F" : "#4A5463" },
                ["trousers"] = new { itemId = "trousers-plain", color = "#262A31" },
                ["boots"] = new { itemId = "boots-plain", color = "#16191E" },
            },
        },
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", _connectionString);
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IClock>(Clock);
            services.AddSingleton<IHumanVerifier, FakeHumanVerifier>();
        });
    }

    public sealed record IdBody(Guid Id);
}
