using NobodyFalls.Application.Realtime;
using NobodyFalls.Domain.Common;

namespace NobodyFalls.Api.Realtime;

/// <summary>What a client receives: the events of the last second, oldest first.</summary>
public sealed record ServerBatch(DateTimeOffset ServerTime, IReadOnlyList<object> Events);

/// <summary>
/// Groups one second of events by channel (a player, the Edge, everyone) and keeps only
/// the latest event for each coalescing key. Pure: the publisher feeds it and sends the result.
/// </summary>
internal sealed class RealtimeBatch
{
    private readonly Dictionary<PlayerId, Channel> _players = [];
    private readonly Channel _edge = new();
    private readonly Channel _everyone = new();

    public IReadOnlyDictionary<PlayerId, Channel> Players => _players;

    public Channel Edge => _edge;

    public Channel Everyone => _everyone;

    public bool IsEmpty => _players.Count == 0 && _edge.IsEmpty && _everyone.IsEmpty;

    /// <param name="friendsOf">Friends of each player named in a <see cref="Audience.FriendsOf"/>.</param>
    public static RealtimeBatch From(
        IEnumerable<(Audience Audience, GameEvent Event)> events,
        IReadOnlyDictionary<PlayerId, IReadOnlyList<PlayerId>> friendsOf)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(friendsOf);

        var batch = new RealtimeBatch();
        foreach (var (audience, gameEvent) in events)
        {
            switch (audience)
            {
                case Audience.Player player:
                    batch.For(player.Id).Add(gameEvent);
                    break;
                case Audience.FriendsOf friends when friendsOf.TryGetValue(friends.Id, out var list):
                    foreach (var friend in list)
                    {
                        batch.For(friend).Add(gameEvent);
                    }

                    break;
                case Audience.FriendsOf:
                    break;
                case Audience.EdgeWatchers:
                    batch._edge.Add(gameEvent);
                    break;
                case Audience.Everyone:
                    batch._everyone.Add(gameEvent);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(events), audience, "Unknown audience.");
            }
        }

        return batch;
    }

    private Channel For(PlayerId player)
    {
        if (!_players.TryGetValue(player, out var channel))
        {
            channel = new Channel();
            _players[player] = channel;
        }

        return channel;
    }

    internal sealed class Channel
    {
        private readonly List<GameEvent> _events = [];
        private readonly Dictionary<string, int> _positions = [];

        public bool IsEmpty => _events.Count == 0;

        /// <summary>Events in order; a coalesced event takes the place of the one it replaces.</summary>
        public IReadOnlyList<GameEvent> Events => _events;

        public void Add(GameEvent gameEvent)
        {
            if (gameEvent.CoalesceKey is { } key)
            {
                if (_positions.TryGetValue(key, out var index))
                {
                    _events[index] = gameEvent;
                    return;
                }

                _positions[key] = _events.Count;
            }

            _events.Add(gameEvent);
        }
    }
}
