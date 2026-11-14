using NobodyFalls.Application.Accounts;
using NobodyFalls.Application.Queries;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Skins;

namespace NobodyFalls.Application.Players;

/// <summary>Who may see a player's name on the map (phase 3); others see an anonymous dot.</summary>
public static class MapNameVisibility
{
    public static readonly IReadOnlyList<string> Values = ["friends", "everyone", "nobody"];
}

public interface IPlayerSettingsStore
{
    Task UpdateAsync(PlayerId player, string? locale, string? mapNameVisibility, CancellationToken cancellationToken);

    Task<IReadOnlySet<string>> OwnedItemsAsync(PlayerId player, CancellationToken cancellationToken);

    Task SaveAppearanceAsync(PlayerId player, Appearance appearance, CancellationToken cancellationToken);
}

public static class SettingsErrors
{
    public static readonly DomainError MapNameVisibility = new("settings.map_name_visibility", "Unknown visibility.");
}

public sealed class UpdateSettings(IPlayerSettingsStore store)
{
    public async Task<Result> ExecuteAsync(PlayerId player, string? locale, string? mapNameVisibility, CancellationToken cancellationToken)
    {
        if (locale is not null && !Locales.IsSupported(locale))
        {
            return AccountErrors.Locale;
        }

        if (mapNameVisibility is not null && !MapNameVisibility.Values.Contains(mapNameVisibility))
        {
            return SettingsErrors.MapNameVisibility;
        }

        await store.UpdateAsync(player, locale, mapNameVisibility, cancellationToken);
        return Result.Success();
    }
}

/// <summary>Dress the character with items the player owns (base items, or unlocked ones).</summary>
public sealed class ChangeAppearance(IPlayerSettingsStore store, ISkinCatalogProvider catalogs)
{
    public async Task<Result> ExecuteAsync(PlayerId player, Appearance appearance, CancellationToken cancellationToken)
    {
        var catalog = await catalogs.GetAsync(cancellationToken);
        var owned = await store.OwnedItemsAsync(player, cancellationToken);
        var check = AppearanceValidator.Validate(appearance, catalog, owned);
        if (check.IsFailure)
        {
            return check;
        }

        await store.SaveAppearanceAsync(player, appearance, cancellationToken);
        return Result.Success();
    }
}
