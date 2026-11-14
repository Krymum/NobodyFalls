using NobodyFalls.Domain.Common;

namespace NobodyFalls.Application.Realtime;

/// <summary>Who receives a real-time event.</summary>
public abstract record Audience
{
    private Audience()
    {
    }

    /// <summary>One player, on all their open tabs and devices.</summary>
    public sealed record Player(PlayerId Id) : Audience;

    /// <summary>The friends of a player (they see that player's status in their lists).</summary>
    public sealed record FriendsOf(PlayerId Id) : Audience;

    /// <summary>Everyone currently looking at the Edge of the void.</summary>
    public sealed record EdgeWatchers : Audience;

    public sealed record Everyone : Audience;

    public static Audience EdgeViewers { get; } = new EdgeWatchers();

    public static Audience All { get; } = new Everyone();
}

/// <summary>
/// An event pushed to clients. Events are batched: at most one message per second and
/// per channel (GAME_DESIGN §13). When two events share a <see cref="CoalesceKey"/>
/// within a batch, only the latest is sent.
/// </summary>
public abstract record GameEvent(string Type)
{
    /// <summary>Null when every occurrence matters.</summary>
    public virtual string? CoalesceKey => Type;
}

/// <summary>My hands left changed (I gave one).</summary>
public sealed record MyHandsChanged(int HandsLeft) : GameEvent("me.hands");

/// <summary>Someone held me. The client refreshes its neighbourhood.</summary>
public sealed record HandReceived(PlayerId From, DateTimeOffset At, int Received, int Required) : GameEvent("me.handReceived")
{
    public override string? CoalesceKey => null;
}

/// <summary>A player's hands for today changed (seen by their friends).</summary>
public sealed record PlayerStanding(PlayerId PlayerId, int Received, int Required) : GameEvent("player.standing")
{
    public override string? CoalesceKey => $"{Type}:{PlayerId}";
}

/// <summary>A player moved on the Edge: still below the threshold, or safe now.</summary>
public sealed record EdgeChanged(PlayerId PlayerId, int Received, int Required) : GameEvent("edge.changed")
{
    public bool OnEdge => Received < Required;

    public override string? CoalesceKey => $"{Type}:{PlayerId}";
}

public sealed record FriendRequestReceived(PlayerId From) : GameEvent("friend.request")
{
    public override string? CoalesceKey => null;
}

public sealed record FriendshipMade(PlayerId With) : GameEvent("friend.accepted")
{
    public override string? CoalesceKey => null;
}

public sealed record WorldStats(int Alive, int UnderThreshold) : GameEvent("world.stats");

/// <summary>Midnight has been resolved: clients reload their state for the new day.</summary>
public sealed record NightResolved(GameDay Day, int Falls, int Streak) : GameEvent("night.resolved");

/// <summary>
/// Publishes events to connected clients. Fire-and-forget: publishing never blocks a
/// request; batching and delivery happen in the background.
/// </summary>
public interface IRealtimePublisher
{
    void Publish(Audience audience, GameEvent gameEvent);
}

/// <summary>Friendships, as needed to deliver <see cref="Audience.FriendsOf"/> events.</summary>
public interface IFriendGraph
{
    Task<IReadOnlyDictionary<PlayerId, IReadOnlyList<PlayerId>>> FriendsOfAsync(IReadOnlyCollection<PlayerId> players, CancellationToken cancellationToken);
}

/// <summary>Default publisher where no client can listen (tests, tools). The API replaces it.</summary>
public sealed class NullRealtimePublisher : IRealtimePublisher
{
    public void Publish(Audience audience, GameEvent gameEvent)
    {
    }
}
