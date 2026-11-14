using System.Security.Claims;
using NobodyFalls.Api.Auth;
using NobodyFalls.Api.Hosting;
using NobodyFalls.Api.Http;
using NobodyFalls.Application.Accounts;
using NobodyFalls.Application.Feedback;
using NobodyFalls.Application.Push;
using NobodyFalls.Domain.Feedback;

namespace NobodyFalls.Api.Endpoints;

public sealed record IdeaRequest(string? Title, string? Body, string? Category, string? TurnstileToken);

public sealed record TicketRequest(string? Category, string? Subject, string? Message);

public sealed record TicketReplyRequest(string? Message);

public sealed record ReportRequestBody(string? Target, Guid? PlayerId, string? Pseudo, long? IdeaId, string? Category, string? Details, string? TurnstileToken);

/// <summary>Ideas, requests to the team and reports, for players (GAME_DESIGN §8).</summary>
internal static class FeedbackEndpoints
{
    public static void MapFeedbackEndpoints(this RouteGroupBuilder api)
    {
        var feedback = api.MapGroup("").RequireAuthorization(PlayerPrincipal.Policy);

        feedback.MapGet("/ideas", async (
            string? status, string? sort, int? offset, int? limit,
            ClaimsPrincipal user, IIdeaStore ideas, INotificationDirectory directory, CancellationToken ct) =>
        {
            var player = user.RequiredPlayerId();
            var page = await ideas.ListAsync(
                player,
                await directory.LocaleOfAsync(player, ct),
                IdeaRules.ParseStatus(status),
                sort == "new" ? IdeaSort.New : IdeaSort.Votes,
                Math.Max(0, offset ?? 0),
                Math.Clamp(limit ?? 20, 1, 50),
                ct);
            return Results.Ok(page);
        });

        feedback.MapGet("/ideas/{id:long}", async (long id, ClaimsPrincipal user, IIdeaStore ideas, CancellationToken ct) =>
            await ideas.GetAsync(user.RequiredPlayerId(), id, ct) is { } idea ? Results.Ok(idea) : Problems.From(IdeaErrors.NotFound));

        feedback.MapPost("/ideas", async (
            IdeaRequest body, ClaimsPrincipal user, HttpContext context, IHumanVerifier verifier,
            INotificationDirectory directory, FeedbackActions actions, CancellationToken ct) =>
        {
            if (!await verifier.VerifyAsync(body.TurnstileToken, context.Connection.RemoteIpAddress?.ToString(), ct))
            {
                return Problems.From(AccountErrors.NotHuman);
            }

            var player = user.RequiredPlayerId();
            var result = await actions.ProposeIdeaAsync(player, body.Title, body.Body, body.Category, await directory.LocaleOfAsync(player, ct), ct);
            return result.IsSuccess ? Results.Created($"/api/v1/ideas/{result.Value}", new { id = result.Value }) : Problems.From(result.Error!);
        }).RequireRateLimiting(RateLimits.Feedback);

        feedback.MapPut("/ideas/{id:long}/vote", async (long id, ClaimsPrincipal user, FeedbackActions actions, CancellationToken ct) =>
            ToResult(await actions.VoteAsync(user.RequiredPlayerId(), id, vote: true, ct))).RequireRateLimiting(RateLimits.Feedback);

        feedback.MapDelete("/ideas/{id:long}/vote", async (long id, ClaimsPrincipal user, FeedbackActions actions, CancellationToken ct) =>
            ToResult(await actions.VoteAsync(user.RequiredPlayerId(), id, vote: false, ct))).RequireRateLimiting(RateLimits.Feedback);

        feedback.MapGet("/tickets", async (ClaimsPrincipal user, ITicketStore tickets, CancellationToken ct) =>
            Results.Ok(await tickets.MineAsync(user.RequiredPlayerId(), ct)));

        feedback.MapGet("/tickets/{id:long}", async (long id, ClaimsPrincipal user, ITicketStore tickets, CancellationToken ct) =>
            await tickets.MineAsync(user.RequiredPlayerId(), id, ct) is { } ticket ? Results.Ok(ticket) : Problems.From(TicketErrors.NotFound));

        feedback.MapPost("/tickets", async (
            TicketRequest body, ClaimsPrincipal user, INotificationDirectory directory, FeedbackActions actions, CancellationToken ct) =>
        {
            var player = user.RequiredPlayerId();
            var result = await actions.OpenTicketAsync(player, body.Category, body.Subject, body.Message, await directory.LocaleOfAsync(player, ct), ct);
            return result.IsSuccess ? Results.Created($"/api/v1/tickets/{result.Value}", new { id = result.Value }) : Problems.From(result.Error!);
        }).RequireRateLimiting(RateLimits.Feedback);

        feedback.MapPost("/tickets/{id:long}/messages", async (long id, TicketReplyRequest body, ClaimsPrincipal user, FeedbackActions actions, CancellationToken ct) =>
            Problems.Of(await actions.ReplyAsync(user.RequiredPlayerId(), id, body.Message, ct))).RequireRateLimiting(RateLimits.Feedback);

        feedback.MapPost("/reports", async (
            ReportRequestBody body, ClaimsPrincipal user, HttpContext context, IHumanVerifier verifier, FeedbackActions actions, CancellationToken ct) =>
        {
            if (!await verifier.VerifyAsync(body.TurnstileToken, context.Connection.RemoteIpAddress?.ToString(), ct))
            {
                return Problems.From(AccountErrors.NotHuman);
            }

            var result = await actions.ReportAsync(
                user.RequiredPlayerId(), body.Target, body.PlayerId, body.Pseudo, body.IdeaId, body.Category, body.Details, ct);
            // The reporter only learns that the report was received (never what happens to the target).
            return result.IsSuccess ? Results.Created($"/api/v1/reports/{result.Value}", new { id = result.Value }) : Problems.From(result.Error!);
        }).RequireRateLimiting(RateLimits.Feedback);
    }

    private static IResult ToResult(NobodyFalls.Domain.Common.Result<VoteResult> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : Problems.From(result.Error!);
}
