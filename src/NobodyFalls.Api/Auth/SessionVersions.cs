using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using NobodyFalls.Application.Staff;

namespace NobodyFalls.Api.Auth;

/// <summary>
/// Ends sessions remotely (ADR 0020): a cookie whose session version is older than the
/// player's current one is refused and removed. A ban or an authenticator reset bumps
/// the version, which signs the player out on every device.
/// </summary>
internal static class SessionVersions
{
    public static async Task<bool> IsCurrentAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (principal?.PlayerId() is not { } player)
        {
            return false;
        }

        var current = await context.HttpContext.RequestServices.GetRequiredService<ISessionVersions>()
            .GetAsync(player, context.HttpContext.RequestAborted);
        return principal.SessionVersion() == current;
    }

    public static async Task RejectStaleAsync(CookieValidatePrincipalContext context)
    {
        if (!await IsCurrentAsync(context))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(context.Scheme.Name);
        }
    }
}
