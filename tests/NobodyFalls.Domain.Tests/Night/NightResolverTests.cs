using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Friends;
using NobodyFalls.Domain.Night;
using NobodyFalls.Domain.Players;
using NobodyFalls.Testing;

namespace NobodyFalls.Domain.Tests.Night;

public sealed class NightResolverTests
{
    private static PlayerNightResult ResultOf(NightOutcome outcome, PlayerId player) =>
        outcome.Players.Single(p => p.PlayerId == player);

    private static HandScore Score(NightOutcome outcome, long handId) =>
        outcome.Hands.Single(h => h.HandId == handId);

    [Fact]
    public void A_player_with_enough_hands_survives_and_one_short_falls()
    {
        var day = new DayBuilder();
        var lea = day.Player("lea");
        var kenji = day.Player("kenji");
        var yuki = day.Player("yuki");
        day.Hand(lea, yuki).Hand(kenji, yuki).Hand(yuki, lea);

        var outcome = day.Resolve();

        Assert.Equal(NightResult.Survived, ResultOf(outcome, yuki).Result);
        Assert.Equal(NightResult.Fell, ResultOf(outcome, lea).Result);
        Assert.Equal(NightResult.Fell, ResultOf(outcome, kenji).Result);
        Assert.Equal(2, outcome.Falls);
    }

    [Fact]
    public void An_immune_newcomer_does_not_fall_even_without_hands()
    {
        var day = new DayBuilder();
        var newcomer = day.Player("newcomer", required: 2, immune: true);

        var outcome = day.Resolve();

        Assert.Equal(NightResult.Immune, ResultOf(outcome, newcomer).Result);
        Assert.Equal(0, outcome.Falls);
    }

    [Fact]
    public void Holding_a_stranger_earns_two_points_and_a_friend_one()
    {
        var day = new DayBuilder();
        var me = day.Player("me", required: 0);
        var friend = day.Player("friend", required: 1);
        var stranger = day.Player("stranger", required: 1);
        day.Hand(me, friend, friend: true).Hand(me, stranger);

        var outcome = day.Resolve();

        Assert.Equal(1, Score(outcome, 1).Points);
        Assert.Equal(2, Score(outcome, 2).Points);
        Assert.Equal(new GuardianAward(me, 3), Assert.Single(outcome.Awards));
    }

    [Fact]
    public void A_hand_given_to_someone_who_falls_earns_nothing()
    {
        var day = new DayBuilder();
        var me = day.Player("me", required: 0);
        var ines = day.Player("ines", required: 2);
        day.Hand(me, ines);

        var outcome = day.Resolve();

        Assert.Equal(0, Score(outcome, 1).Points);
        Assert.Empty(outcome.Awards);
    }

    [Fact]
    public void A_hand_given_to_an_immune_newcomer_earns_points()
    {
        var day = new DayBuilder();
        var me = day.Player("me", required: 0);
        var newcomer = day.Player("newcomer", required: 3, immune: true);
        day.Hand(me, newcomer);

        Assert.Equal(2, Score(day.Resolve(), 1).Points);
    }

    [Fact]
    public void Reciprocal_hands_earn_nothing_for_either_player()
    {
        var day = new DayBuilder();
        var a = day.Player("a", required: 1);
        var b = day.Player("b", required: 1);
        day.Hand(a, b, at: "23:30").Hand(b, a, at: "23:40");

        var outcome = day.Resolve();

        Assert.All(outcome.Hands, h =>
        {
            Assert.True(h.Reciprocal);
            Assert.False(h.LastRampart);
            Assert.Equal(0, h.Points);
        });
        Assert.Empty(outcome.Awards);
        Assert.Equal(0, outcome.Falls);
    }

    [Fact]
    public void The_hand_that_reaches_the_threshold_in_the_last_hour_is_a_last_stand()
    {
        var day = new DayBuilder();
        var tom = day.Player("tom", required: 0);
        var lea = day.Player("lea", required: 0);
        var me = day.Player("me", required: 0);
        var yuki = day.Player("yuki", required: 2);
        day.Hand(tom, yuki, at: "10:00")      // 1st: before the window
           .Hand(me, yuki, at: "23:15")       // 2nd: reaches the threshold
           .Hand(lea, yuki, at: "23:50");     // 3rd: Yuki was already safe

        var outcome = day.Resolve();

        Assert.False(Score(outcome, 1).LastRampart);
        Assert.Equal(2, Score(outcome, 1).Points);
        Assert.True(Score(outcome, 2).LastRampart);
        Assert.Equal(4, Score(outcome, 2).Points);
        Assert.False(Score(outcome, 3).LastRampart);
        Assert.Equal(2, Score(outcome, 3).Points);
    }

    [Fact]
    public void The_window_starts_exactly_one_hour_before_midnight()
    {
        var day = new DayBuilder();
        var me = day.Player("me", required: 0);
        var other = day.Player("other", required: 0);
        var a = day.Player("a", required: 1);
        var b = day.Player("b", required: 1);
        day.Hand(me, a, at: "22:59:59").Hand(other, b, at: "23:00:00");

        var outcome = day.Resolve();

        Assert.False(Score(outcome, 1).LastRampart);
        Assert.True(Score(outcome, 2).LastRampart);
    }

    [Fact]
    public void A_newcomer_cannot_be_saved_at_the_last_stand_since_they_could_not_fall()
    {
        var day = new DayBuilder();
        var me = day.Player("me", required: 0);
        var newcomer = day.Player("newcomer", required: 1, immune: true);
        day.Hand(me, newcomer, at: "23:30");

        var score = Score(day.Resolve(), 1);

        Assert.False(score.LastRampart);
        Assert.Equal(2, score.Points);
    }

    [Fact]
    public void Hands_given_at_the_same_instant_are_ordered_by_id()
    {
        var day = new DayBuilder();
        var a = day.Player("a", required: 0);
        var b = day.Player("b", required: 0);
        var target = day.Player("target", required: 1);
        day.Hand(a, target, at: "23:30").Hand(b, target, at: "23:30");

        var outcome = day.Resolve();

        Assert.True(Score(outcome, 1).LastRampart);
        Assert.False(Score(outcome, 2).LastRampart);
    }

    [Fact]
    public void When_a_survivor_had_exactly_the_hands_needed_every_giver_saved_them()
    {
        var day = new DayBuilder();
        var lea = day.Player("lea", required: 0);
        var kenji = day.Player("kenji", required: 0);
        var me = day.Player("me", required: 2);
        day.Hand(lea, me).Hand(kenji, me);

        var outcome = day.Resolve();

        Assert.Equal(
            new[] { new LifeDebt(me, lea), new LifeDebt(me, kenji) }.OrderBy(d => d.Saver),
            outcome.LifeDebts);
    }

    [Fact]
    public void With_one_hand_to_spare_no_single_hand_was_decisive()
    {
        var day = new DayBuilder();
        var a = day.Player("a", required: 0);
        var b = day.Player("b", required: 0);
        var c = day.Player("c", required: 0);
        var me = day.Player("me", required: 2);
        day.Hand(a, me).Hand(b, me).Hand(c, me);

        Assert.Empty(day.Resolve().LifeDebts);
    }

    [Fact]
    public void Watcher_hands_count_for_survival_but_earn_nothing_and_create_no_debt()
    {
        var day = new DayBuilder();
        var watcher = Ids.Of("watcher");
        var lea = day.Player("lea", required: 0);
        var ines = day.Player("ines", required: 2);
        day.WatcherHand(watcher, ines).Hand(lea, ines);

        var outcome = day.Resolve();

        Assert.Equal(NightResult.Survived, ResultOf(outcome, ines).Result);
        Assert.Equal(0, Score(outcome, 1).Points);
        Assert.Equal(new LifeDebt(ines, lea), Assert.Single(outcome.LifeDebts));
        Assert.DoesNotContain(outcome.Awards, a => a.PlayerId == watcher);
        Assert.DoesNotContain(outcome.HeldTogether, p => p.Contains(watcher));
    }

    [Fact]
    public void A_night_without_falls_extends_the_world_streak()
    {
        var day = new DayBuilder { PreviousStreak = 12, PreviousDeathsAllTime = 40 };
        day.Player("alone", required: 0);

        var outcome = day.Resolve();

        Assert.Equal(13, outcome.Streak);
        Assert.Equal(40, outcome.DeathsAllTime);
    }

    [Fact]
    public void Any_fall_resets_the_streak_and_adds_one_death_per_fall()
    {
        var day = new DayBuilder { PreviousStreak = 12, PreviousDeathsAllTime = 40 };
        day.Player("a");
        day.Player("b");

        var outcome = day.Resolve();

        Assert.Equal(0, outcome.Streak);
        Assert.Equal(42, outcome.DeathsAllTime);
    }

    [Fact]
    public void Players_who_held_each_other_form_one_pair()
    {
        var day = new DayBuilder();
        var a = day.Player("a", required: 0);
        var b = day.Player("b", required: 0);
        day.Hand(a, b).Hand(b, a);

        Assert.Equal(PlayerPair.Of(a, b), Assert.Single(day.Resolve().HeldTogether));
    }

    [Fact]
    public void A_player_not_in_play_can_still_give_and_earn()
    {
        var day = new DayBuilder();
        var fallen = Ids.Of("fallen-not-back-yet");
        var me = day.Player("me", required: 1);
        day.Hand(fallen, me);

        var outcome = day.Resolve();

        Assert.Equal(new GuardianAward(fallen, 2), Assert.Single(outcome.Awards));
        Assert.DoesNotContain(outcome.Players, p => p.PlayerId == fallen);
    }

    [Fact]
    public void An_empty_day_is_a_quiet_night()
    {
        var outcome = new DayBuilder { PreviousStreak = 3 }.Resolve();

        Assert.Equal(0, outcome.Falls);
        Assert.Equal(4, outcome.Streak);
    }

    [Fact]
    public void Inconsistent_snapshots_are_rejected()
    {
        var self = new DayBuilder();
        var a = self.Player("a");
        self.Hand(a, a);
        Assert.Throws<ArgumentException>(() => self.Resolve());

        var duplicate = new DayBuilder();
        var b = duplicate.Player("b");
        var c = duplicate.Player("c");
        duplicate.Hand(b, c).Hand(b, c, at: "13:00");
        Assert.Throws<ArgumentException>(() => duplicate.Resolve());

        var outOfDay = new DayBuilder();
        var d = outOfDay.Player("d");
        var e = outOfDay.Player("e");
        outOfDay.Hand(d, e, at: "1.00:00:00");
        Assert.Throws<ArgumentException>(() => outOfDay.Resolve());
    }

    [Fact]
    public void Configured_points_are_used()
    {
        var day = new DayBuilder
        {
            Rules = Domain.Rules.GameRules.Default with { PointsFriend = 5, PointsStranger = 7, LastRampartBonus = 11 },
        };
        var me = day.Player("me", required: 0);
        var friend = day.Player("friend", required: 1);
        var stranger = day.Player("stranger", required: 1);
        day.Hand(me, friend, friend: true).Hand(me, stranger, at: "23:30");

        var outcome = day.Resolve();

        Assert.Equal(5, Score(outcome, 1).Points);
        Assert.Equal(18, Score(outcome, 2).Points);
    }

    [Fact]
    public void Givers_kind_is_taken_from_the_hand()
    {
        var hand = new HandRecord(1, Ids.Of("w"), PlayerKind.Watcher, Ids.Of("x"), DayBuilder.At("10:00"), GiverWasFriend: true);
        var snapshot = new DaySnapshot(
            DayBuilder.Day,
            Domain.Rules.GameRules.Default,
            [new PlayerDayEntry(Ids.Of("x"), 1, false)],
            [hand],
            0,
            0);

        var outcome = NightResolver.Resolve(snapshot);

        Assert.Equal(0, outcome.Hands.Single().Points);
        Assert.Equal(NightResult.Survived, outcome.Players.Single().Result);
    }
}
