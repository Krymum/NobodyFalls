using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Friends;

namespace NobodyFalls.Domain.Night;

public enum NightResult
{
    Survived,
    Fell,

    /// <summary>First night after joining: the player could not fall.</summary>
    Immune,
}

public sealed record PlayerNightResult(PlayerId PlayerId, int Received, int Required, NightResult Result);

/// <param name="Reciprocal">The receiver also held the giver that day: the hand earns nothing.</param>
/// <param name="LastRampart">Given in the last window and was the hand that reached the threshold.</param>
public sealed record HandScore(long HandId, int Points, bool Reciprocal, bool LastRampart);

/// <summary>"<see cref="Saved"/> was saved by <see cref="Saver"/>": without that hand, they would have fallen.</summary>
public sealed record LifeDebt(PlayerId Saved, PlayerId Saver);

/// <summary>Guardian points earned by a giver during the night.</summary>
public sealed record GuardianAward(PlayerId PlayerId, int Points);

/// <summary>
/// The result of a midnight. Lists are sorted deterministically so that two
/// implementations (C# reference and SQL) can be compared item by item.
/// </summary>
public sealed record NightOutcome(
    GameDay Day,
    IReadOnlyList<PlayerNightResult> Players,
    IReadOnlyList<HandScore> Hands,
    IReadOnlyList<GuardianAward> Awards,
    IReadOnlyList<LifeDebt> LifeDebts,
    IReadOnlyList<PlayerPair> HeldTogether,
    int Falls,
    int Streak,
    long DeathsAllTime);
