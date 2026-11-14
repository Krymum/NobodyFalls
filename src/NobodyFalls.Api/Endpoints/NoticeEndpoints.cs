using System.Security.Claims;
using NobodyFalls.Api.Auth;
using NobodyFalls.Api.Http;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Moderation;
using NobodyFalls.Domain.Common;

namespace NobodyFalls.Api.Endpoints;

public sealed record PseudoRequest(string? Pseudo);

/// <summary>
/// What a sanctioned player must see (GAME_DESIGN §9): warnings to acknowledge, a ban with its
/// end and message, a pseudo to choose after a reset.
/// </summary>
internal static class NoticeEndpoints
{
    public static void MapNoticeEndpoints(this RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").RequireAuthorization(PlayerPrincipal.Policy);

        me.MapGet("/notices", async (ClaimsPrincipal user, IPlayerNotices notices, IClock clock, CancellationToken ct) =>
            Results.Ok(await notices.NoticesAsync(user.RequiredPlayerId(), clock.UtcNow, ct)));

        me.MapPost("/notices/warnings/{id:long}/ack", async (long id, ClaimsPrincipal user, IPlayerNotices notices, IClock clock, CancellationToken ct) =>
            Problems.Of(await notices.AcknowledgeAsync(user.RequiredPlayerId(), id, clock.UtcNow, ct)));

        me.MapPut("/pseudo", async (PseudoRequest body, ClaimsPrincipal user, IPlayerNotices notices, CancellationToken ct) =>
        {
            var pseudo = Pseudo.Create(body.Pseudo);
            return pseudo.IsFailure ? Problems.From(pseudo.Error!) : Problems.Of(await notices.ChoosePseudoAsync(user.RequiredPlayerId(), pseudo.Value, ct));
        });
    }
}

/// <summary>
/// A banned player keeps their account but leaves the game (decision Q2): only their session,
/// their notices, contesting the ban and the public world counters remain open to them.
/// </summary>
internal sealed class BannedPlayersFilter(IPlayerNotices notices, IClock clock) : IEndpointFilter
{
    public static readonly DomainError Banned = new("player.banned", "Your account is banned: you can contest it from the contact page.");

    private static readonly string[] Open = ["/api/v1/session", "/api/v1/me/notices", "/api/v1/tickets", "/api/v1/world"];

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (http.User.PlayerId() is { } player
            && !Open.Any(path => http.Request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase))
            && await notices.BannedUntilAsync(player, clock.UtcNow, http.RequestAborted) is not null)
        {
            return Problems.From(Banned);
        }

        return await next(context);
    }

    /// <summary>Whether a request of a banned player may proceed (tickets: appeals only).</summary>
    public static async Task<bool> IsBannedAsync(HttpContext http, IPlayerNotices notices, IClock clock) =>
        http.User.PlayerId() is { } player && await notices.BannedUntilAsync(player, clock.UtcNow, http.RequestAborted) is not null;
}
