using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using NobodyFalls.Api.Auth;
using NobodyFalls.Api.Hosting;
using NobodyFalls.Api.Http;
using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Accounts;
using NobodyFalls.Application.Players;
using NobodyFalls.Application.Queries;
using NobodyFalls.Domain.Players;
using NobodyFalls.Infrastructure.Accounts;

namespace NobodyFalls.Api.Endpoints;

internal static class SessionEndpoints
{
    public static void MapSessionEndpoints(this RouteGroupBuilder api)
    {
        // Called when the app opens: who am I, which sign-in methods exist. A fallen
        // player's visit brings them back automatically (GAME_DESIGN §2).
        api.MapGet("/session", async (
            HttpContext context,
            IGameQueries queries,
            ComeBack comeBack,
            IAccountStore accounts,
            IClock clock,
            SignInProviders providers,
            IOptions<TurnstileOptions> turnstile,
            DevToolsOptions devTools,
            CancellationToken ct) =>
        {
            SessionPlayer? player = null;
            ReturnOutcome? returned = null;
            var state = SessionState.Anonymous;

            if (context.User.PlayerId() is { } id)
            {
                var outcome = await comeBack.ExecuteAsync(id, ct);
                returned = outcome == ReturnOutcome.NotFallen ? null : outcome;
                await accounts.TouchAsync(id, clock.UtcNow, ct);
                player = await queries.SessionAsync(id, ct);
                state = player is null ? SessionState.Anonymous : SessionState.Player;
            }

            if (state == SessionState.Anonymous)
            {
                var external = await context.AuthenticateAsync(PlayerPrincipal.ExternalScheme);
                if (external.Succeeded && external.Principal.External() is not null)
                {
                    state = SessionState.PendingRegistration;
                }
            }

            return Results.Ok(new SessionResponse(state, player, returned, providers.Names, turnstile.Value.SiteKey, devTools.Enabled));
        });

        api.MapPost("/registration", async (
            RegistrationRequest body,
            HttpContext context,
            RegisterPlayer register,
            CancellationToken ct) =>
        {
            var external = await context.AuthenticateAsync(PlayerPrincipal.ExternalScheme);
            if (!external.Succeeded || external.Principal.External() is not { } identity)
            {
                return Problems.Code(StatusCodes.Status401Unauthorized, "account.not_signed_in", "Sign in with a provider first.");
            }

            var result = await register.ExecuteAsync(
                identity,
                body.Pseudo,
                body.Appearance,
                body.Locale,
                body.InviteCode,
                body.TurnstileToken,
                context.Connection.RemoteIpAddress?.ToString(),
                ct);
            if (result.IsFailure)
            {
                return Problems.From(result.Error!);
            }

            await context.SignInPlayerAsync(result.Value);
            await context.SignOutAsync(PlayerPrincipal.ExternalScheme);
            return Results.Created("/api/v1/me/profile", new IdResponse(result.Value.Value));
        }).RequireRateLimiting(RateLimits.Registration);

        api.MapGet("/invites/{code}", async (string code, IGameQueries queries, CancellationToken ct) =>
            await queries.InviteAsync(code, ct) is { } invite ? Results.Ok(invite) : Results.NotFound());

        api.MapGet("/skins/catalog", async (HttpContext context, IGameQueries queries, CancellationToken ct) =>
            Results.Ok(await queries.CatalogAsync(context.User.PlayerId(), ct)));
    }
}
