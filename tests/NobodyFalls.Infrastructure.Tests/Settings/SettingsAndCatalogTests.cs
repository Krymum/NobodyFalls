using NobodyFalls.Application.Abstractions;
using NobodyFalls.Domain.Rules;
using NobodyFalls.Domain.Skins;
using NobodyFalls.Infrastructure.Persistence;
using NobodyFalls.Infrastructure.Tests.Support;

namespace NobodyFalls.Infrastructure.Tests.Settings;

public sealed class SettingsAndCatalogTests(PostgresFixture postgres)
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task A_new_database_starts_with_the_documented_rules()
    {
        await using var app = await TestApp.StartAsync(postgres, DateTimeOffset.UtcNow);

        var current = await app.Get<IGameSettingsProvider>().GetCurrentAsync(Ct);

        Assert.Equal(1, current.Version);
        Assert.Equal(GameRules.Default, current.Rules);
    }

    [Fact]
    public async Task Saving_rules_creates_a_new_version_and_keeps_the_old_one()
    {
        await using var app = await TestApp.StartAsync(postgres, DateTimeOffset.UtcNow);
        var provider = app.Get<IGameSettingsProvider>();

        var saved = await provider.SaveAsync(GameRules.Default with { BaseRequired = 3 }, "harder", null, Ct);
        var invalid = await provider.SaveAsync(GameRules.Default with { HandsPerDay = 0 }, "broken", null, Ct);

        Assert.Equal(2, saved.Value.Version);
        Assert.Equal(3, (await provider.GetCurrentAsync(Ct)).Rules.BaseRequired);
        Assert.Equal(2, (await provider.GetVersionAsync(1, Ct)).Rules.BaseRequired);
        Assert.Equal("rules.hands_per_day", invalid.Error?.Code);
    }

    [Fact]
    public async Task The_seeded_wardrobe_is_a_valid_catalogue()
    {
        await using var app = await TestApp.StartAsync(postgres, DateTimeOffset.UtcNow);

        var items = await app.Seed.QueryAsync(
            "select id, slot, rarity, render_key, colors, unlock_metric, unlock_threshold from skin_items",
            r => SkinMapping.ToDomain(new SkinItemRow
            {
                Id = r.GetString(0),
                Slot = r.GetString(1),
                Rarity = r.GetString(2),
                RenderKey = r.GetString(3),
                Colors = [.. r.GetFieldValue<string[]>(4)],
                UnlockMetric = r.IsDBNull(5) ? null : r.GetString(5),
                UnlockThreshold = r.IsDBNull(6) ? null : r.GetInt32(6),
            }));
        var tones = await app.Seed.QueryAsync("select color from skin_tones order by sort", r => r.GetString(0));

        var catalog = new SkinCatalog(items, tones);

        Assert.Empty(catalog.Validate());
        Assert.Equal(15, catalog.Items.Count);
        Assert.Equal(4, catalog.Items.Count(i => i.Rarity == SkinRarity.Unlockable));
    }
}
