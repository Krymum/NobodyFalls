using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using NobodyFalls.Application.Realtime;
using NobodyFalls.Domain.Common;

namespace NobodyFalls.Api.Realtime;

internal sealed class RealtimeOptions
{
    /// <summary>One message per channel per interval (GAME_DESIGN §13: at most one per second).</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// Collects events from requests and background jobs and sends them in batches
/// (ADR 0008). With several instances, each batches its own events and the Redis
/// backplane routes them to the right connections.
/// </summary>
internal sealed partial class BatchingRealtimePublisher(
    IHubContext<GameHub> hub,
    IFriendGraph friends,
    TimeProvider time,
    RealtimeOptions options,
    ILogger<BatchingRealtimePublisher> logger) : BackgroundService, IRealtimePublisher
{
    private readonly Channel<(Audience, GameEvent)> _queue =
        Channel.CreateUnbounded<(Audience, GameEvent)>(new UnboundedChannelOptions { SingleReader = true });

    public void Publish(Audience audience, GameEvent gameEvent) => _queue.Writer.TryWrite((audience, gameEvent));

    /// <summary>Sends everything queued so far. Exposed for tests; the worker calls it every interval.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        var events = new List<(Audience Audience, GameEvent Event)>();
        while (_queue.Reader.TryRead(out var item))
        {
            events.Add(item);
        }

        if (events.Count == 0)
        {
            return;
        }

        var subjects = events.Select(e => e.Audience).OfType<Audience.FriendsOf>().Select(a => a.Id).Distinct().ToList();
        var friendsOf = subjects.Count == 0
            ? new Dictionary<PlayerId, IReadOnlyList<PlayerId>>()
            : await friends.FriendsOfAsync(subjects, cancellationToken);

        var batch = RealtimeBatch.From(events, friendsOf);
        var now = time.GetUtcNow();

        foreach (var (player, channel) in batch.Players)
        {
            await hub.Clients.User(player.ToString()).SendAsync(GameHub.BatchMethod, Envelope(now, channel), cancellationToken);
        }

        if (!batch.Edge.IsEmpty)
        {
            await hub.Clients.Group(GameHub.EdgeGroup).SendAsync(GameHub.BatchMethod, Envelope(now, batch.Edge), cancellationToken);
        }

        if (!batch.Everyone.IsEmpty)
        {
            await hub.Clients.All.SendAsync(GameHub.BatchMethod, Envelope(now, batch.Everyone), cancellationToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.FlushInterval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await FlushAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Real-time is best effort: clients resync on reconnect or night change.
            catch (Exception e)
#pragma warning restore CA1031
            {
                LogFlushFailed(e);
            }
        }
    }

    // Declared as object so that each event serializes with its own properties.
    private static ServerBatch Envelope(DateTimeOffset now, RealtimeBatch.Channel channel) =>
        new(now, channel.Events.Cast<object>().ToList());

    [LoggerMessage(Level = LogLevel.Warning, Message = "A real-time batch could not be sent.")]
    private partial void LogFlushFailed(Exception exception);
}
