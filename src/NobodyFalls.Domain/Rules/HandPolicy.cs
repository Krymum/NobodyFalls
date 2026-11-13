using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Players;

namespace NobodyFalls.Domain.Rules;

/// <summary>
/// Everything the rules need to know about an attempt to give a hand. The application
/// layer gathers these facts inside the same transaction that records the hand; the
/// database constraints remain the last line of defence against races.
/// </summary>
public sealed record HandAttempt
{
    public required PlayerId Giver { get; init; }

    public required PlayerId Receiver { get; init; }

    /// <summary>Null when no such player exists (or was deleted).</summary>
    public required PlayerKind? ReceiverKind { get; init; }

    /// <summary>The receiver is in play today (alive, or a fallen player who already came back).</summary>
    public required bool ReceiverInPlay { get; init; }

    public required int GiverHandsUsedToday { get; init; }

    public required bool GiverAlreadyHoldsReceiverToday { get; init; }

    /// <summary>Either player has blocked the other.</summary>
    public required bool Blocked { get; init; }

    public required bool SeasonOpen { get; init; }

    /// <summary>False while the previous midnight is being resolved or this day is already resolved.</summary>
    public required bool DayOpen { get; init; }
}

public static class HandErrors
{
    public static readonly DomainError SeasonClosed = new("hand.season_closed", "The season has not opened yet.");
    public static readonly DomainError NightResolving = new("hand.night_resolving", "Midnight is being resolved. Try again in a moment.");

    /// <summary>Also returned when players blocked each other, so that a block is never revealed.</summary>
    public static readonly DomainError ReceiverNotFound = new("hand.receiver_not_found", "This player cannot be found.");
    public static readonly DomainError Self = new("hand.self", "You cannot hold yourself.");
    public static readonly DomainError ReceiverIsWatcher = new("hand.receiver_is_watcher", "Watchers do not need to be held.");
    public static readonly DomainError ReceiverNotInPlay = new("hand.receiver_not_in_play", "This player is not in play today.");
    public static readonly DomainError NoneLeft = new("hand.none_left", "You have no hands left today.");
    public static readonly DomainError Duplicate = new("hand.duplicate", "You already hold this player today.");
}

/// <summary>Who may hold whom (GAME_DESIGN §2). A hand given is final.</summary>
public static class HandPolicy
{
    public static Result Check(HandAttempt attempt, GameRules rules)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(rules);

        if (!attempt.SeasonOpen)
        {
            return HandErrors.SeasonClosed;
        }

        if (!attempt.DayOpen)
        {
            return HandErrors.NightResolving;
        }

        if (attempt.Giver == attempt.Receiver)
        {
            return HandErrors.Self;
        }

        if (attempt.ReceiverKind is null || attempt.Blocked)
        {
            return HandErrors.ReceiverNotFound;
        }

        if (attempt.ReceiverKind == PlayerKind.Watcher)
        {
            return HandErrors.ReceiverIsWatcher;
        }

        if (!attempt.ReceiverInPlay)
        {
            return HandErrors.ReceiverNotInPlay;
        }

        if (attempt.GiverAlreadyHoldsReceiverToday)
        {
            return HandErrors.Duplicate;
        }

        return attempt.GiverHandsUsedToday >= rules.HandsPerDay ? HandErrors.NoneLeft : Result.Success();
    }
}
