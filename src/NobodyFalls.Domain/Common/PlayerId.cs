namespace NobodyFalls.Domain.Common;

/// <summary>Identity of a player (human or Watcher).</summary>
/// <remarks>
/// Ordering follows <see cref="Guid.CompareTo(Guid)"/>, which matches PostgreSQL's
/// <c>uuid</c> ordering. Canonical pairs (<see cref="Friends.PlayerPair"/>) rely on it.
/// </remarks>
public readonly record struct PlayerId(Guid Value) : IComparable<PlayerId>
{
    public int CompareTo(PlayerId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("D");

    public static bool operator <(PlayerId left, PlayerId right) => left.CompareTo(right) < 0;

    public static bool operator >(PlayerId left, PlayerId right) => left.CompareTo(right) > 0;

    public static bool operator <=(PlayerId left, PlayerId right) => left.CompareTo(right) <= 0;

    public static bool operator >=(PlayerId left, PlayerId right) => left.CompareTo(right) >= 0;
}
