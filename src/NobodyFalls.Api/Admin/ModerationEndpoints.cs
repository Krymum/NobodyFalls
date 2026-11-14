using NobodyFalls.Api.Http;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Audit;
using NobodyFalls.Application.Insights;
using NobodyFalls.Application.Moderation;
using NobodyFalls.Application.Staff;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Feedback;
using NobodyFalls.Domain.Moderation;
using NobodyFalls.Domain.Staff;

namespace NobodyFalls.Api.Admin;

public sealed record SanctionRequest(string? Kind, string? Reason, string? PublicMessage, DateTimeOffset? EndsAt);

public sealed record RevokeRequest(string? Reason);

public sealed record AssignRequest(Guid? AssigneeId, bool? ToMe);

public sealed record ResolveRequest(string? Outcome, string? Note, SanctionRequest? Sanction);

public sealed record StaffMessageRequest(string? Body, bool? Internal);

public sealed record TicketStatusRequest(string? Status);

public sealed record IdeaPatchRequest(string? Status, string? StaffAnswer, bool? Hidden, string? HiddenReason);

public sealed record MergeRequest(long? IntoId);

public sealed record DeleteAccountRequest(string? Reason);

/// <summary>
/// Moderation (GAME_DESIGN §9): players and sanctions, reports, requests, ideas. Every change
/// is audited by its store; sanctions check the permission of their kind (ADR 0018), and a
/// permanent ban asks for a fresh code (ADR 0019).
/// </summary>
internal static class ModerationEndpoints
{
    public static void MapModeration(RouteGroupBuilder admin)
    {
        MapPlayers(admin);
        MapReports(admin.MapGroup("/reports"));
        MapTickets(admin.MapGroup("/tickets"));
        MapIdeas(admin.MapGroup("/ideas"));
    }

    private static void MapPlayers(RouteGroupBuilder admin)
    {
        admin.MapGet("/players", async (string? q, string? filter, IAdminPlayers players, CancellationToken ct) =>
            Results.Ok(await players.SearchAsync(q, filter, 50, ct))).Requires(Permission.PlayersView);

        admin.MapGet("/players/{id:guid}", async (Guid id, IAdminPlayers players, IClock clock, CancellationToken ct) =>
            await players.SheetAsync(new PlayerId(id), clock.UtcNow, ct) is { } sheet
                ? Results.Ok(sheet)
                : Problems.From(SanctionErrors.PlayerNotFound)).Requires(Permission.PlayersView);

        admin.MapGet("/players/{id:guid}/hands", async (Guid id, int? days, IAdminPlayers players, IClock clock, CancellationToken ct) =>
            Results.Ok(await players.HandsAsync(new PlayerId(id), days ?? 7, clock.UtcNow, ct))).Requires(Permission.PlayersView);

        // Declared with the lowest sanction permission; the handler checks the kind's own.
        admin.MapPost("/players/{id:guid}/sanctions", async (Guid id, SanctionRequest body, HttpContext http, ISanctionStore sanctions, IClock clock, CancellationToken ct) =>
        {
            var draft = CheckSanction(http, body, clock.UtcNow);
            if (draft.IsFailure)
            {
                return Problems.From(draft.Error!);
            }

            var result = await sanctions.IssueAsync(new PlayerId(id), draft.Value, null, http.Actor(), clock.UtcNow, ct);
            return result.IsSuccess ? Results.Ok(new { id = result.Value }) : Problems.From(result.Error!);
        }).Requires(Permission.SanctionsWarn);

        admin.MapGet("/dashboard", async (IDashboardQueries dashboard, IClock clock, CancellationToken ct) =>
            Results.Ok(await dashboard.GetAsync(clock.UtcNow, ct))).Requires(Permission.DashboardView);

        // Linked accounts show plain IP addresses (decision Q5): each look is audited.
        admin.MapGet("/players/{id:guid}/links", async (Guid id, HttpContext http, IAccountLinks links, IAuditLog audit, CancellationToken ct) =>
        {
            var result = await links.LinksAsync(new PlayerId(id), ct);
            await audit.AppendAsync(new AuditRecord(http.Actor(), AuditActions.PlayerLinksViewed, AuditTargets.Player, id.ToString()), ct);
            return Results.Ok(result);
        }).Requires(Permission.PlayersViewLinks);

        admin.MapGet("/players/{id:guid}/export", async (Guid id, HttpContext http, IPersonalData data, IAuditLog audit, IClock clock, CancellationToken ct) =>
        {
            using var document = await data.ExportAsync(new PlayerId(id), clock.UtcNow, ct);
            if (document is null)
            {
                return Problems.From(PersonalDataErrors.NotFound);
            }

            await audit.AppendAsync(new AuditRecord(http.Actor(), AuditActions.PlayerExported, AuditTargets.Player, id.ToString()), ct);
            var bytes = System.Text.Encoding.UTF8.GetBytes(document.RootElement.GetRawText());
            return Results.File(bytes, "application/json", $"nobodyfalls-{id}.json");
        }).Requires(Permission.GdprExport);

        admin.MapDelete("/players/{id:guid}", async (Guid id, [Microsoft.AspNetCore.Mvc.FromBody] DeleteAccountRequest body, HttpContext http, IPersonalData data, IClock clock, CancellationToken ct) =>
            Problems.Of(await data.DeleteAsync(new PlayerId(id), body.Reason ?? "", http.Actor(), clock.UtcNow, ct)))
            .Requires(Permission.GdprDelete, sensitive: true);

        admin.MapPost("/sanctions/{id:long}/revoke", async (long id, RevokeRequest body, HttpContext http, ISanctionStore sanctions, IClock clock, CancellationToken ct) =>
            Problems.Of(await sanctions.RevokeAsync(id, body.Reason, http.Actor(), clock.UtcNow, ct))).Requires(Permission.SanctionsRevoke);
    }

    private static void MapReports(RouteGroupBuilder reports)
    {
        reports.MapGet("", async (string? status, bool? mine, long? before, HttpContext http, IReportModeration moderation, CancellationToken ct) =>
            Results.Ok(await moderation.ListAsync(status, mine == true, http.Member().PlayerId, before, 50, ct))).Requires(Permission.ReportsView);

        reports.MapGet("/{id:long}", async (long id, IReportModeration moderation, CancellationToken ct) =>
            await moderation.GetAsync(id, ct) is { } report ? Results.Ok(report) : Problems.From(ReportModerationErrors.NotFound)).Requires(Permission.ReportsView);

        reports.MapPost("/{id:long}/assign", async (long id, AssignRequest body, HttpContext http, IReportModeration moderation, IClock clock, CancellationToken ct) =>
            Problems.Of(await moderation.AssignAsync(id, AssigneeOf(http, body), http.Actor(), clock.UtcNow, ct))).Requires(Permission.ReportsHandle);

        reports.MapPost("/{id:long}/resolve", async (long id, ResolveRequest body, HttpContext http, IReportModeration moderation, IClock clock, CancellationToken ct) =>
        {
            if (ReportModeration.ParseOutcome(body.Outcome) is not { } outcome)
            {
                return Problems.From(ReportModerationErrors.Outcome);
            }

            SanctionDraft? sanction = null;
            if (body.Sanction is not null)
            {
                var draft = CheckSanction(http, body.Sanction, clock.UtcNow);
                if (draft.IsFailure)
                {
                    return Problems.From(draft.Error!);
                }

                sanction = draft.Value;
            }

            var result = await moderation.ResolveAsync(id, outcome, body.Note, sanction, http.Actor(), clock.UtcNow, ct);
            return result.IsSuccess ? Results.Ok(new { sanctionId = result.Value }) : Problems.From(result.Error!);
        }).Requires(Permission.ReportsHandle);
    }

    private static void MapTickets(RouteGroupBuilder tickets)
    {
        tickets.MapGet("", async (string? status, HttpContext http, ITicketModeration moderation, CancellationToken ct) =>
            Results.Ok(await moderation.ListAsync(CategoriesOf(http.Member()), status, 100, ct)))
            .RequiresAny(Permission.TicketsSupport, Permission.TicketsAppeal);

        tickets.MapGet("/{id:long}", async (long id, HttpContext http, ITicketModeration moderation, CancellationToken ct) =>
        {
            var ticket = await moderation.GetAsync(id, ct);
            return ticket is not null && MayHandle(http.Member(), ticket.Summary.Category)
                ? Results.Ok(ticket)
                : Problems.From(TicketErrors.NotFound);
        }).RequiresAny(Permission.TicketsSupport, Permission.TicketsAppeal);

        tickets.MapPost("/{id:long}/messages", async (long id, StaffMessageRequest body, HttpContext http, ITicketModeration moderation, ISanctionStore sanctions, IClock clock, CancellationToken ct) =>
        {
            var access = await CheckTicketAsync(http, id, moderation, sanctions, ct);
            if (access is not null)
            {
                return access;
            }

            var message = TicketRules.Message(body.Body);
            return message.IsFailure
                ? Problems.From(message.Error!)
                : Problems.Of(await moderation.ReplyAsync(id, message.Value, body.Internal == true, http.Actor(), clock.UtcNow, ct));
        }).RequiresAny(Permission.TicketsSupport, Permission.TicketsAppeal);

        tickets.MapPost("/{id:long}/status", async (long id, TicketStatusRequest body, HttpContext http, ITicketModeration moderation, ISanctionStore sanctions, IClock clock, CancellationToken ct) =>
        {
            var access = await CheckTicketAsync(http, id, moderation, sanctions, ct);
            if (access is not null)
            {
                return access;
            }

            return TicketRules.ParseStatus(body.Status) is { } status
                ? Problems.Of(await moderation.SetStatusAsync(id, status, http.Actor(), clock.UtcNow, ct))
                : Problems.From(new DomainError("ticket.status", "Unknown status."));
        }).RequiresAny(Permission.TicketsSupport, Permission.TicketsAppeal);

        tickets.MapPost("/{id:long}/assign", async (long id, AssignRequest body, HttpContext http, ITicketModeration moderation, ISanctionStore sanctions, IClock clock, CancellationToken ct) =>
        {
            var access = await CheckTicketAsync(http, id, moderation, sanctions, ct);
            return access ?? Problems.Of(await moderation.AssignAsync(id, AssigneeOf(http, body), http.Actor(), clock.UtcNow, ct));
        }).RequiresAny(Permission.TicketsSupport, Permission.TicketsAppeal);
    }

    private static void MapIdeas(RouteGroupBuilder ideas)
    {
        ideas.MapGet("", async (string? status, bool? hidden, IIdeaModeration moderation, CancellationToken ct) =>
            Results.Ok(await moderation.ListAsync(status, hidden == true, 200, ct))).Requires(Permission.IdeasModerate);

        ideas.MapPatch("/{id:long}", async (long id, IdeaPatchRequest body, HttpContext http, IIdeaModeration moderation, IClock clock, CancellationToken ct) =>
        {
            IdeaStatus? status = null;
            if (body.Status is not null)
            {
                status = IdeaRules.ParseStatus(body.Status);
                if (status is null)
                {
                    return Problems.From(new DomainError("idea.status", "Unknown status."));
                }
            }

            var result = await moderation.UpdateAsync(id, new IdeaChanges(status, body.StaffAnswer, body.Hidden, body.HiddenReason), http.Actor(), clock.UtcNow, ct);
            return result.IsSuccess ? Results.Ok(result.Value) : Problems.From(result.Error!);
        }).Requires(Permission.IdeasModerate);

        ideas.MapPost("/{id:long}/merge", async (long id, MergeRequest body, HttpContext http, IIdeaModeration moderation, IClock clock, CancellationToken ct) =>
            Problems.Of(await moderation.MergeAsync(id, body.IntoId ?? 0, http.Actor(), clock.UtcNow, ct))).Requires(Permission.IdeasModerate);
    }

    /// <summary>Checks a sanction: its fields, the permission of its kind, and a fresh code for a permanent ban.</summary>
    private static Result<SanctionDraft> CheckSanction(HttpContext http, SanctionRequest body, DateTimeOffset now)
    {
        var draft = SanctionRules.Draft(body.Kind, body.Reason, body.PublicMessage, body.EndsAt, now);
        if (draft.IsFailure)
        {
            return draft;
        }

        if (!PermissionMatrix.Allows(http.Member().Roles, SanctionRules.RequiredPermission(draft.Value)))
        {
            return AdminErrors.Forbidden;
        }

        return SanctionRules.IsSensitive(draft.Value) && !AdminAccessFilter.HasFreshCode(http) ? AdminErrors.ReauthRequired : draft;
    }

    private static PlayerId? AssigneeOf(HttpContext http, AssignRequest body) =>
        body.ToMe == true ? http.Member().PlayerId : body.AssigneeId is { } id ? new PlayerId(id) : null;

    private static TicketCategory[] CategoriesOf(StaffAccess member)
    {
        var categories = new List<TicketCategory>();
        if (PermissionMatrix.Allows(member.Roles, Permission.TicketsSupport))
        {
            categories.Add(TicketCategory.Support);
            categories.Add(TicketCategory.AccountDeletion);
        }

        if (PermissionMatrix.Allows(member.Roles, Permission.TicketsAppeal))
        {
            categories.Add(TicketCategory.Appeal);
        }

        return [.. categories];
    }

    private static bool MayHandle(StaffAccess member, string category) =>
        TicketRules.ParseCategory(category) is { } parsed && CategoriesOf(member).Contains(parsed);

    /// <summary>
    /// A member acts only on the categories their roles cover; an appeal is never handled by
    /// the person who issued the contested sanction (plan, decisions by default).
    /// </summary>
    private static async Task<IResult?> CheckTicketAsync(HttpContext http, long id, ITicketModeration moderation, ISanctionStore sanctions, CancellationToken ct)
    {
        var ticket = await moderation.GetAsync(id, ct);
        var member = http.Member();
        if (ticket is null || !MayHandle(member, ticket.Summary.Category))
        {
            return Problems.From(TicketErrors.NotFound);
        }

        if (ticket.Summary.SanctionId is { } sanction && await sanctions.IssuerAsync(sanction, ct) == member.PlayerId)
        {
            return Problems.From(new DomainError("ticket.own_sanction", "Another team member handles appeals against your own sanctions."));
        }

        return null;
    }
}
