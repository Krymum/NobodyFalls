using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Feedback;

namespace NobodyFalls.Application.Feedback;

public enum IdeaSort
{
    Votes,
    New,
}

/// <summary>An idea as a player sees it.</summary>
public sealed record IdeaView(
    long Id,
    string Title,
    string Body,
    string Category,
    string Status,
    string Locale,
    string? AuthorPseudo,
    bool Mine,
    bool Hidden,
    int Votes,
    bool Voted,
    string? StaffAnswer,
    DateTimeOffset? AnsweredAt,
    long? MergedInto,
    DateTimeOffset CreatedAt);

public sealed record IdeaPage(IReadOnlyList<IdeaView> Items, int? NextOffset);

public sealed record VoteResult(long IdeaId, int Votes, bool Voted);

/// <summary>Ideas and votes (GAME_DESIGN §8). Daily limits are checked inside the write transaction.</summary>
public interface IIdeaStore
{
    Task<Result<long>> ProposeAsync(PlayerId author, IdeaDraft draft, string locale, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Visible ideas, not merged; the viewer's language first, then by votes or by date.
    /// The viewer also sees their own hidden ideas, marked as such.
    /// </summary>
    Task<IdeaPage> ListAsync(PlayerId viewer, string viewerLocale, IdeaStatus? status, IdeaSort sort, int offset, int limit, CancellationToken cancellationToken);

    Task<IdeaView?> GetAsync(PlayerId viewer, long id, CancellationToken cancellationToken);

    /// <summary>Adds or removes the viewer's vote (idempotent) and returns the new count.</summary>
    Task<Result<VoteResult>> VoteAsync(PlayerId voter, long ideaId, bool vote, CancellationToken cancellationToken);
}

public sealed record TicketMessageView(long Id, string From, string Body, DateTimeOffset At);

public sealed record TicketSummary(long Id, string Category, string Subject, string Status, DateTimeOffset UpdatedAt, bool AnswerWaiting);

public sealed record TicketView(long Id, string Category, string Subject, string Status, DateTimeOffset CreatedAt, IReadOnlyList<TicketMessageView> Messages);

/// <summary>Requests to the team, as players see them: internal team notes are never returned.</summary>
public interface ITicketStore
{
    Task<Result<long>> OpenAsync(PlayerId player, TicketDraft draft, string locale, DateTimeOffset now, CancellationToken cancellationToken);

    Task<Result> ReplyAsync(PlayerId player, long ticketId, string message, DateTimeOffset now, CancellationToken cancellationToken);

    Task<IReadOnlyList<TicketSummary>> MineAsync(PlayerId player, CancellationToken cancellationToken);

    Task<TicketView?> MineAsync(PlayerId player, long ticketId, CancellationToken cancellationToken);
}

/// <summary>A report about a player (by id or pseudo) or an idea.</summary>
public sealed record ReportRequest(ReportTarget Target, Guid? PlayerId, string? Pseudo, long? IdeaId, ReportDraft Draft);

/// <summary>The moderation queue, as players fill it (GAME_DESIGN §8).</summary>
public interface IReportStore
{
    Task<Result<long>> ReportAsync(PlayerId reporter, ReportRequest request, DateTimeOffset now, CancellationToken cancellationToken);
}
