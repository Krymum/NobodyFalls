using NobodyFalls.Domain.Common;

namespace NobodyFalls.Domain.Feedback;

/// <summary>Why a player writes to the team (GAME_DESIGN §8–9).</summary>
public enum TicketCategory
{
    Support,

    /// <summary>Contesting a sanction: never handled by the person who issued it.</summary>
    Appeal,

    AccountDeletion,
}

public enum TicketStatus
{
    /// <summary>Waiting for the team.</summary>
    Open,

    /// <summary>The team answered and waits for the player.</summary>
    WaitingPlayer,

    Resolved,
    Closed,
}

public static class TicketErrors
{
    public static readonly DomainError Subject = new("ticket.subject", "A subject has 3 to 120 characters.");
    public static readonly DomainError Message = new("ticket.message", "A message has 1 to 2000 characters.");
    public static readonly DomainError Category = new("ticket.category", "Unknown category.");
    public static readonly DomainError DailyLimit = new("ticket.limit_reached", "Five requests a day at most.");
    public static readonly DomainError NotFound = new("ticket.not_found", "This request cannot be found.");
    public static readonly DomainError Closed = new("ticket.closed", "This request is closed. Open a new one if needed.");
}

public sealed record TicketDraft(TicketCategory Category, string Subject, string Message);

/// <summary>Requests to the team: support, appeals, account deletion. Pure rules.</summary>
public static class TicketRules
{
    public const int MaxPerDay = 5;
    public const int MaxMessage = 2000;

    public static Result<TicketDraft> Draft(string? category, string? subject, string? message)
    {
        if (ParseCategory(category) is not { } parsed)
        {
            return TicketErrors.Category;
        }

        var cleanSubject = Text.Clean(subject);
        if (cleanSubject.Length is < 3 or > 120)
        {
            return TicketErrors.Subject;
        }

        var body = Message(message);
        return body.IsSuccess ? Result.Success(new TicketDraft(parsed, cleanSubject, body.Value)) : body.Error!;
    }

    public static Result<string> Message(string? message)
    {
        var clean = Text.Clean(message, keepLines: true);
        return clean.Length is < 1 or > MaxMessage ? TicketErrors.Message : Result.Success(clean);
    }

    public static Result CanOpen(int openedToday) => openedToday >= MaxPerDay ? TicketErrors.DailyLimit : Result.Success();

    /// <summary>A player replies until the request is closed; a reply reopens a resolved request.</summary>
    public static Result<TicketStatus> AfterPlayerReply(TicketStatus status) =>
        status == TicketStatus.Closed ? TicketErrors.Closed : Result.Success(TicketStatus.Open);

    /// <summary>A public answer from the team puts the request back in the player's hands.</summary>
    public static Result<TicketStatus> AfterStaffReply(TicketStatus status) =>
        status == TicketStatus.Closed ? TicketErrors.Closed : Result.Success(TicketStatus.WaitingPlayer);

    public static string Name(this TicketCategory category) => category switch
    {
        TicketCategory.Support => "support",
        TicketCategory.Appeal => "appeal",
        TicketCategory.AccountDeletion => "account_deletion",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    public static TicketCategory? ParseCategory(string? name) => name switch
    {
        "support" => TicketCategory.Support,
        "appeal" => TicketCategory.Appeal,
        "account_deletion" => TicketCategory.AccountDeletion,
        _ => null,
    };

    public static string Name(this TicketStatus status) => status switch
    {
        TicketStatus.Open => "open",
        TicketStatus.WaitingPlayer => "waiting_player",
        TicketStatus.Resolved => "resolved",
        TicketStatus.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static TicketStatus? ParseStatus(string? name) => name switch
    {
        "open" => TicketStatus.Open,
        "waiting_player" => TicketStatus.WaitingPlayer,
        "resolved" => TicketStatus.Resolved,
        "closed" => TicketStatus.Closed,
        _ => null,
    };
}
