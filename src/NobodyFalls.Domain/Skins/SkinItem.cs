namespace NobodyFalls.Domain.Skins;

/// <summary>
/// Where a piece of clothing goes on the character. Each slot maps to one or more
/// layers of the character renderer.
/// </summary>
public enum Slot
{
    Hair,
    Headwear,
    Coat,
    Scarf,
    Gloves,
    Trousers,
    Boots,
}

public enum SkinRarity
{
    /// <summary>Owned by everyone from the start.</summary>
    Base,

    /// <summary>Earned through acts of solidarity. Nothing can be bought.</summary>
    Unlockable,

    /// <summary>Awarded once (end of season…), never through a regular condition.</summary>
    Exclusive,
}

/// <summary>A measurable act of solidarity that can unlock an item.</summary>
public enum UnlockMetric
{
    /// <summary>Life debts as the saver.</summary>
    Rescues,

    /// <summary>Hands given to non-friends who made it through the night.</summary>
    StrangersHeld,

    NightsSurvived,
    StormsSurvived,
    GuardianScore,

    /// <summary>Best rank reached in a season (lower is better).</summary>
    SeasonTopRank,
}

/// <summary>"Unlocked when <see cref="Metric"/> reaches <see cref="Threshold"/>".</summary>
public sealed record UnlockCondition(UnlockMetric Metric, int Threshold);

/// <summary>
/// A catalogue entry. A character is data: it references items by id and the
/// renderer draws them through <see cref="RenderKey"/> (code-drawn parts today,
/// an illustrated sprite atlas later).
/// </summary>
/// <param name="Colors">Allowed colours, as <c>#RRGGBB</c>. The first is the default.</param>
public sealed record SkinItem(
    string Id,
    Slot Slot,
    SkinRarity Rarity,
    string RenderKey,
    IReadOnlyList<string> Colors,
    UnlockCondition? Unlock = null);
