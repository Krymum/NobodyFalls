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
    public const string Policy = "player";

    public static ClaimsPrincipal ForPlayer(PlayerId player) =>
        new(new ClaimsIdentity([new Claim(PlayerClaim, player.ToString())], Scheme));

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
