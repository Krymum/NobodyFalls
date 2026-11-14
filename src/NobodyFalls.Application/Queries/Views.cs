using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Players;
using NobodyFalls.Domain.Skins;

namespace NobodyFalls.Application.Queries;

// Read models. They are serialized as-is by the API (camelCase JSON), so they are
// the public contract of the game screens: change them with care.

public enum LifeStatus
{
    Waitlisted,
    Alive,
    Fallen,
}

/// <summary>A player as shown anywhere: name, character, and whether it is a Watcher.</summary>
public sealed record PlayerRef(PlayerId Id, string Pseudo, bool IsWatcher, Appearance? Appearance);

/// <summary>Where a player stands today.</summary>
public sealed record DayStanding(int Received, int Required, bool Immune);

public sealed record HandLink(PlayerRef Player, DateTimeOffset GivenAt, DayStanding? Standing);

/// <summary>The "Tonight" screen: my status, my hands, my neighbourhood.</summary>
/// <param name="FellOn">Set when the player fell at the last midnight they took part in and is seeing it for the first time.</param>
/// <param name="Returned">What happened when this visit brought a fallen player back.</param>
public sealed record TonightView(
    GameDay Day,
    int? NightNumber,
    DateTimeOffset ServerNow,
    DateTimeOffset MidnightAt,
    bool SeasonOpen,
    bool Storm,
    LifeStatus Status,
    DayStanding? Standing,
    int HandsLeft,
    IReadOnlyList<HandLink> HeldBy,
    IReadOnlyList<HandLink> Holding,
    GameDay? FellOn,
    ReturnOutcome? Returned);

public sealed record EdgeEntry(PlayerRef Player, int Received, int Required, bool Immune, bool IsFriend);

public sealed record EdgePage(IReadOnlyList<EdgeEntry> Entries, int? NextOffset);

public sealed record WorldView(
    GameDay Day,
    int? NightNumber,
    string? SeasonName,
    int Streak,
    int? LastNightFalls,
    int Alive,
    int UnderThreshold,
    long DeathsSeason,
    long DeathsAllTime,
    bool StormTonight);

public sealed record LifeDebtView(PlayerRef Player, GameDay Day);

public sealed record MyProfileView(
    PlayerRef Me,
    LifeStatus Status,
    int GuardianScore,
    int FallsCount,
    string InviteCode,
    string Locale,
    string MapNameVisibility,
    IReadOnlyList<LifeDebtView> SavedBy,
    IReadOnlyList<LifeDebtView> Saved);

public enum Relation
{
    None,
    Friends,
    RequestSent,
    RequestReceived,
}

public sealed record PublicProfileView(PlayerRef Player, LifeStatus Status, int GuardianScore, Relation Relation, int NightsHeldTogether);

public sealed record FriendView(PlayerRef Player, LifeStatus Status, DayStanding? Today, DateTimeOffset Since);

public sealed record FriendRequestView(long Id, PlayerRef Player, DateTimeOffset CreatedAt);

public sealed record FriendRequestsView(IReadOnlyList<FriendRequestView> Incoming, IReadOnlyList<FriendRequestView> Outgoing);

public sealed record SuggestionView(PlayerRef Player, int Nights);

public sealed record SessionPlayer(PlayerId Id, string Pseudo, LifeStatus Status, string Locale, Appearance? Appearance);

public sealed record InvitePreview(string Code, PlayerRef Inviter);

public sealed record CatalogItemView(
    string Id,
    Slot Slot,
    SkinRarity Rarity,
    string RenderKey,
    IReadOnlyList<string> Colors,
    UnlockCondition? Unlock,
    bool Owned);

public sealed record CatalogView(IReadOnlyList<CatalogItemView> Items, IReadOnlyList<string> SkinTones);
