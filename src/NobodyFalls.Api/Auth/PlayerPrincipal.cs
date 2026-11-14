using System.Security.Claims;
using NobodyFalls.Application.Accounts;
using NobodyFalls.Domain.Common;

namespace NobodyFalls.Api.Auth;

/// <summary>Claims carried by the two cookies (ADR 0007).</summary>
internal static class PlayerPrincipal
{
    /// <summary>Session cookie: the player is known.</summary>
    public const string Scheme = "nf";

    /// <summary>
    /// Short-lived cookie set by Google/Discord/dev login: we know who the person is, but
    /// they may not have a character yet (registration pending).
    /// </summary>
    public const string ExternalScheme = "nf.external";

    public const string PlayerClaim = "nf:player";

    /// <summary>The player's session version when the cookie was issued (ADR 0020).</summary>
    public const string SessionVersionClaim = "nf:sv";
    public const string Policy = "player";

    public static ClaimsPrincipal ForPlayer(PlayerId player, int sessionVersion) =>
        new(new ClaimsIdentity(
            [
                new Claim(PlayerClaim, player.ToString()),
                new Claim(SessionVersionClaim, sessionVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ],
            Scheme));

    /// <summary>Cookies issued before session versions existed carry none: they count as version 0.</summary>
    public static int SessionVersion(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst(SessionVersionClaim)?.Value, System.Globalization.CultureInfo.InvariantCulture, out var version) ? version : 0;

    public static PlayerId? PlayerId(this ClaimsPrincipal user) =>
        user.FindFirst(PlayerClaim)?.Value is { } value && Guid.TryParse(value, out var id) ? new PlayerId(id) : null;

    /// <summary>The player of an endpoint protected by <see cref="Policy"/>.</summary>
    public static PlayerId RequiredPlayerId(this ClaimsPrincipal user) =>
        user.PlayerId() ?? throw new InvalidOperationException("This endpoint requires the player policy.");

    /// <summary>
    /// The identity from the external cookie. The provider is the issuer of the name
    /// identifier claim ("Google", "Discord", or "dev").
    /// </summary>
    public static ExternalIdentity? External(this ClaimsPrincipal user)
    {
        var id = user.FindFirst(ClaimTypes.NameIdentifier);
        return id is null || string.IsNullOrEmpty(id.Value)
            ? null
            : new ExternalIdentity(id.Issuer.ToLowerInvariant(), id.Value);
    }

    public static ClaimsPrincipal ForDevIdentity(string name) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, name, ClaimValueTypes.String, "dev")], ExternalScheme));
}
