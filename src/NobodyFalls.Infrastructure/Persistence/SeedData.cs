using NobodyFalls.Domain.Rules;
using NobodyFalls.Infrastructure.Settings;

namespace NobodyFalls.Infrastructure.Persistence;

/// <summary>
/// Data every database starts with: the first version of the rules and the skin
/// tones. The base wardrobe is seeded by the <c>SeedWardrobe</c> migration. All of
/// it is editable afterwards.
/// </summary>
internal static class SeedData
{
    public static readonly DateTimeOffset SeededAt = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);

    public static readonly string[] SkinTones = ["#C9C3B9", "#E0D3C4", "#B49A86", "#8D6E5A", "#5E4536"];

    public static GameSettingsRow FirstSettings => new()
    {
        Version = 1,
        Document = GameRulesJson.Serialize(GameRules.Default),
        CreatedAt = SeededAt,
        Comment = "Defaults from docs/GAME_DESIGN.md",
    };
}
