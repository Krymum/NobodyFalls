using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Rules;

namespace NobodyFalls.Domain.Friends;

/// <summary>Facts about a friend request, gathered by the application layer.</summary>
public sealed record FriendRequestAttempt
{
    public required PlayerId From { get; init; }

    public required PlayerId To { get; init; }

    /// <summary>False when the target does not exist, was deleted or is a Watcher.</summary>
    public required bool TargetExists { get; init; }

    public required bool Blocked { get; init; }

    public required bool AlreadyFriends { get; init; }

    /// <summary>A pending request from <see cref="From"/> to <see cref="To"/> already exists.</summary>
    public required bool AlreadyRequested { get; init; }

    public required int FromFriendCount { get; init; }

    public required int ToFriendCount { get; init; }
}

public static class FriendErrors
{
    public static readonly DomainError Self = new("friend.self", "You cannot befriend yourself.");

    /// <summary>Also returned when players blocked each other, so that a block is never revealed.</summary>
    public static readonly DomainError NotFound = new("friend.not_found", "This player cannot be found.");
    public static readonly DomainError AlreadyFriends = new("friend.already_friends", "You are already friends.");
    public static readonly DomainError AlreadyRequested = new("friend.already_requested", "Your request is already waiting for an answer.");
    public static readonly DomainError LimitReached = new("friend.limit_reached", "You have reached the maximum number of friends.");
    public static readonly DomainError TargetLimitReached = new("friend.target_limit_reached", "This player has reached the maximum number of friends.");
}

/// <summary>Mutual friendship: request then acceptance, or automatic through an invitation (GAME_DESIGN §4).</summary>
public static class FriendPolicy
{
    /// <remarks>
    /// When the target already sent a request to the sender, the application accepts
    /// that request instead of creating a second one.
    /// </remarks>
    public static Result CanRequest(FriendRequestAttempt attempt, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(rules);

        if (attempt.From == attempt.To)
        {
            return FriendErrors.Self;
        }

        if (!attempt.TargetExists || attempt.Blocked)
        {
            return FriendErrors.NotFound;
        }

        if (attempt.AlreadyFriends)
        {
            return FriendErrors.AlreadyFriends;
        }

        if (attempt.AlreadyRequested)
        {
            return FriendErrors.AlreadyRequested;
        }

        return CanBecomeFriends(attempt.FromFriendCount, attempt.ToFriendCount, rules);
    }

    /// <summary>Accepting re-checks the limit, which may have been reached since the request.</summary>
    public static Result CanAccept(int accepterFriendCount, int requesterFriendCount, GameRules rules) =>
        CanBecomeFriends(accepterFriendCount, requesterFriendCount, rules);

    /// <summary>
    /// Joining through an invitation link befriends the inviter automatically, unless a
    /// limit is reached: the sign-up still succeeds, without the friendship.
    /// </summary>
    public static bool BefriendsOnInvitation(int inviterFriendCount, int newcomerFriendCount, GameRules rules) =>
        CanBecomeFriends(newcomerFriendCount, inviterFriendCount, rules).IsSuccess;

    private static Result CanBecomeFriends(int ownCount, int otherCount, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (ownCount >= rules.FriendLimit)
        {
            return FriendErrors.LimitReached;
        }

        return otherCount >= rules.FriendLimit ? FriendErrors.TargetLimitReached : Result.Success();
    }
}
