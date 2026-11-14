using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Realtime;
using NobodyFalls.Domain.Common;

namespace NobodyFalls.Application.Friends;

/// <summary>Friend requests that the other player should see at once.</summary>
public sealed class FriendActions(IFriendStore store, IRealtimePublisher realtime, IClock clock)
{
    public async Task<Result<FriendRequestResult>> RequestAsync(PlayerId from, string pseudo, CancellationToken cancellationToken)
    {
        var result = await store.RequestAsync(from, pseudo, clock.UtcNow, cancellationToken);
        if (result.IsSuccess)
        {
            realtime.Publish(
                new Audience.Player(result.Value.Target),
                result.Value.Outcome == FriendRequestOutcome.Sent ? new FriendRequestReceived(from) : new FriendshipMade(from));
        }

        return result;
    }

    public async Task<Result> AcceptAsync(PlayerId player, long requestId, CancellationToken cancellationToken)
    {
        var result = await store.AcceptAsync(player, requestId, clock.UtcNow, cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        realtime.Publish(new Audience.Player(result.Value), new FriendshipMade(player));
        return Result.Success();
    }
}
