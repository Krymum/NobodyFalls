using NobodyFalls.Domain.Common;

namespace NobodyFalls.Domain.Rules;

/// <summary>Storm nights: +1 hand for everyone, announced ahead of time.</summary>
public static class StormPolicy
{
    public static readonly DomainError TooLate =
        new("storm.too_late", "A storm must be scheduled before its day starts.");

    /// <summary>When players are pushed a reminder about the storm on <paramref name="day"/>.</summary>
    public static DateTimeOffset AnnouncementAt(GameDay day, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return day.MidnightAt - rules.StormNotice;
    }

    /// <summary>
    /// A storm is part of its day from the first second: the threshold players see
    /// never changes during a day. It must therefore be scheduled before the day
    /// starts; the push announcement comes later, <see cref="GameRules.StormNotice"/>
    /// before midnight.
    /// </summary>
    public static Result CanSchedule(GameDay day, DateTimeOffset now) =>
        now < day.StartsAt ? Result.Success() : TooLate;
}
