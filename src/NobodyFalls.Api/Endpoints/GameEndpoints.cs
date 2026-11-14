using System.Security.Claims;
using NobodyFalls.Api.Auth;
using NobodyFalls.Api.Hosting;
using NobodyFalls.Api.Http;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Accounts;
using NobodyFalls.Application.Hands;
using NobodyFalls.Application.Players;
using NobodyFalls.Application.Queries;
using NobodyFalls.Domain.Common;
using NobodyFalls.Domain.Players;

namespace NobodyFalls.Api.Endpoints;

internal static class GameEndpoints
{
    public static void MapGameEndpoints(this RouteGroupBuilder api)
    {
        // World counters are public: they are the game's shop window.
        api.MapGet("/world", async (IGameQueries queries, IClock clock, CancellationToken ct) =>
            Results.Ok(await queries.WorldAsync(clock.UtcNow, ct)));

        var game = api.MapGroup("").RequireAuthorization(PlayerPrincipal.Policy);

        game.MapGet("/tonight", async (ClaimsPrincipal user, IGameQueries queries, ComeBack comeBack, IClock clock, CancellationToken ct) =>
        {
            var player = user.RequiredPlayerId();
            var returned = await comeBack.ExecuteAsync(player, ct);
            var tonight = await queries.TonightAsync(player, clock.UtcNow, ct);
            return Results.Ok(tonight with { Returned = returned == ReturnOutcome.NotFallen ? null : returned });
        });

        game.MapPost("/hands", async (
            GiveHandRequest body,
            ClaimsPrincipal user,
            HttpContext context,
            IHumanVerifier verifier,
            GiveHand give,
            CancellationToken ct) =>
        {
            if (!await verifier.VerifyAsync(body.TurnstileToken, context.Connection.RemoteIpAddress?.ToString(), ct))
            {
                return Problems.From(AccountErrors.NotHuman);
            }

            var result = await give.ExecuteAsync(user.RequiredPlayerId(), new PlayerId(body.ReceiverId), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/hands/{result.Value.HandId}", result.Value)
                : Problems.From(result.Error!);
        }).RequireRateLimiting(RateLimits.Hands);

        game.MapGet("/edge", async (int? offset, int? limit, ClaimsPrincipal user, IGameQueries queries, IClock clock, CancellationToken ct) =>
            Results.Ok(await queries.EdgeAsync(
                user.RequiredPlayerId(),
                clock.UtcNow,
                Math.Max(0, offset ?? 0),
                Math.Clamp(limit ?? 30, 1, 100),
                ct)));

        game.MapGet("/players/{id:guid}", async (Guid id, ClaimsPrincipal user, IGameQueries queries, CancellationToken ct) =>
            await queries.PublicProfileAsync(user.RequiredPlayerId(), new PlayerId(id), ct) is { } profile
                ? Results.Ok(profile)
                : Results.NotFound());
    }
}
