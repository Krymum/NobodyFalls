using System.Text.Json;
using NobodyFalls.Application.Audit;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Feedback;
using NobodyFalls.Domain.Moderation;

namespace NobodyFalls.Application.Moderation;

// ---- Players and sanctions -------------------------------------------------------------

public sealed record AdminPlayerRow(
    Guid Id,
    string Pseudo,
    string Kind,
    string Status,
    DateTimeOffset? BannedUntil,
    DateTimeOffset? ScoreExcludedUntil,
    int OpenReports,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt);

public sealed record SanctionView(
    long Id,
    string Kind,
    string Reason,
    string? PublicMessage,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    string? IssuedBy,
    long? ReportId,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? RevokedAt,
    string? RevokedBy,
    string? RevokeReason,
    bool Active);

public sealed record ReportSummary(
    long Id,
    string TargetType,
    string TargetId,
    Guid? TargetPlayerId,
    string TargetLabel,
    string Category,
    string Status,
    Guid ReporterId,
    string? ReporterPseudo,
    string? AssigneePseudo,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt);

public sealed record AdminPlayerSheet(
    Guid Id,
    string Pseudo,
    string Kind,
    string Status,
    string Locale,
    int GuardianScore,
    int FallsCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? BannedUntil,
    DateTimeOffset? ScoreExcludedUntil,
    bool PseudoResetPending,
    int Friends,
    int HandsGiven7,
    int HandsReceived7,
    IReadOnlyList<string> StaffRoles,
    IReadOnlyList<SanctionView> Sanctions,
    IReadOnlyList<ReportSummary> ReportsAbout,
    IReadOnlyList<ReportSummary> ReportsMade);

public sealed record AdminHand(long Id, string Day, Guid GiverId, string GiverPseudo, Guid ReceiverId, string ReceiverPseudo, DateTimeOffset GivenAt, int? Points);

/// <summary>Player search and sheet for the team (GAME_DESIGN §9).</summary>
public interface IAdminPlayers
{
    Task<IReadOnlyList<AdminPlayerRow>> SearchAsync(string? query, string? filter, int limit, CancellationToken cancellationToken);

    Task<AdminPlayerSheet?> SheetAsync(PlayerId player, DateTimeOffset now, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminHand>> HandsAsync(PlayerId player, int days, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>Issuing and revoking sanctions; each change is audited in its own transaction.</summary>
public interface ISanctionStore
{
    Task<Result<long>> IssueAsync(PlayerId target, SanctionDraft draft, long? reportId, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken);

    Task<Result> RevokeAsync(long sanctionId, string? reason, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The team member who issued a sanction (appeals go to someone else).</summary>
    Task<PlayerId?> IssuerAsync(long sanctionId, CancellationToken cancellationToken);
}

// ---- What a sanctioned player sees ---------------------------------------------------

public sealed record WarningNotice(long Id, string Message, DateTimeOffset At);

public sealed record BanNotice(long Id, string? Message, DateTimeOffset? Until);

public sealed record PlayerNotices(IReadOnlyList<WarningNotice> Warnings, BanNotice? Ban, bool PseudoResetPending);

/// <summary>Notices a player must see on their next visit, and the restrictions on their account.</summary>
public interface IPlayerNotices
{
    Task<PlayerNotices> NoticesAsync(PlayerId player, DateTimeOffset now, CancellationToken cancellationToken);

    Task<Result> AcknowledgeAsync(PlayerId player, long sanctionId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>After a pseudo reset, the player picks a new pseudo (once).</summary>
    Task<Result> ChoosePseudoAsync(PlayerId player, Pseudo pseudo, CancellationToken cancellationToken);

    /// <summary>Until when the player is banned, if they are (cached for a few seconds).</summary>
    Task<DateTimeOffset?> BannedUntilAsync(PlayerId player, DateTimeOffset now, CancellationToken cancellationToken);
}

// ---- Reports ---------------------------------------------------------------------------

public sealed record ReportDetail(
    ReportSummary Summary,
    string Details,
    JsonElement TargetSnapshot,
    string? ResolutionNote,
    long? SanctionId,
    int OtherReportsOnTarget);

public sealed record ReportPage(IReadOnlyList<ReportSummary> Items, long? NextBeforeId);

public interface IReportModeration
{
    Task<ReportPage> ListAsync(string? status, bool mine, PlayerId viewer, long? beforeId, int limit, CancellationToken cancellationToken);

    Task<ReportDetail?> GetAsync(long id, CancellationToken cancellationToken);

    Task<Result> AssignAsync(long id, PlayerId? assignee, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a report, with a sanction on the target player in the same transaction when
    /// one is given, and tells the reporter it was handled (never what was decided).
    /// </summary>
    Task<Result<long?>> ResolveAsync(long id, ReportOutcome outcome, string? note, SanctionDraft? sanction, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken);
}

// ---- Tickets ---------------------------------------------------------------------------

public sealed record AdminTicketSummary(
    long Id,
    Guid PlayerId,
    string Pseudo,
    string Category,
    string Subject,
    string Status,
    string? AssigneePseudo,
    long? SanctionId,
    DateTimeOffset UpdatedAt);

public sealed record AdminTicketMessage(long Id, string AuthorKind, string? AuthorPseudo, string Body, bool Internal, DateTimeOffset At);

public sealed record AdminTicket(AdminTicketSummary Summary, IReadOnlyList<AdminTicketMessage> Messages);

public interface ITicketModeration
{
    Task<IReadOnlyList<AdminTicketSummary>> ListAsync(IReadOnlyCollection<TicketCategory> categories, string? status, int limit, CancellationToken cancellationToken);

    Task<AdminTicket?> GetAsync(long id, CancellationToken cancellationToken);

    /// <summary>A public answer (the player is notified) or an internal note (the team only).</summary>
    Task<Result> ReplyAsync(long id, string body, bool isInternal, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken);

    Task<Result> SetStatusAsync(long id, TicketStatus status, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken);

    Task<Result> AssignAsync(long id, PlayerId? assignee, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken);
}

// ---- Ideas -----------------------------------------------------------------------------

public sealed record AdminIdea(
    long Id,
    string Title,
    string Body,
    string Category,
    string Status,
    string Locale,
    Guid AuthorId,
    string? AuthorPseudo,
    int Votes,
    string? StaffAnswer,
    bool Hidden,
    string? HiddenReason,
    long? MergedInto,
    DateTimeOffset CreatedAt);

public sealed record IdeaChanges(IdeaStatus? Status, string? StaffAnswer, bool? Hidden, string? HiddenReason);

public interface IIdeaModeration
{
    Task<IReadOnlyList<AdminIdea>> ListAsync(string? status, bool includeHidden, int limit, CancellationToken cancellationToken);

    Task<Result<AdminIdea>> UpdateAsync(long id, IdeaChanges changes, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken);

    Task<Result> MergeAsync(long duplicateId, long intoId, AuditActor actor, DateTimeOffset now, CancellationToken cancellationToken);
}
