using NobodyFalls.Application.Queries;
using NobodyFalls.Domain.Common;

namespace NobodyFalls.Application.Friends;

public enum FriendRequestOutcome
{
    Sent,

    /// <summary>The other player had already asked: the friendship is made at once.</summary>
    BecameFriends,
}

/// <summary>
/// Friendship, requests and blocks (GAME_DESIGN §4). Each write checks the domain's
/// <c>FriendPolicy</c> inside its transaction.
/// </summary>
public interface IFriendStore
{
    Task<Result<FriendRequestOutcome>> RequestAsync(PlayerId from, string targetPseudo, DateTimeOffset now, CancellationToken cancellationToken);

    Task<Result> AcceptAsync(PlayerId player, long requestId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<Result> DeclineAsync(PlayerId player, long requestId, DateTimeOffset now, CancellationToken cancellationToken);

    Task<Result> CancelAsync(PlayerId player, long requestId, DateTimeOffset now, CancellationToken cancellationToken);

    Task RemoveAsync(PlayerId player, PlayerId other, CancellationToken cancellationToken);

    Task<IReadOnlyList<FriendView>> ListAsync(PlayerId player, GameDay today, CancellationToken cancellationToken);

    Task<FriendRequestsView> RequestsAsync(PlayerId player, CancellationToken cancellationToken);

    Task<IReadOnlyList<SuggestionView>> SuggestionsAsync(PlayerId player, int minNights, int limit, CancellationToken cancellationToken);

    /// <summary>Blocking ends the friendship and any pending request, both ways.</summary>
    Task<Result> BlockAsync(PlayerId player, PlayerId target, DateTimeOffset now, CancellationToken cancellationToken);

    Task UnblockAsync(PlayerId player, PlayerId target, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlayerRef>> BlockedAsync(PlayerId player, CancellationToken cancellationToken);
}

public static class FriendRequestErrors
{
    public static readonly DomainError NotFound = new("friend_request.not_found", "This request no longer exists.");
}
