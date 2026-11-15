using NobodyFalls.Domain.Common;

namespace NobodyFalls.Domain.Seasons;

/// <summary>
/// What the team may change in the calendar of seasons (GAME_DESIGN §9): plan the next
/// one, change it until it opens, and set the last day of the current one. Seasons never
/// overlap, a day already started never moves, and a finished season is history.
/// </summary>
public static class SeasonCalendar
{
    public static readonly DomainError Overlap =
        new("season.overlap", "Seasons cannot overlap: the previous one must end before the next one opens.");

    public static readonly DomainError AlreadyStarted =
        new("season.already_started", "This day has started: the opening day of an open season cannot move.");

    public static readonly DomainError AlreadyClosed = new("season.already_closed", "This season is over and cannot change.");

    public static readonly DomainError NotFound = new("season.not_found", "No season has this number.");

    public static readonly DomainError PastEnd = new("season.past_end", "The last day cannot be in the past.");

    /// <summary>A new season, after the last one, opening on a day that has not started.</summary>
    public static Result<Season> Plan(IReadOnlyList<Season> seasons, string? name, GameDay openingDay, GameDay? lastDay, GameDay today)
    {
        ArgumentNullException.ThrowIfNull(seasons);
        if (openingDay <= today)
        {
            return AlreadyStarted;
        }

        var latest = seasons.MaxBy(s => s.Number);
        var planned = Season.Plan((latest?.Number ?? 0) + 1, name ?? "", openingDay, lastDay);
        if (planned.IsFailure)
        {
            return planned;
        }

        if (latest is not null && (latest.LastDay is not { } end || end >= openingDay))
        {
            return Overlap;
        }

        return planned;
    }

    /// <summary>The same season with new values, if the change is allowed today.</summary>
    public static Result<Season> Change(IReadOnlyList<Season> seasons, int number, string? name, GameDay openingDay, GameDay? lastDay, GameDay today)
    {
        ArgumentNullException.ThrowIfNull(seasons);
        var current = seasons.FirstOrDefault(s => s.Number == number);
        if (current is null)
        {
            return NotFound;
        }

        if (current.LastDay is { } over && over < today)
        {
            return AlreadyClosed;
        }

        var opened = current.OpeningDay <= today;
        if (opened ? openingDay != current.OpeningDay : openingDay <= today)
        {
            return AlreadyStarted;
        }

        if (lastDay is { } end && end < today)
        {
            return PastEnd;
        }

        var changed = Season.Plan(number, name ?? "", openingDay, lastDay);
        if (changed.IsFailure)
        {
            return changed;
        }

        var previous = seasons.Where(s => s.Number < number).MaxBy(s => s.Number);
        var next = seasons.Where(s => s.Number > number).MinBy(s => s.Number);
        if (previous is not null && (previous.LastDay is not { } previousEnd || previousEnd >= openingDay))
        {
            return Overlap;
        }

        if (next is not null && (lastDay is not { } nextBound || nextBound >= next.OpeningDay))
        {
            return Overlap;
        }

        return changed;
    }
}
