using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Players;
using NobodyFalls.Domain.Rules;

namespace NobodyFalls.Domain.Night;

/// <summary>A player in play on the day being resolved.</summary>
/// <param name="Required">Threshold for the day, storm and fragility included.</param>
/// <param name="Immune">First night after joining: cannot fall.</param>
public sealed record PlayerDayEntry(PlayerId PlayerId, int Required, bool Immune);

/// <summary>A hand given during the day.</summary>
/// <param name="Id">Monotonic identifier; breaks ties between hands given at the same instant.</param>
/// <param name="GiverWasFriend">Friendship at the moment of giving, frozen so that unfriending before midnight changes nothing.</param>
public sealed record HandRecord(
    long Id,
    PlayerId Giver,
    PlayerKind GiverKind,
    PlayerId Receiver,
    DateTimeOffset GivenAt,
    bool GiverWasFriend);

/// <summary>Everything needed to resolve one midnight, and nothing more.</summary>
/// <param name="PreviousStreak">World streak (nights without any fall) before this night.</param>
/// <param name="PreviousDeathsAllTime">Falls since launch before this night.</param>
public sealed record DaySnapshot(
    GameDay Day,
    GameRules Rules,
    IReadOnlyList<PlayerDayEntry> Players,
    IReadOnlyList<HandRecord> Hands,
    int PreviousStreak,
    long PreviousDeathsAllTime);
