using NobodyFalls.Domain.Skins;

namespace NobodyFalls.Domain.Tests.Skins;

public sealed class SkinTests
{
    private static readonly SkinCatalog Catalog = new(
        [
            new SkinItem("coat-long", Slot.Coat, SkinRarity.Base, "coat.long", ["#4A5463", "#6A6744"]),
            new SkinItem("trousers", Slot.Trousers, SkinRarity.Base, "trousers.plain", ["#262A31"]),
            new SkinItem("boots", Slot.Boots, SkinRarity.Base, "boots.plain", ["#16191E"]),
            new SkinItem("beanie", Slot.Headwear, SkinRarity.Base, "headwear.beanie", ["#7A3931", "#585C63"]),
            new SkinItem("scarf-rescuer", Slot.Scarf, SkinRarity.Unlockable, "scarf.long", ["#B5AD9C"], new UnlockCondition(UnlockMetric.Rescues, 3)),
            new SkinItem("hat-season-1", Slot.Headwear, SkinRarity.Exclusive, "headwear.brim", ["#34343A"]),
        ],
        ["#C9C3B9", "#8D6E5A"]);

    private static readonly Appearance Me = new(
        "#C9C3B9",
        new Dictionary<Slot, SlotChoice>
        {
            [Slot.Coat] = new("coat-long", "#4A5463"),
            [Slot.Trousers] = new("trousers", "#262A31"),
            [Slot.Boots] = new("boots", "#16191E"),
            [Slot.Headwear] = new("beanie", "#7A3931"),
        });

    private static readonly IReadOnlySet<string> NothingOwned = new HashSet<string>();

    private static Appearance With(Slot slot, SlotChoice? choice)
    {
        var slots = new Dictionary<Slot, SlotChoice>(Me.Slots);
        if (choice is null)
        {
            slots.Remove(slot);
        }
        else
        {
            slots[slot] = choice;
        }

        return Me with { Slots = slots };
    }

    [Fact]
    public void The_test_catalog_is_valid() => Assert.Empty(Catalog.Validate());

    [Fact]
    public void A_character_made_of_base_items_is_valid() =>
        Assert.True(AppearanceValidator.Validate(Me, Catalog, NothingOwned).IsSuccess);

    [Fact]
    public void Appearance_errors()
    {
        Assert.Equal(AppearanceErrors.SkinTone, AppearanceValidator.Validate(Me with { SkinTone = "#FFFFFF" }, Catalog, NothingOwned).Error);
        Assert.Equal(AppearanceErrors.MissingSlot, AppearanceValidator.Validate(With(Slot.Coat, null), Catalog, NothingOwned).Error);
        Assert.Equal(AppearanceErrors.UnknownItem, AppearanceValidator.Validate(With(Slot.Scarf, new("nope", "#000000")), Catalog, NothingOwned).Error);
        Assert.Equal(AppearanceErrors.WrongSlot, AppearanceValidator.Validate(With(Slot.Scarf, new("beanie", "#7A3931")), Catalog, NothingOwned).Error);
        Assert.Equal(AppearanceErrors.Color, AppearanceValidator.Validate(With(Slot.Coat, new("coat-long", "#123456")), Catalog, NothingOwned).Error);
    }

    [Fact]
    public void Unlocked_items_can_be_worn_only_once_owned()
    {
        var withScarf = With(Slot.Scarf, new("scarf-rescuer", "#B5AD9C"));

        Assert.Equal(AppearanceErrors.NotOwned, AppearanceValidator.Validate(withScarf, Catalog, NothingOwned).Error);
        Assert.True(AppearanceValidator.Validate(withScarf, Catalog, new HashSet<string> { "scarf-rescuer" }).IsSuccess);
    }

    [Fact]
    public void Items_unlock_through_solidarity_only_once()
    {
        var progress = new PlayerProgress(Rescues: 3, StrangersHeld: 0, NightsSurvived: 0, StormsSurvived: 0, GuardianScore: 0, BestSeasonRank: null);

        Assert.Equal("scarf-rescuer", Assert.Single(UnlockEvaluator.NewlyUnlocked(Catalog, NothingOwned, progress)).Id);
        Assert.Empty(UnlockEvaluator.NewlyUnlocked(Catalog, new HashSet<string> { "scarf-rescuer" }, progress));
        Assert.Empty(UnlockEvaluator.NewlyUnlocked(Catalog, NothingOwned, progress with { Rescues = 2 }));
    }

    [Theory]
    [InlineData(UnlockMetric.StrangersHeld, 5, true)]
    [InlineData(UnlockMetric.NightsSurvived, 31, false)]
    [InlineData(UnlockMetric.StormsSurvived, 1, true)]
    [InlineData(UnlockMetric.GuardianScore, 100, true)]
    [InlineData(UnlockMetric.SeasonTopRank, 100, true)]
    [InlineData(UnlockMetric.SeasonTopRank, 10, false)]
    public void Each_metric_is_compared_to_its_threshold(UnlockMetric metric, int threshold, bool met)
    {
        var progress = new PlayerProgress(0, 5, 30, 1, 120, BestSeasonRank: 42);
        Assert.Equal(met, UnlockEvaluator.IsMet(new UnlockCondition(metric, threshold), progress));
    }

    [Fact]
    public void Clothes_never_use_status_colours()
    {
        Assert.True(StatusColorGuard.IsReserved("#D8A85A"));
        Assert.True(StatusColorGuard.IsReserved("#D06A60"));
        Assert.True(StatusColorGuard.IsReserved("#C9A05A"));

        // Every clothing colour of the reference scene stays clear of them.
        string[] reference = ["#6A6744", "#2E4749", "#4A5463", "#7A3931", "#B5AD9C", "#4E5A3C", "#585C63", "#5A2E39", "#CFC7BA", "#6F757E", "#857A65", "#34343A", "#3B3F45", "#2E3238", "#2A1E19"];
        Assert.All(reference, c => Assert.False(StatusColorGuard.IsReserved(c), c));
    }

    [Fact]
    public void Catalog_errors_are_reported()
    {
        var broken = new SkinCatalog(
            [
                new SkinItem("coat", Slot.Coat, SkinRarity.Base, "coat", ["#D0645F"]),
                new SkinItem("coat", Slot.Coat, SkinRarity.Base, "coat", ["red"]),
                new SkinItem("gift", Slot.Scarf, SkinRarity.Unlockable, "scarf", ["#B5AD9C"]),
                new SkinItem("odd", Slot.Scarf, SkinRarity.Base, "scarf", [], new UnlockCondition(UnlockMetric.Rescues, 0)),
            ],
            []);

        var codes = broken.Validate().Select(e => e.Code).ToHashSet();

        Assert.Superset(
            new HashSet<string>
            {
                "catalog.duplicate_id", "catalog.reserved_color", "catalog.bad_color", "catalog.missing_condition",
                "catalog.unexpected_condition", "catalog.no_color", "catalog.bad_threshold", "catalog.missing_base",
                "catalog.skin_tones",
            },
            codes);
    }
}
