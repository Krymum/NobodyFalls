using NobodyFalls.Domain.Common;

namespace NobodyFalls.Domain.Rules;

/// <summary>Storm nights: +1 hand for everyone, announced ahead of time.</summary>
public static class StormPolicy
{
    public static readonly DomainError TooLate =
        new("storm.too_late", "A storm must be scheduled before it is announced to players.");

    public static readonly DomainError InThePast =
        new("storm.in_the_past", "This night has already been resolved.");

    /// <summary>When players are told about the storm on <paramref name="day"/>.</summary>
    public static DateTimeOffset AnnouncementAt(GameDay day, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return day.MidnightAt - rules.StormNotice;
    }

    /// <summary>
    /// A storm can be scheduled up to the moment it must be announced, so that every
    /// player gets the full notice. A storm on the current day therefore raises the
    /// thresholds already set for that day.
    /// </summary>
    public static Result CanSchedule(GameDay day, DateTimeOffset now, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (now >= day.MidnightAt)
        {
            return InThePast;
        }

        return now <= AnnouncementAt(day, rules) ? Result.Success() : TooLate;
    }
}
