using Microsoft.AspNetCore.Authentication;
using NobodyFalls.Api.Hosting;
using NobodyFalls.Application.Accounts;

namespace NobodyFalls.Api.Auth;

/// <summary>
/// Sign-in flow (ADR 0007): the provider (or the dev login) sets the external cookie,
/// then <c>/auth/complete</c> either opens the session of an existing player or sends the
/// person to the onboarding to create their character.
/// </summary>
internal static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app, DevToolsOptions devTools)
    {
        var auth = app.MapGroup("/auth").WithTags("Auth");

        auth.MapGet("/login/{provider}", (string provider, string? returnUrl, SignInProviders providers) =>
            !providers.Names.Contains(provider) || provider == "dev"
                ? Results.NotFound()
                : Results.Challenge(
                    new AuthenticationProperties { RedirectUri = $"/auth/complete?returnUrl={Uri.EscapeDataString(LocalUrl(returnUrl))}" },
                    [provider]));

        if (devTools.Enabled)
        {
            // Any name signs in: development only, never registered otherwise.
            auth.MapGet("/login/dev", async (string name, string? returnUrl, HttpContext context) =>
            {
                if (string.IsNullOrWhiteSpace(name) || name.Length > 64)
                {
                    return Results.BadRequest();
                }

                await context.SignInAsync(PlayerPrincipal.ExternalScheme, PlayerPrincipal.ForDevIdentity(name.Trim()));
                return Results.Redirect($"/auth/complete?returnUrl={Uri.EscapeDataString(LocalUrl(returnUrl))}");
            });
        }

        auth.MapGet("/complete", async (string? returnUrl, HttpContext context, IAccountStore accounts, CancellationToken ct) =>
        {
            var target = LocalUrl(returnUrl);
            var external = await context.AuthenticateAsync(PlayerPrincipal.ExternalScheme);
            var identity = external.Succeeded ? external.Principal.External() : null;
            if (identity is null)
            {
                return Results.Redirect(target);
            }

            if (await accounts.FindByLoginAsync(identity, ct) is { } player)
            {
                await context.SignInPlayerAsync(player);
                await context.SignOutAsync(PlayerPrincipal.ExternalScheme);
                return Results.Redirect(target);
            }

            // No character yet: the external cookie stays until registration completes.
            return Results.Redirect($"/{LanguageOf(target)}/onboarding");
        });

        auth.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(PlayerPrincipal.Scheme);
            await context.SignOutAsync(PlayerPrincipal.ExternalScheme);
            return Results.NoContent();
        });
    }

    /// <summary>Only local paths are accepted as return URLs (no open redirect).</summary>
    public static string LocalUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal)
            ? url
            : "/";

    private static string LanguageOf(string path) =>
        path.StartsWith("/en", StringComparison.Ordinal) ? "en" : "fr";
}
