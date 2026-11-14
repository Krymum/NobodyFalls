using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Seasons;

namespace NobodyFalls.Domain.Tests.Seasons;

public sealed class SeasonTests
{
    private static readonly GameDay Opening = GameDay.Parse("2026-12-01");

    [Fact]
    public void Nights_are_counted_from_the_opening_day()
    {
        var season = Season.Plan(1, "Première saison", Opening).Value;

        Assert.Equal(1, season.NightNumber(Opening));
        Assert.Equal(41, season.NightNumber(Opening.AddDays(40)));
        Assert.Equal(Opening.StartsAt, season.OpensAt);
    }

    [Fact]
    public void A_season_is_open_between_its_first_and_last_day()
    {
        var season = Season.Plan(1, "S1", Opening, Opening.AddDays(89)).Value;

        Assert.False(season.IsOpenOn(Opening.Previous()));
        Assert.True(season.IsOpenAt(Opening.StartsAt));
        Assert.True(season.IsOpenOn(Opening.AddDays(89)));
        Assert.False(season.IsOpenOn(Opening.AddDays(90)));
    }

    [Fact]
    public void Inconsistent_seasons_are_refused()
    {
        Assert.Equal("season.number", Season.Plan(0, "S", Opening).Error?.Code);
        Assert.Equal("season.name", Season.Plan(1, " ", Opening).Error?.Code);
        Assert.Equal("season.dates", Season.Plan(1, "S", Opening, Opening.Previous()).Error?.Code);

        var season = Season.Plan(1, "S", Opening).Value;
        Assert.True(season.End(Opening.AddDays(30)).IsSuccess);
        Assert.True(season.End(Opening.Previous()).IsFailure);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_is_a_programming_error() =>
        Assert.Throws<InvalidOperationException>(() => Season.Plan(0, "S", Opening).Value);
}
