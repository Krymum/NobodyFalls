using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Seasons;

namespace NobodyFalls.Domain.Tests.Seasons;

public sealed class SeasonCalendarTests
{
    private static readonly GameDay Today = GameDay.Parse("2026-11-15");

    private static Season S(int number, string opening, string? last = null) =>
        Season.Plan(number, $"S{number}", GameDay.Parse(opening), last is null ? null : GameDay.Parse(last)).Value;

    [Fact]
    public void The_next_season_opens_after_the_current_one_ends_and_after_today()
    {
        Season[] seasons = [S(1, "2026-09-01", "2026-12-31")];

        var next = SeasonCalendar.Plan(seasons, "Hiver", GameDay.Parse("2027-01-01"), null, Today);

        Assert.True(next.IsSuccess);
        Assert.Equal(2, next.Value.Number);
        Assert.Equal(SeasonCalendar.Overlap, SeasonCalendar.Plan(seasons, "Hiver", GameDay.Parse("2026-12-31"), null, Today).Error);
        Assert.Equal(SeasonCalendar.AlreadyStarted, SeasonCalendar.Plan([], "Une", Today, null, Today).Error);
    }

    [Fact]
    public void A_season_without_a_last_day_must_be_given_one_before_the_next_is_planned()
    {
        Season[] seasons = [S(1, "2026-09-01")];

        Assert.Equal(SeasonCalendar.Overlap, SeasonCalendar.Plan(seasons, "Hiver", GameDay.Parse("2027-01-01"), null, Today).Error);
    }

    [Fact]
    public void An_open_season_keeps_its_opening_day_and_cannot_end_in_the_past()
    {
        Season[] seasons = [S(1, "2026-09-01")];

        Assert.True(SeasonCalendar.Change(seasons, 1, "Automne", GameDay.Parse("2026-09-01"), Today, Today).IsSuccess);
        Assert.Equal(SeasonCalendar.AlreadyStarted, SeasonCalendar.Change(seasons, 1, "S1", GameDay.Parse("2026-09-02"), null, Today).Error);
        Assert.Equal(SeasonCalendar.PastEnd, SeasonCalendar.Change(seasons, 1, "S1", GameDay.Parse("2026-09-01"), Today.Previous(), Today).Error);
    }

    [Fact]
    public void A_planned_season_moves_freely_until_it_opens_but_never_over_its_neighbours()
    {
        Season[] seasons = [S(1, "2026-09-01", "2026-12-31"), S(2, "2027-01-01")];

        Assert.True(SeasonCalendar.Change(seasons, 2, "Hiver", GameDay.Parse("2027-01-15"), GameDay.Parse("2027-03-31"), Today).IsSuccess);
        Assert.Equal(SeasonCalendar.Overlap, SeasonCalendar.Change(seasons, 2, "Hiver", GameDay.Parse("2026-12-20"), null, Today).Error);
        // The current season cannot run into the next one, nor lose its last day.
        Assert.Equal(SeasonCalendar.Overlap, SeasonCalendar.Change(seasons, 1, "S1", GameDay.Parse("2026-09-01"), GameDay.Parse("2027-01-01"), Today).Error);
        Assert.Equal(SeasonCalendar.Overlap, SeasonCalendar.Change(seasons, 1, "S1", GameDay.Parse("2026-09-01"), null, Today).Error);
    }

    [Fact]
    public void A_finished_season_is_history()
    {
        Season[] seasons = [S(1, "2026-01-01", "2026-06-30"), S(2, "2026-09-01")];

        Assert.Equal(SeasonCalendar.AlreadyClosed, SeasonCalendar.Change(seasons, 1, "S1", GameDay.Parse("2026-01-01"), GameDay.Parse("2026-07-31"), Today).Error);
        Assert.Equal(SeasonCalendar.NotFound, SeasonCalendar.Change(seasons, 9, "S9", Today.Next(), null, Today).Error);
    }
}
