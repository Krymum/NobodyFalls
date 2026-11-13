using NobodyFalls.Domain.Common;

namespace NobodyFalls.Domain.Skins;

/// <summary>
/// The wardrobe every character is built from. Stored in the database and editable
/// without redeploying; <see cref="Validate"/> runs before any change is accepted.
/// </summary>
public sealed class SkinCatalog
{
    private readonly Dictionary<string, SkinItem> _items;

    public SkinCatalog(IEnumerable<SkinItem> items, IEnumerable<string> skinTones)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(skinTones);
        Items = items.ToList();
        SkinTones = skinTones.ToList();
        _items = Items
            .GroupBy(i => i.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    /// <summary>Slots every character must fill (a character is never drawn naked).</summary>
    public static IReadOnlySet<Slot> RequiredSlots { get; } = new HashSet<Slot> { Slot.Coat, Slot.Trousers, Slot.Boots };

    public IReadOnlyList<SkinItem> Items { get; }

    public IReadOnlyList<string> SkinTones { get; }

    public SkinItem? Find(string itemId) => _items.GetValueOrDefault(itemId);

    public IEnumerable<SkinItem> BaseItems => Items.Where(i => i.Rarity == SkinRarity.Base);

    public IReadOnlyList<DomainError> Validate()
    {
        var errors = new List<DomainError>();

        foreach (var duplicate in Items.GroupBy(i => i.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            errors.Add(new DomainError("catalog.duplicate_id", $"Item '{duplicate.Key}' is defined more than once."));
        }

        foreach (var item in Items)
        {
            if (item.Colors.Count == 0)
            {
                errors.Add(new DomainError("catalog.no_color", $"Item '{item.Id}' needs at least one colour."));
            }

            foreach (var color in item.Colors)
            {
                if (!StatusColorGuard.TryParse(color, out _))
                {
                    errors.Add(new DomainError("catalog.bad_color", $"Item '{item.Id}': '{color}' is not #RRGGBB."));
                }
                else if (StatusColorGuard.IsReserved(color))
                {
                    errors.Add(new DomainError("catalog.reserved_color", $"Item '{item.Id}': '{color}' is too close to a status colour."));
                }
            }

            if (item.Rarity == SkinRarity.Unlockable && item.Unlock is null)
            {
                errors.Add(new DomainError("catalog.missing_condition", $"Item '{item.Id}' is unlockable but has no condition."));
            }

            if (item.Rarity != SkinRarity.Unlockable && item.Unlock is not null)
            {
                errors.Add(new DomainError("catalog.unexpected_condition", $"Item '{item.Id}' has a condition but is not unlockable."));
            }

            if (item.Unlock is { Threshold: < 1 })
            {
                errors.Add(new DomainError("catalog.bad_threshold", $"Item '{item.Id}': the threshold must be at least 1."));
            }
        }

        foreach (var slot in RequiredSlots.Where(s => !BaseItems.Any(i => i.Slot == s)))
        {
            errors.Add(new DomainError("catalog.missing_base", $"Slot {slot} needs at least one base item."));
        }

        if (SkinTones.Count == 0 || SkinTones.Any(t => !StatusColorGuard.TryParse(t, out _)))
        {
            errors.Add(new DomainError("catalog.skin_tones", "At least one #RRGGBB skin tone is needed."));
        }

        return errors;
    }
}
