using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using NobodyFalls.Api.Auth;
using NobodyFalls.Domain.Common;

namespace NobodyFalls.Api.Admin;

/// <summary>
/// The admin session (ADR 0019): a second cookie, issued only after a TOTP code, on top of
/// the player session. It records when the member signed in and when they last confirmed
/// a code; sensitive actions require a confirmation less than five minutes old.
/// </summary>
internal static class AdminPrincipal
{
    public const string Scheme = "nf.admin";
    public const string AuthTimeClaim = "nf:auth_time";
    public const string ReauthClaim = "nf:reauth_at";

    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(8);
    public static readonly TimeSpan ReauthWindow = TimeSpan.FromMinutes(5);

    public static ClaimsPrincipal Create(PlayerId player, int sessionVersion, DateTimeOffset authTime, DateTimeOffset reauthAt) =>
        new(new ClaimsIdentity(
            [
                new Claim(PlayerPrincipal.PlayerClaim, player.ToString()),
                new Claim(PlayerPrincipal.SessionVersionClaim, sessionVersion.ToString(CultureInfo.InvariantCulture)),
                new Claim(AuthTimeClaim, authTime.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
                new Claim(ReauthClaim, reauthAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            ],
            Scheme));

    public static DateTimeOffset? AuthTime(this ClaimsPrincipal user) => Read(user, AuthTimeClaim);

    public static DateTimeOffset? ReauthAt(this ClaimsPrincipal user) => Read(user, ReauthClaim);

    /// <summary>Refuses an admin cookie past its absolute lifetime or from an ended session.</summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var tooOld = context.Principal?.AuthTime() is not { } authTime || now - authTime > MaxLifetime;
        if (tooOld || !await SessionVersions.IsCurrentAsync(context))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(Scheme);
        }
    }

    /// <summary>Opens (or refreshes after re-authentication) the admin session.</summary>
    public static Task SignInAsync(HttpContext context, PlayerId player, int sessionVersion, DateTimeOffset authTime, DateTimeOffset now) =>
        context.SignInAsync(Scheme, Create(player, sessionVersion, authTime, now));

    private static DateTimeOffset? Read(ClaimsPrincipal user, string claim) =>
        long.TryParse(user.FindFirst(claim)?.Value, CultureInfo.InvariantCulture, out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
}
