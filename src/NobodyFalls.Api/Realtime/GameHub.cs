using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NobodyFalls.Api.Auth;

namespace NobodyFalls.Api.Realtime;

/// <summary>
/// The only hub (ADR 0008). Server → client: a single <c>batch</c> method, at most once a
/// second per channel. Client → server: subscriptions only; game actions go through the
/// REST API (Turnstile, rate limits, HTTP status codes).
/// </summary>
[Authorize(Policy = PlayerPrincipal.Policy)]
internal sealed class GameHub : Hub
{
    public const string Path = "/hubs/game";
    public const string BatchMethod = "batch";
    public const string EdgeGroup = "edge";

    /// <summary>Receive live updates of the Edge while the screen is open.</summary>
    public Task WatchEdge(bool watching) =>
        watching
            ? Groups.AddToGroupAsync(Context.ConnectionId, EdgeGroup)
            : Groups.RemoveFromGroupAsync(Context.ConnectionId, EdgeGroup);

    // Phase 3: WatchTiles(TileKey[]) joins one group per visible map tile.
}

/// <summary>SignalR users are players: <c>Clients.User(playerId)</c> reaches all their connections.</summary>
internal sealed class PlayerUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection.User.PlayerId()?.ToString();
}
