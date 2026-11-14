using NobodyFalls.Domain.Common;

namespace NobodyFalls.Domain.Rules;

/// <summary>When a fallen player who comes back is put back in play.</summary>
public static class ReturnPolicy
{
    /// <summary>
    /// The current day if enough time is left before midnight
    /// (<see cref="GameRules.ReturnCutoff"/>), otherwise the next day.
    /// </summary>
    public static GameDay ReturnDay(DateTimeOffset visitAt, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var today = GameDay.Of(visitAt);
        return today.MidnightAt - visitAt >= rules.ReturnCutoff ? today : today.Next();
    }
}
