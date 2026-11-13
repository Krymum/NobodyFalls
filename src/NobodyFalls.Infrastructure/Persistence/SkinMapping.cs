using System.Text;
using NobodyFalls.Domain.Skins;

namespace NobodyFalls.Infrastructure.Persistence;

/// <summary>Maps skin enums to the snake_case words stored in the catalogue tables.</summary>
internal static class SkinMapping
{
    public static string ToDb<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }

    public static TEnum FromDb<TEnum>(string value)
        where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Single(v => ToDb(v) == value);

    public static SkinItem ToDomain(SkinItemRow row) => new(
        row.Id,
        FromDb<Slot>(row.Slot),
        FromDb<SkinRarity>(row.Rarity),
        row.RenderKey,
        row.Colors,
        row.UnlockMetric is null || row.UnlockThreshold is null
            ? null
            : new UnlockCondition(FromDb<UnlockMetric>(row.UnlockMetric), row.UnlockThreshold.Value));

    public static SkinItemRow ToRow(SkinItem item, int sort) => new()
    {
        Id = item.Id,
        Slot = ToDb(item.Slot),
        Rarity = ToDb(item.Rarity),
        RenderKey = item.RenderKey,
        Colors = [.. item.Colors],
        UnlockMetric = item.Unlock is null ? null : ToDb(item.Unlock.Metric),
        UnlockThreshold = item.Unlock?.Threshold,
        Sort = sort,
    };
}
