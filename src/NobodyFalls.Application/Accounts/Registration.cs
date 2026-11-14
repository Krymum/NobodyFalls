using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Queries;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Skins;

namespace NobodyFalls.Application.Accounts;

/// <summary>Who the identity provider says the person is (Google, Discord, or the dev login).</summary>
public sealed record ExternalIdentity(string Provider, string Key);

public sealed record NewPlayer(
    ExternalIdentity Identity,
    Pseudo Pseudo,
    Appearance Appearance,
    string Locale,
    string? InviteCode);

public interface IAccountStore
{
    Task<PlayerId?> FindByLoginAsync(ExternalIdentity identity, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the player in one transaction: account, character, login, invitation tree,
    /// automatic friendship with the inviter, and a place in today's game if a season is open.
    /// Fails with <see cref="PseudoErrors.Taken"/> or <see cref="AccountErrors.AlreadyRegistered"/>.
    /// </summary>
    Task<Result<PlayerId>> CreateAsync(NewPlayer player, DateTimeOffset now, CancellationToken cancellationToken);

    Task TouchAsync(PlayerId player, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Cloudflare Turnstile, verified server side (GAME_DESIGN §12).</summary>
public interface IHumanVerifier
{
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken);
}

public static class AccountErrors
{
    public static readonly DomainError AlreadyRegistered = new("account.already_registered", "This account already has a character.");
    public static readonly DomainError Locale = new("account.locale", "Unsupported language.");
    public static readonly DomainError NotHuman = new("account.verification_failed", "The anti-bot check failed. Please try again.");
}

public static class Locales
{
    public static readonly IReadOnlyList<string> Supported = ["fr", "en"];

    public static bool IsSupported(string? locale) => locale is not null && Supported.Contains(locale);
}

/// <summary>Creates a character for someone who signed in with an identity provider.</summary>
public sealed class RegisterPlayer(IAccountStore accounts, ISkinCatalogProvider catalogs, IHumanVerifier verifier, IClock clock)
{
    public async Task<Result<PlayerId>> ExecuteAsync(
        ExternalIdentity identity,
        string? pseudo,
        Appearance appearance,
        string? locale,
        string? inviteCode,
        string? turnstileToken,
        string? remoteIp,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        if (!await verifier.VerifyAsync(turnstileToken, remoteIp, cancellationToken))
        {
            return AccountErrors.NotHuman;
        }

        var name = Pseudo.Create(pseudo);
        if (name.IsFailure)
        {
            return name.Error!;
        }

        if (name.Value.IsReservedForWatchers)
        {
            return PseudoErrors.Reserved;
        }

        if (!Locales.IsSupported(locale))
        {
            return AccountErrors.Locale;
        }

        var catalog = await catalogs.GetAsync(cancellationToken);
        var look = AppearanceValidator.Validate(appearance, catalog, new HashSet<string>());
        if (look.IsFailure)
        {
            return look.Error!;
        }

        return await accounts.CreateAsync(
            new NewPlayer(identity, name.Value, appearance, locale!, string.IsNullOrWhiteSpace(inviteCode) ? null : inviteCode.Trim()),
            clock.UtcNow,
            cancellationToken);
    }
}
