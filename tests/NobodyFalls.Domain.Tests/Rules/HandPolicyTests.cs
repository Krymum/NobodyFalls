using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Players;
using NobodyFalls.Domain.Rules;
using NobodyFalls.Domain.Tests.Support;

namespace NobodyFalls.Domain.Tests.Rules;

public sealed class HandPolicyTests
{
    private static readonly HandAttempt Valid = new()
    {
        Giver = Ids.Of("me"),
        Receiver = Ids.Of("yuki"),
        ReceiverKind = PlayerKind.Human,
        ReceiverInPlay = true,
        GiverHandsUsedToday = 2,
        GiverAlreadyHoldsReceiverToday = false,
        Blocked = false,
        SeasonOpen = true,
        DayOpen = true,
    };

    private static DomainError? Check(HandAttempt attempt) => HandPolicy.Check(attempt, GameRules.Default).Error;

    [Fact]
    public void The_last_hand_of_the_day_can_be_given() => Assert.Null(Check(Valid));

    [Fact]
    public void No_fourth_hand() =>
        Assert.Equal(HandErrors.NoneLeft, Check(Valid with { GiverHandsUsedToday = 3 }));

    [Fact]
    public void Nobody_holds_themselves() =>
        Assert.Equal(HandErrors.Self, Check(Valid with { Receiver = Valid.Giver }));

    [Fact]
    public void Two_hands_to_the_same_player_on_the_same_day_are_refused() =>
        Assert.Equal(HandErrors.Duplicate, Check(Valid with { GiverAlreadyHoldsReceiverToday = true }));

    [Fact]
    public void A_block_looks_like_an_unknown_player_so_it_is_never_revealed() =>
        Assert.Equal(HandErrors.ReceiverNotFound, Check(Valid with { Blocked = true }));

    [Fact]
    public void Unknown_players_cannot_be_held() =>
        Assert.Equal(HandErrors.ReceiverNotFound, Check(Valid with { ReceiverKind = null }));

    [Fact]
    public void Watchers_are_never_held() =>
        Assert.Equal(HandErrors.ReceiverIsWatcher, Check(Valid with { ReceiverKind = PlayerKind.Watcher }));

    [Fact]
    public void Players_out_of_play_today_cannot_be_held() =>
        Assert.Equal(HandErrors.ReceiverNotInPlay, Check(Valid with { ReceiverInPlay = false }));

    [Fact]
    public void Nothing_happens_before_the_season_opens() =>
        Assert.Equal(HandErrors.SeasonClosed, Check(Valid with { SeasonOpen = false }));

    [Fact]
    public void Hands_wait_while_midnight_is_resolved() =>
        Assert.Equal(HandErrors.NightResolving, Check(Valid with { DayOpen = false }));

    [Fact]
    public void The_number_of_hands_per_day_is_configurable()
    {
        var rules = GameRules.Default with { HandsPerDay = 5 };
        Assert.True(HandPolicy.Check(Valid with { GiverHandsUsedToday = 4 }, rules).IsSuccess);
    }
}
