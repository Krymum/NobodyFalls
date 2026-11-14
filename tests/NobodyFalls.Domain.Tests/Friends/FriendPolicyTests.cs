using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Friends;
using NobodyFalls.Domain.Rules;
using NobodyFalls.Testing;

namespace NobodyFalls.Domain.Tests.Friends;

public sealed class FriendPolicyTests
{
    private static readonly FriendRequestAttempt Valid = new()
    {
        From = Ids.Of("me"),
        To = Ids.Of("lea"),
        TargetExists = true,
        Blocked = false,
        AlreadyFriends = false,
        AlreadyRequested = false,
        FromFriendCount = 10,
        ToFriendCount = 10,
    };

    private static DomainError? Check(FriendRequestAttempt attempt) =>
        FriendPolicy.CanRequest(attempt, GameRules.Default).Error;

    [Fact]
    public void A_request_can_be_sent() => Assert.Null(Check(Valid));

    [Fact]
    public void Requests_are_refused_in_these_cases()
    {
        Assert.Equal(FriendErrors.Self, Check(Valid with { To = Valid.From }));
        Assert.Equal(FriendErrors.NotFound, Check(Valid with { TargetExists = false }));
        Assert.Equal(FriendErrors.NotFound, Check(Valid with { Blocked = true }));
        Assert.Equal(FriendErrors.AlreadyFriends, Check(Valid with { AlreadyFriends = true }));
        Assert.Equal(FriendErrors.AlreadyRequested, Check(Valid with { AlreadyRequested = true }));
        Assert.Equal(FriendErrors.LimitReached, Check(Valid with { FromFriendCount = 500 }));
        Assert.Equal(FriendErrors.TargetLimitReached, Check(Valid with { ToFriendCount = 500 }));
    }

    [Fact]
    public void Accepting_rechecks_the_limit()
    {
        Assert.True(FriendPolicy.CanAccept(499, 499, GameRules.Default).IsSuccess);
        Assert.Equal(FriendErrors.LimitReached, FriendPolicy.CanAccept(500, 3, GameRules.Default).Error);
    }

    [Fact]
    public void An_invitation_befriends_unless_the_inviter_is_full()
    {
        Assert.True(FriendPolicy.BefriendsOnInvitation(12, 0, GameRules.Default));
        Assert.False(FriendPolicy.BefriendsOnInvitation(500, 0, GameRules.Default));
    }

    [Fact]
    public void A_pair_is_the_same_whichever_way_round()
    {
        var a = Ids.Of("a");
        var b = Ids.Of("b");

        Assert.Equal(PlayerPair.Of(a, b), PlayerPair.Of(b, a));
        Assert.True(PlayerPair.Of(a, b).Low < PlayerPair.Of(a, b).High);
        Assert.Equal(b, PlayerPair.Of(a, b).Other(a));
        Assert.Throws<ArgumentException>(() => PlayerPair.Of(a, a));
    }
}
