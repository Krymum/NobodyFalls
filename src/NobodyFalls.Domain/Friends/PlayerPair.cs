using NobodyFalls.Domain.Common;

namespace NobodyFalls.Domain.Friends;

/// <summary>
/// An unordered pair of distinct players, stored canonically (<see cref="Low"/> &lt;
/// <see cref="High"/>) so that a friendship or a "held each other" counter exists once.
/// </summary>
public readonly record struct PlayerPair
{
    private PlayerPair(PlayerId low, PlayerId high)
    {
        Low = low;
        High = high;
    }

    public PlayerId Low { get; }

    public PlayerId High { get; }

    public static PlayerPair Of(PlayerId a, PlayerId b)
    {
        if (a == b)
        {
            throw new ArgumentException("A pair needs two different players.", nameof(b));
        }

        return a < b ? new PlayerPair(a, b) : new PlayerPair(b, a);
    }

    public bool Contains(PlayerId player) => Low == player || High == player;

    public PlayerId Other(PlayerId player) =>
        player == Low ? High
        : player == High ? Low
        : throw new ArgumentException("The player is not part of this pair.", nameof(player));
}
