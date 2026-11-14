namespace NobodyFalls.Infrastructure.Persistence;

// Persistence models: plain rows, one per table. Rules live in the domain; these
// classes only describe the schema (configured in NobodyFallsDbContext).

internal sealed class PlayerRow
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = DbValues.Human;
    public string Pseudo { get; set; } = "";
    public string PseudoKey { get; set; } = "";
    public string Status { get; set; } = DbValues.Active;
    public string Locale { get; set; } = "fr";
    public DateOnly? JoinedDay { get; set; }
    public Guid? InvitedById { get; set; }
    public string InviteCode { get; set; } = "";
    public int GuardianScore { get; set; }
    public int FallsCount { get; set; }
    public DateOnly? LastFallDay { get; set; }
    public DateOnly? ReturnsOnDay { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    // Phase 3 (map of the net): a stable position, assigned once.
    public float? MapX { get; set; }
    public float? MapY { get; set; }
    public int? TileX { get; set; }
    public int? TileY { get; set; }
    public string MapNameVisibility { get; set; } = "friends";
}

internal sealed class SeasonRow
{
    public int Id { get; set; }
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public DateOnly OpeningDay { get; set; }
    public DateOnly? LastDay { get; set; }
    public long Deaths { get; set; }
}

internal sealed class GameDayRow
{
    public DateOnly Day { get; set; }
    public int SeasonId { get; set; }
    public int NightNumber { get; set; }
    public int BaseRequired { get; set; }
    public bool Storm { get; set; }
    public int AliveAtStart { get; set; }
    public int WatcherCount { get; set; }
    public int SettingsVersion { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? WatcherPassAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public int? Falls { get; set; }
    public int? StreakAfter { get; set; }
    public long? DeathsAllTimeAfter { get; set; }
}

internal sealed class StormRow
{
    public DateOnly Day { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}

internal sealed class PlayerDayRow
{
    public DateOnly Day { get; set; }
    public Guid PlayerId { get; set; }
    public int Required { get; set; }
    public bool Immune { get; set; }
    public int Received { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public string? Outcome { get; set; }
    public int ScoreGained { get; set; }
}

internal sealed class HandRow
{
    public long Id { get; set; }
    public DateOnly Day { get; set; }
    public Guid GiverId { get; set; }
    public Guid ReceiverId { get; set; }
    public short Slot { get; set; }
    public DateTimeOffset GivenAt { get; set; }
    public string GiverKind { get; set; } = DbValues.Human;
    public bool GiverWasFriend { get; set; }
    public int? Points { get; set; }
    public bool? Reciprocal { get; set; }
    public bool? LastRampart { get; set; }
}

internal sealed class LifeDebtRow
{
    public DateOnly Day { get; set; }
    public Guid SavedId { get; set; }
    public Guid SaverId { get; set; }
}

internal sealed class PlayerFallRow
{
    public Guid PlayerId { get; set; }
    public DateOnly Day { get; set; }
    public int SeasonId { get; set; }
}

internal sealed class SeasonScoreRow
{
    public int SeasonId { get; set; }
    public Guid PlayerId { get; set; }
    public int Points { get; set; }
}

internal sealed class PlayerStatsRow
{
    public Guid PlayerId { get; set; }
    public int Rescues { get; set; }
    public int StrangersHeld { get; set; }
    public int NightsSurvived { get; set; }
    public int StormsSurvived { get; set; }
    public DateOnly UpdatedDay { get; set; }
}

internal sealed class FriendshipRow
{
    public Guid PlayerLow { get; set; }
    public Guid PlayerHigh { get; set; }
    public string Source { get; set; } = "request";
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class BlockRow
{
    public Guid BlockerId { get; set; }
    public Guid BlockedId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class HoldPairRow
{
    public Guid PlayerLow { get; set; }
    public Guid PlayerHigh { get; set; }
    public int Nights { get; set; }
    public DateOnly LastDay { get; set; }
}

internal sealed class SkinItemRow
{
    public string Id { get; set; } = "";
    public string Slot { get; set; } = "";
    public string Rarity { get; set; } = "";
    public string RenderKey { get; set; } = "";
    public List<string> Colors { get; init; } = [];
    public string? UnlockMetric { get; set; }
    public int? UnlockThreshold { get; set; }
    public bool Active { get; set; } = true;
    public int Sort { get; set; }
}

internal sealed class SkinToneRow
{
    public string Color { get; set; } = "";
    public int Sort { get; set; }
}

internal sealed class PlayerSkinUnlockRow
{
    public Guid PlayerId { get; set; }
    public string ItemId { get; set; } = "";
    public DateOnly Day { get; set; }
    public string Reason { get; set; } = "";
}

internal sealed class NotificationOutboxRow
{
    public long Id { get; set; }
    public string DedupeKey { get; set; } = "";
    public Guid PlayerId { get; set; }
    public string Kind { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public int Attempts { get; set; }
}

internal sealed class GameSettingsRow
{
    public int Version { get; set; }
    public string Document { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public string Comment { get; set; } = "";
}

internal sealed class DevClockRow
{
    public int Id { get; set; }
    public long OffsetMs { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
