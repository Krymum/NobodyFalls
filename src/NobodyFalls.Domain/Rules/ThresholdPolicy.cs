using NobodyFalls.Domain.Common;

namespace NobodyFalls.Domain.Rules;

/// <summary>Where a player stands in their life cycle on a given day.</summary>
public enum PlayerStanding
{
    /// <summary>Joined (or the season opened) today: immune at the coming midnight.</summary>
    Newcomer,

    /// <summary>Second night: needs <see cref="GameRules.FragileExtra"/> more hands.</summary>
    Fragile,

    Regular,
}

/// <summary>How many hands a player needs to get through the night.</summary>
public static class ThresholdPolicy
{
    /// <summary>
    /// The threshold everybody starts from, given the living population at the start
    /// of the day: 1 while fewer than 50 players are alive, otherwise
    /// <c>min(2, alive − 1)</c> (GAME_DESIGN §3).
    /// </summary>
    public static int BaseRequired(GameRules rules, int alivePopulation)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentOutOfRangeException.ThrowIfNegative(alivePopulation);
        return alivePopulation < rules.LowPopulationBelow
            ? rules.LowPopulationRequired
            : Math.Max(0, Math.Min(rules.BaseRequired, alivePopulation - 1));
    }

    /// <summary>A player's threshold for the day: base, storm and fragility add up.</summary>
    public static int Required(GameRules rules, int baseRequired, bool storm, PlayerStanding standing)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return baseRequired
            + (storm ? rules.StormExtra : 0)
            + (standing == PlayerStanding.Fragile ? rules.FragileExtra : 0);
    }

    /// <summary>
    /// Standing is derived from the day the player entered the game (sign-up, or the
    /// season opening for the waiting list). A player coming back after a fall keeps
    /// their original day, so they come back as <see cref="PlayerStanding.Regular"/>.
    /// </summary>
    public static PlayerStanding StandingOn(GameDay day, GameDay joinedDay) =>
        day.DaysSince(joinedDay) switch
        {
            <= 0 => PlayerStanding.Newcomer,
            1 => PlayerStanding.Fragile,
            _ => PlayerStanding.Regular,
        };
}
