using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Rules;

namespace NobodyFalls.Domain.Tests.Rules;

public sealed class TimingPolicyTests
{
    private static readonly GameDay Day = GameDay.Parse("2026-11-13");
    private static readonly GameRules Rules = GameRules.Default;

    private static DateTimeOffset At(int hour, int minute = 0) => Day.StartsAt.AddHours(hour).AddMinutes(minute);

    [Fact]
    public void A_storm_is_announced_six_hours_before_midnight() =>
        Assert.Equal(At(18), StormPolicy.AnnouncementAt(Day, Rules));

    [Fact]
    public void A_storm_is_scheduled_before_its_day_starts_so_thresholds_never_move()
    {
        Assert.True(StormPolicy.CanSchedule(Day, Day.StartsAt.AddSeconds(-1)).IsSuccess);
        Assert.True(StormPolicy.CanSchedule(Day.AddDays(3), At(23)).IsSuccess);
        Assert.Equal(StormPolicy.TooLate, StormPolicy.CanSchedule(Day, Day.StartsAt).Error);
        Assert.Equal(StormPolicy.TooLate, StormPolicy.CanSchedule(Day, At(9)).Error);
    }

    [Fact]
    public void A_fallen_player_coming_back_early_plays_today() =>
        Assert.Equal(Day, ReturnPolicy.ReturnDay(At(17, 59), Rules));

    [Fact]
    public void Exactly_at_the_cutoff_the_player_still_plays_today() =>
        Assert.Equal(Day, ReturnPolicy.ReturnDay(At(18), Rules));

    [Fact]
    public void A_fallen_player_coming_back_late_plays_tomorrow() =>
        Assert.Equal(Day.Next(), ReturnPolicy.ReturnDay(At(23, 50), Rules));

    [Fact]
    public void Game_days_run_from_midnight_to_midnight_utc()
    {
        var parisEvening = new DateTimeOffset(2026, 11, 14, 0, 30, 0, TimeSpan.FromHours(1));

        Assert.Equal(Day, GameDay.Of(parisEvening));
        Assert.True(Day.Contains(Day.StartsAt));
        Assert.False(Day.Contains(Day.MidnightAt));
        Assert.Equal("2026-11-13", Day.ToString());
        Assert.Equal(1, Day.Next().DaysSince(Day));
    }
}
