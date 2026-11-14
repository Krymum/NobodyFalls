using System.Security.Claims;
using NobodyFalls.Api.Auth;
using NobodyFalls.Api.Hosting;
using NobodyFalls.Api.Http;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Friends;
using NobodyFalls.Domain.Common;

namespace NobodyFalls.Api.Endpoints;

internal static class FriendEndpoints
{
    private const int SuggestionCount = 20;

    public static void MapFriendEndpoints(this RouteGroupBuilder api)
    {
        var friends = api.MapGroup("/friends").RequireAuthorization(PlayerPrincipal.Policy);

        friends.MapGet("", async (ClaimsPrincipal user, IFriendStore store, IClock clock, CancellationToken ct) =>
            Results.Ok(new ListResponse<Application.Queries.FriendView>(
                await store.ListAsync(user.RequiredPlayerId(), GameDay.Of(clock.UtcNow), ct))));

        friends.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, IFriendStore store, CancellationToken ct) =>
        {
            await store.RemoveAsync(user.RequiredPlayerId(), new PlayerId(id), ct);
            return Results.NoContent();
        });

        friends.MapGet("/requests", async (ClaimsPrincipal user, IFriendStore store, CancellationToken ct) =>
            Results.Ok(await store.RequestsAsync(user.RequiredPlayerId(), ct)));

        friends.MapPost("/requests", async (FriendRequestBody body, ClaimsPrincipal user, IFriendStore store, IClock clock, CancellationToken ct) =>
        {
            var result = await store.RequestAsync(user.RequiredPlayerId(), body.Pseudo ?? "", clock.UtcNow, ct);
            return result.IsSuccess
                ? Results.Ok(new FriendRequestResponse(result.Value == FriendRequestOutcome.Sent ? "sent" : "becameFriends"))
                : Problems.From(result.Error!);
        }).RequireRateLimiting(RateLimits.Friends);

        friends.MapPost("/requests/{id:long}/accept", async (long id, ClaimsPrincipal user, IFriendStore store, IClock clock, CancellationToken ct) =>
            Problems.Of(await store.AcceptAsync(user.RequiredPlayerId(), id, clock.UtcNow, ct)));

        friends.MapPost("/requests/{id:long}/decline", async (long id, ClaimsPrincipal user, IFriendStore store, IClock clock, CancellationToken ct) =>
            Problems.Of(await store.DeclineAsync(user.RequiredPlayerId(), id, clock.UtcNow, ct)));

        friends.MapDelete("/requests/{id:long}", async (long id, ClaimsPrincipal user, IFriendStore store, IClock clock, CancellationToken ct) =>
            Problems.Of(await store.CancelAsync(user.RequiredPlayerId(), id, clock.UtcNow, ct)));

        friends.MapGet("/suggestions", async (ClaimsPrincipal user, IFriendStore store, IGameSettingsProvider settings, CancellationToken ct) =>
        {
            var rules = (await settings.GetCurrentAsync(ct)).Rules;
            return Results.Ok(new ListResponse<Application.Queries.SuggestionView>(
                await store.SuggestionsAsync(user.RequiredPlayerId(), rules.SuggestionMinNights, SuggestionCount, ct)));
        });

        var blocks = api.MapGroup("/blocks").RequireAuthorization(PlayerPrincipal.Policy);

        blocks.MapGet("", async (ClaimsPrincipal user, IFriendStore store, CancellationToken ct) =>
            Results.Ok(new ListResponse<Application.Queries.PlayerRef>(await store.BlockedAsync(user.RequiredPlayerId(), ct))));

        blocks.MapPut("/{id:guid}", async (Guid id, ClaimsPrincipal user, IFriendStore store, IClock clock, CancellationToken ct) =>
            Problems.Of(await store.BlockAsync(user.RequiredPlayerId(), new PlayerId(id), clock.UtcNow, ct)))
            .RequireRateLimiting(RateLimits.Friends);

        blocks.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, IFriendStore store, CancellationToken ct) =>
        {
            await store.UnblockAsync(user.RequiredPlayerId(), new PlayerId(id), ct);
            return Results.NoContent();
        });
    }
}
