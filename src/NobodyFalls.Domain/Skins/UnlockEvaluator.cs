namespace NobodyFalls.Domain.Skins;

/// <summary>A player's record of solidarity, as counted at midnight.</summary>
/// <param name="BestSeasonRank">Null until the player has been ranked at a season end.</param>
public sealed record PlayerProgress(
    int Rescues,
    int StrangersHeld,
    int NightsSurvived,
    int StormsSurvived,
    int GuardianScore,
    int? BestSeasonRank);

/// <summary>Skins are unlocked only by acts of solidarity (GAME_DESIGN §5).</summary>
public static class UnlockEvaluator
{
    public static bool IsMet(UnlockCondition condition, PlayerProgress progress)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(progress);
        return condition.Metric switch
        {
            UnlockMetric.Rescues => progress.Rescues >= condition.Threshold,
            UnlockMetric.StrangersHeld => progress.StrangersHeld >= condition.Threshold,
            UnlockMetric.NightsSurvived => progress.NightsSurvived >= condition.Threshold,
            UnlockMetric.StormsSurvived => progress.StormsSurvived >= condition.Threshold,
            UnlockMetric.GuardianScore => progress.GuardianScore >= condition.Threshold,
            UnlockMetric.SeasonTopRank => progress.BestSeasonRank is { } rank && rank <= condition.Threshold,
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition.Metric, "Unknown metric."),
        };
    }

    /// <summary>Unlockable items whose condition is now met and that the player does not own yet.</summary>
    public static IReadOnlyList<SkinItem> NewlyUnlocked(SkinCatalog catalog, IReadOnlySet<string> owned, PlayerProgress progress)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(owned);
        return catalog.Items
            .Where(i => i is { Rarity: SkinRarity.Unlockable, Unlock: not null })
            .Where(i => !owned.Contains(i.Id) && IsMet(i.Unlock!, progress))
            .ToList();
    }
}
