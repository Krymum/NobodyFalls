using System.Globalization;

namespace NobodyFalls.Domain.Common;

/// <summary>
/// A game day: 00:00:00 to 23:59:59 UTC. Hands are valid for one game day only,
/// and the day is resolved at <see cref="MidnightAt"/> (the start of the next day).
/// </summary>
public readonly record struct GameDay(DateOnly Date) : IComparable<GameDay>
{
    public static GameDay Of(DateTimeOffset instant) => new(DateOnly.FromDateTime(instant.UtcDateTime));

    public static GameDay Parse(string isoDate) =>
        new(DateOnly.ParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture));

    /// <summary>First instant of the day (00:00:00 UTC).</summary>
    public DateTimeOffset StartsAt => new(Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>The midnight that ends this day and triggers its resolution.</summary>
    public DateTimeOffset MidnightAt => StartsAt.AddDays(1);

    public GameDay Next() => new(Date.AddDays(1));

    public GameDay Previous() => new(Date.AddDays(-1));

    public GameDay AddDays(int days) => new(Date.AddDays(days));

    /// <summary>Number of days from <paramref name="earlier"/> to this day (0 when equal).</summary>
    public int DaysSince(GameDay earlier) => Date.DayNumber - earlier.Date.DayNumber;

    public bool Contains(DateTimeOffset instant) => instant >= StartsAt && instant < MidnightAt;

    public int CompareTo(GameDay other) => Date.CompareTo(other.Date);

    public override string ToString() => Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static bool operator <(GameDay left, GameDay right) => left.CompareTo(right) < 0;

    public static bool operator >(GameDay left, GameDay right) => left.CompareTo(right) > 0;

    public static bool operator <=(GameDay left, GameDay right) => left.CompareTo(right) <= 0;

    public static bool operator >=(GameDay left, GameDay right) => left.CompareTo(right) >= 0;
}
