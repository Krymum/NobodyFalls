using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Players;
using NobodyFalls.Domain.Rules;
using NobodyFalls.Domain.Tests.Support;

namespace NobodyFalls.Domain.Tests.Players;

public sealed class PlayerTests
{
    private static readonly GameDay Day = GameDay.Parse("2026-11-13");
    private static readonly Pseudo Name = Pseudo.FromTrusted("yuki");

    private static Player Alive() => Player.Register(Ids.Of("yuki"), Name, Day, seasonOpen: true, invitedBy: null);

    [Fact]
    public void Signing_up_during_a_season_puts_the_player_in_play_at_once()
    {
        var inviter = Ids.Of("lea");
        var player = Player.Register(Ids.Of("yuki"), Name, Day, seasonOpen: true, invitedBy: inviter);

        Assert.Equal(PlayerStatus.Active, player.Status);
        Assert.Equal(Day, player.JoinedDay);
        Assert.Equal(inviter, player.InvitedBy);
        Assert.True(player.IsAlive);
    }

    [Fact]
    public void Before_the_season_players_wait_and_all_enter_on_the_opening_night()
    {
        var player = Player.Register(Ids.Of("yuki"), Name, Day, seasonOpen: false, invitedBy: null);
        Assert.Equal(PlayerStatus.Waitlisted, player.Status);
        Assert.Null(player.JoinedDay);

        var opening = Day.AddDays(10);
        player.EnterOpeningSeason(opening);

        Assert.Equal(PlayerStatus.Active, player.Status);
        Assert.Equal(opening, player.JoinedDay);
    }

    [Fact]
    public void A_fall_is_recorded_and_nothing_else_is_lost()
    {
        var player = Alive();

        player.Fall(Day);

        Assert.Equal(PlayerStatus.Fallen, player.Status);
        Assert.Equal(1, player.FallsCount);
        Assert.Equal(Day, player.LastFallDay);
        Assert.Equal(Day, player.JoinedDay);
    }

    [Fact]
    public void Only_living_humans_can_fall()
    {
        var player = Alive();
        player.Fall(Day);
        Assert.Throws<InvalidOperationException>(() => player.Fall(Day.Next()));

        var watcher = Player.CreateWatcher(Ids.Of("w"), Pseudo.FromTrusted("veilleur"));
        Assert.Throws<InvalidOperationException>(() => watcher.Fall(Day));
    }

    [Fact]
    public void A_fallen_player_coming_back_during_the_day_is_in_play_at_once()
    {
        var player = Alive();
        player.Fall(Day);

        var outcome = player.Return(Day.Next().StartsAt.AddHours(9), GameRules.Default);

        Assert.Equal(ReturnOutcome.BackToday, outcome);
        Assert.True(player.IsAlive);
        Assert.Null(player.ReturnsOnDay);
    }

    [Fact]
    public void A_fallen_player_coming_back_late_is_in_play_the_next_day()
    {
        var player = Alive();
        player.Fall(Day);
        var tomorrow = Day.Next();

        var outcome = player.Return(tomorrow.StartsAt.AddHours(23), GameRules.Default);

        Assert.Equal(ReturnOutcome.BackTomorrow, outcome);
        Assert.Equal(PlayerStatus.Fallen, player.Status);
        Assert.Equal(tomorrow.Next(), player.ReturnsOnDay);

        player.CompleteScheduledReturn(tomorrow.Next());
        Assert.True(player.IsAlive);
        Assert.Null(player.ReturnsOnDay);
    }

    [Fact]
    public void Coming_back_is_idempotent()
    {
        var player = Alive();
        player.Fall(Day);
        var visit = Day.Next().StartsAt.AddHours(10);

        player.Return(visit, GameRules.Default);

        Assert.Equal(ReturnOutcome.NotFallen, player.Return(visit, GameRules.Default));
        Assert.Equal(1, player.FallsCount);
    }

    [Fact]
    public void A_scheduled_return_is_not_completed_early()
    {
        var player = Alive();
        player.Fall(Day);
        player.Return(Day.Next().StartsAt.AddHours(23), GameRules.Default);

        player.CompleteScheduledReturn(Day.Next());

        Assert.Equal(PlayerStatus.Fallen, player.Status);
    }

    [Fact]
    public void A_returning_player_is_a_regular_again_not_a_newcomer()
    {
        var player = Alive();
        player.Fall(Day.Next());
        var returnDay = Day.AddDays(2);
        player.Return(returnDay.StartsAt.AddHours(8), GameRules.Default);

        Assert.Equal(PlayerStanding.Regular, ThresholdPolicy.StandingOn(returnDay, player.JoinedDay!.Value));
    }
}
