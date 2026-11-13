using NobodyFalls.Domain.Common;

namespace NobodyFalls.Domain.Seasons;

/// <summary>
/// A season opens for everyone at the same midnight; players who signed up before
/// wait on the waiting list (GAME_DESIGN §3). End-of-season rules are phase 3.
/// </summary>
public sealed class Season
{
    private Season(int number, string name, GameDay openingDay, GameDay? lastDay)
    {
        Number = number;
        Name = name;
        OpeningDay = openingDay;
        LastDay = lastDay;
    }

    public int Number { get; }

    public string Name { get; }

    /// <summary>First game day of the season; it opens at its 00:00 UTC.</summary>
    public GameDay OpeningDay { get; }

    /// <summary>Last game day, if the end is already planned.</summary>
    public GameDay? LastDay { get; private set; }

    public DateTimeOffset OpensAt => OpeningDay.StartsAt;

    public static Result<Season> Plan(int number, string name, GameDay openingDay, GameDay? lastDay = null)
    {
        if (number < 1)
        {
            return new DomainError("season.number", "Seasons are numbered from 1.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return new DomainError("season.name", "A season needs a name.");
        }

        if (lastDay is { } end && end < openingDay)
        {
            return new DomainError("season.dates", "A season cannot end before it opens.");
        }

        return Result.Success(new Season(number, name.Trim(), openingDay, lastDay));
    }

    public bool IsOpenOn(GameDay day) => day >= OpeningDay && (LastDay is null || day <= LastDay);

    public bool IsOpenAt(DateTimeOffset instant) => IsOpenOn(GameDay.Of(instant));

    /// <summary>"Night 41": nights are counted from 1 on the opening day.</summary>
    public int NightNumber(GameDay day) => day.DaysSince(OpeningDay) + 1;

    public Result End(GameDay lastDay)
    {
        if (lastDay < OpeningDay)
        {
            return new DomainError("season.dates", "A season cannot end before it opens.");
        }

        LastDay = lastDay;
        return Result.Success();
    }
}
