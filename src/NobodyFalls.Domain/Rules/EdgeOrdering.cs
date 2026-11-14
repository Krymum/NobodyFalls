using NobodyFalls.Domain.Common;

namespace NobodyFalls.Domain.Rules;

/// <summary>A player in play today, as seen from the Edge.</summary>
public sealed record EdgeCandidate(PlayerId PlayerId, int Received, int Required, bool Immune)
{
    public int Missing => Math.Max(0, Required - Received);
}

/// <summary>
/// The Edge of the void: players below their threshold, most urgent first
/// (GAME_DESIGN §2).
/// </summary>
public static class EdgeOrdering
{
    public static bool IsOnEdge(EdgeCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return candidate.Received < candidate.Required;
    }

    /// <summary>
    /// Order: players who can fall before immune newcomers; then most hands missing;
    /// then fewest hands received; then a per-day shuffle, so that everybody looking at
    /// the Edge does not rush to the same player at the top of the list.
    /// </summary>
    public static IReadOnlyList<EdgeCandidate> Order(GameDay day, IEnumerable<EdgeCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates
            .Where(IsOnEdge)
            .OrderBy(c => c.Immune)
            .ThenByDescending(c => c.Missing)
            .ThenBy(c => c.Received)
            .ThenBy(c => StableShuffle.Key(day, c.PlayerId))
            .ThenBy(c => c.PlayerId)
            .ToList();
    }
}
