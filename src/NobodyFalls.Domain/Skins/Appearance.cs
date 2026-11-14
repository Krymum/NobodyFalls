using NobodyFalls.Domain.Common;

namespace NobodyFalls.Domain.Skins;

public sealed record SlotChoice(string ItemId, string Color);

/// <summary>
/// What a character looks like, as data: a skin tone and one item per worn slot.
/// The renderer turns this into layers; the server only ever stores and validates it.
/// </summary>
public sealed record Appearance(string SkinTone, IReadOnlyDictionary<Slot, SlotChoice> Slots);

public static class AppearanceErrors
{
    public static readonly DomainError SkinTone = new("appearance.skin_tone", "Unknown skin tone.");
    public static readonly DomainError MissingSlot = new("appearance.missing_slot", "A coat, trousers and boots are required.");
    public static readonly DomainError UnknownItem = new("appearance.unknown_item", "Unknown item.");
    public static readonly DomainError WrongSlot = new("appearance.wrong_slot", "This item does not go there.");
    public static readonly DomainError NotOwned = new("appearance.not_owned", "This item has not been unlocked yet.");
    public static readonly DomainError Color = new("appearance.color", "This colour is not available for this item.");
}

public static class AppearanceValidator
{
    /// <param name="owned">Ids of the non-base items the player unlocked.</param>
    public static Result Validate(Appearance appearance, SkinCatalog catalog, IReadOnlySet<string> owned)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(owned);

        if (!catalog.SkinTones.Contains(appearance.SkinTone, StringComparer.OrdinalIgnoreCase))
        {
            return AppearanceErrors.SkinTone;
        }

        if (SkinCatalog.RequiredSlots.Any(s => !appearance.Slots.ContainsKey(s)))
        {
            return AppearanceErrors.MissingSlot;
        }

        foreach (var (slot, choice) in appearance.Slots)
        {
            var item = catalog.Find(choice.ItemId);
            if (item is null)
            {
                return AppearanceErrors.UnknownItem;
            }

            if (item.Slot != slot)
            {
                return AppearanceErrors.WrongSlot;
            }

            if (item.Rarity != SkinRarity.Base && !owned.Contains(item.Id))
            {
                return AppearanceErrors.NotOwned;
            }

            if (!item.Colors.Contains(choice.Color, StringComparer.OrdinalIgnoreCase))
            {
                return AppearanceErrors.Color;
            }
        }

        return Result.Success();
    }
}
