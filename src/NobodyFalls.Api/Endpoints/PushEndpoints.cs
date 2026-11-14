using System.Security.Claims;
using NobodyFalls.Api.Auth;
using NobodyFalls.Api.Http;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Push;

namespace NobodyFalls.Api.Endpoints;

public sealed record PushKeys(string P256dh, string Auth);

public sealed record SubscribeRequest(string? Endpoint, PushKeys? Keys);

public sealed record VapidKeyResponse(string PublicKey);

internal static class PushEndpoints
{
    public static void MapPushEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/push/vapid-key", async (IPushKeys keys, CancellationToken ct) =>
            Results.Ok(new VapidKeyResponse(await keys.PublicKeyAsync(ct))));

        var push = api.MapGroup("").RequireAuthorization(PlayerPrincipal.Policy);

        push.MapPost("/push/subscriptions", async (SubscribeRequest body, ClaimsPrincipal user, HttpContext context, IPushStore store, IClock clock, CancellationToken ct) =>
        {
            // Browsers only hand out HTTPS endpoints of their push service.
            if (!Uri.TryCreate(body.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps
                || body.Endpoint!.Length > 1000 || string.IsNullOrEmpty(body.Keys?.P256dh) || string.IsNullOrEmpty(body.Keys.Auth))
            {
                return Problems.Code(StatusCodes.Status422UnprocessableEntity, "push.invalid_subscription", "This push subscription is not valid.");
            }

            await store.SubscribeAsync(
                user.RequiredPlayerId(),
                new PushSubscription(body.Endpoint, body.Keys.P256dh, body.Keys.Auth),
                context.Request.Headers.UserAgent.ToString(),
                clock.UtcNow,
                ct);
            return Results.NoContent();
        });

        push.MapDelete("/push/subscriptions", async (string endpoint, ClaimsPrincipal user, IPushStore store, CancellationToken ct) =>
        {
            await store.UnsubscribeAsync(user.RequiredPlayerId(), endpoint, ct);
            return Results.NoContent();
        });

        push.MapGet("/me/notifications", async (ClaimsPrincipal user, IPushStore store, CancellationToken ct) =>
            Results.Ok(await store.PreferencesAsync(user.RequiredPlayerId(), ct)));

        push.MapPut("/me/notifications", async (NotificationPreferences body, ClaimsPrincipal user, IPushStore store, CancellationToken ct) =>
        {
            await store.SavePreferencesAsync(user.RequiredPlayerId(), body, ct);
            return Results.NoContent();
        });
    }
}
