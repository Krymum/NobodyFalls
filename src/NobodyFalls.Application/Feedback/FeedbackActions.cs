using NobodyFalls.Application.Abstractions;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Feedback;

namespace NobodyFalls.Application.Feedback;

/// <summary>
/// What players send to the team (GAME_DESIGN §8): ideas and votes, requests, reports.
/// Texts are cleaned and checked by the domain rules before reaching a store.
/// </summary>
public sealed class FeedbackActions(IIdeaStore ideas, ITicketStore tickets, IReportStore reports, IClock clock)
{
    public async Task<Result<long>> ProposeIdeaAsync(PlayerId author, string? title, string? body, string? category, string locale, CancellationToken cancellationToken)
    {
        var draft = IdeaRules.Draft(title, body, category);
        return draft.IsFailure ? draft.Error! : await ideas.ProposeAsync(author, draft.Value, locale, clock.UtcNow, cancellationToken);
    }

    public Task<Result<VoteResult>> VoteAsync(PlayerId voter, long ideaId, bool vote, CancellationToken cancellationToken) =>
        ideas.VoteAsync(voter, ideaId, vote, cancellationToken);

    public async Task<Result<long>> OpenTicketAsync(PlayerId player, string? category, string? subject, string? message, string locale, CancellationToken cancellationToken)
    {
        var draft = TicketRules.Draft(category, subject, message);
        return draft.IsFailure ? draft.Error! : await tickets.OpenAsync(player, draft.Value, locale, clock.UtcNow, cancellationToken);
    }

    public async Task<Result> ReplyAsync(PlayerId player, long ticketId, string? message, CancellationToken cancellationToken)
    {
        var body = TicketRules.Message(message);
        return body.IsFailure
            ? Result.Failure(body.Error!)
            : await tickets.ReplyAsync(player, ticketId, body.Value, clock.UtcNow, cancellationToken);
    }

    public async Task<Result<long>> ReportAsync(
        PlayerId reporter,
        string? target,
        Guid? playerId,
        string? pseudo,
        long? ideaId,
        string? category,
        string? details,
        CancellationToken cancellationToken)
    {
        if (ReportRules.ParseTarget(target ?? "player") is not { } kind)
        {
            return ReportErrors.TargetNotFound;
        }

        var draft = ReportRules.Draft(category, details);
        if (draft.IsFailure)
        {
            return draft.Error!;
        }

        return await reports.ReportAsync(reporter, new ReportRequest(kind, playerId, pseudo, ideaId, draft.Value), clock.UtcNow, cancellationToken);
    }
}
