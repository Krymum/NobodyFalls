using System.Security.Claims;
using NobodyFalls.Api.Auth;
using NobodyFalls.Api.Http;
using NobodyFalls.Application.Players;
using NobodyFalls.Application.Queries;
using NobodyFalls.Domain.Skins;

namespace NobodyFalls.Api.Endpoints;

internal static class MeEndpoints
{
    public static void MapMeEndpoints(this RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").RequireAuthorization(PlayerPrincipal.Policy);

        me.MapGet("/profile", async (ClaimsPrincipal user, IGameQueries queries, CancellationToken ct) =>
            await queries.MyProfileAsync(user.RequiredPlayerId(), ct) is { } profile ? Results.Ok(profile) : Results.NotFound());

        me.MapGet("/settings/choices", () => Results.Ok(SettingsChoices.Current));

        me.MapPatch("/settings", async (SettingsRequest body, ClaimsPrincipal user, UpdateSettings update, CancellationToken ct) =>
            Problems.Of(await update.ExecuteAsync(user.RequiredPlayerId(), body.Locale, body.MapNameVisibility, ct)));

        me.MapPut("/appearance", async (Appearance body, ClaimsPrincipal user, ChangeAppearance change, CancellationToken ct) =>
            Problems.Of(await change.ExecuteAsync(user.RequiredPlayerId(), body, ct)));
    }
}
