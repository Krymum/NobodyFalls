using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using NobodyFalls.Infrastructure;

namespace NobodyFalls.Api.Auth;

internal sealed class OAuthProviderOptions
{
    public string ClientId { get; set; } = "";

    public string ClientSecret { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrEmpty(ClientId) && !string.IsNullOrEmpty(ClientSecret);
}

/// <summary>Which sign-in methods this deployment offers; sent to the front end.</summary>
internal sealed record SignInProviders(IReadOnlyList<string> Names);

internal static class AuthSetup
{
    /// <summary>
    /// Cookie session (HttpOnly, SameSite=Lax, Secure), Google and Discord when their keys
    /// are configured, and a dev login when dev tools are on (ADR 0007).
    /// </summary>
    public static IServiceCollection AddNobodyFallsAuth(this IServiceCollection services, IConfiguration configuration, bool devTools)
    {
        var secure = configuration.GetValue("Auth:SecureCookies", true);
        var google = configuration.GetSection("Auth:Google").Get<OAuthProviderOptions>() ?? new();
        var discord = configuration.GetSection("Auth:Discord").Get<OAuthProviderOptions>() ?? new();

        services.AddDataProtection().SetApplicationName("NobodyFalls").PersistKeysToNobodyFallsDatabase();

        var auth = services
            .AddAuthentication(PlayerPrincipal.Scheme)
            .AddCookie(PlayerPrincipal.Scheme, options =>
            {
                Configure(options.Cookie, secure, "nf");
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
                ApiFriendly(options);
            })
            .AddCookie(PlayerPrincipal.ExternalScheme, options =>
            {
                Configure(options.Cookie, secure, "nf-ext");
                options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                options.SlidingExpiration = false;
                ApiFriendly(options);
            });

        var providers = new List<string>();
        if (google.IsConfigured)
        {
            auth.AddGoogle("google", options =>
            {
                options.SignInScheme = PlayerPrincipal.ExternalScheme;
                options.ClientId = google.ClientId;
                options.ClientSecret = google.ClientSecret;
                options.CallbackPath = "/auth/callback/google";
                options.ClaimsIssuer = "google";
            });
            providers.Add("google");
        }

        if (discord.IsConfigured)
        {
            auth.AddDiscord("discord", options =>
            {
                options.SignInScheme = PlayerPrincipal.ExternalScheme;
                options.ClientId = discord.ClientId;
                options.ClientSecret = discord.ClientSecret;
                options.CallbackPath = "/auth/callback/discord";
                options.ClaimsIssuer = "discord";
                options.Scope.Clear();
                options.Scope.Add("identify");
            });
            providers.Add("discord");
        }

        if (devTools)
        {
            providers.Add("dev");
        }

        services.AddSingleton(new SignInProviders(providers));
        services.AddAuthorizationBuilder()
            .AddPolicy(PlayerPrincipal.Policy, policy => policy
                .AddAuthenticationSchemes(PlayerPrincipal.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(PlayerPrincipal.PlayerClaim));
        return services;
    }

    /// <summary>
    /// <c>__Host-</c> cookies cannot be read by subdomains nor set over HTTP. Local HTTP
    /// development (docker compose) turns <c>Auth:SecureCookies</c> off.
    /// </summary>
    private static void Configure(CookieBuilder cookie, bool secure, string name)
    {
        cookie.Name = secure ? $"__Host-{name}" : name;
        cookie.HttpOnly = true;
        cookie.SameSite = SameSiteMode.Lax;
        cookie.SecurePolicy = secure ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        cookie.Path = "/";
        cookie.IsEssential = true;
    }

    /// <summary>An API answers 401/403; it never redirects to a login page.</summary>
    private static void ApiFriendly(CookieAuthenticationOptions options)
    {
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }

    public static Task SignInPlayerAsync(this HttpContext context, NobodyFalls.Domain.Common.PlayerId player) =>
        context.SignInAsync(PlayerPrincipal.Scheme, PlayerPrincipal.ForPlayer(player));
}
