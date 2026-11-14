using NobodyFalls.Api.Realtime;
using NobodyFalls.Application.Realtime;
using NobodyFalls.Domain.Common;
using NobodyFalls.Testing;

namespace NobodyFalls.Api.Tests;

public sealed class RealtimeBatchTests
{
    private static readonly PlayerId Me = Ids.Of("me");
    private static readonly PlayerId Yuki = Ids.Of("yuki");
    private static readonly PlayerId Lea = Ids.Of("lea");

    private static readonly Dictionary<PlayerId, IReadOnlyList<PlayerId>> NoFriends = [];

    [Fact]
    public void Successive_states_of_the_same_thing_collapse_into_the_latest()
    {
        var batch = RealtimeBatch.From(
            [
                (new Audience.Player(Me), new MyHandsChanged(2)),
                (new Audience.Player(Me), new MyHandsChanged(1)),
                (Audience.EdgeViewers, new EdgeChanged(Yuki, 0, 2)),
                (Audience.EdgeViewers, new EdgeChanged(Lea, 0, 2)),
                (Audience.EdgeViewers, new EdgeChanged(Yuki, 1, 2)),
            ],
            NoFriends);

        Assert.Equal([new MyHandsChanged(1)], batch.Players[Me].Events);
        Assert.Equal([new EdgeChanged(Yuki, 1, 2), new EdgeChanged(Lea, 0, 2)], batch.Edge.Events);
    }

    [Fact]
    public void Every_hand_received_is_kept()
    {
        var at = DateTimeOffset.UnixEpoch;
        var batch = RealtimeBatch.From(
            [
                (new Audience.Player(Me), new HandReceived(Yuki, at, 1, 2)),
                (new Audience.Player(Me), new HandReceived(Lea, at, 2, 2)),
            ],
            NoFriends);

        Assert.Equal(2, batch.Players[Me].Events.Count);
    }

    [Fact]
    public void Friends_audiences_are_expanded_to_each_friend()
    {
        var friends = new Dictionary<PlayerId, IReadOnlyList<PlayerId>> { [Yuki] = [Me, Lea] };

        var batch = RealtimeBatch.From([(new Audience.FriendsOf(Yuki), new PlayerStanding(Yuki, 1, 2))], friends);

        Assert.Equal(2, batch.Players.Count);
        Assert.Contains(Me, batch.Players.Keys);
        Assert.Contains(Lea, batch.Players.Keys);
        Assert.True(batch.Edge.IsEmpty);
        Assert.True(batch.Everyone.IsEmpty);
    }
}
