# 8. Real time: one hub, batched events

- Status: accepted
- Date: 2026-11-14

## Context

Players should see their neighbourhood, their friends and the Edge move while it
happens, without hammering the server: at most one broadcast per second
(GAME_DESIGN §13). Mobile networks drop WebSockets; some proxies block them.
Later, the map (phase 3) will stream changes per visible tile.

## Decision

- **One SignalR hub**, `/hubs/game`, for players only (cookie session). All
  transports are enabled: WebSockets, then Server-Sent Events, then long polling.
- **Server → client: a single method, `batch`**, carrying
  `{ serverTime, events: [{ type, ... }] }`. Event types: `me.hands`,
  `me.handReceived`, `player.standing`, `edge.changed`, `friend.request`,
  `friend.accepted`, `world.stats`, `night.resolved`.
- **Client → server: subscriptions only** (`WatchEdge(bool)`; phase 3 adds
  `WatchTiles`). Game actions stay on the REST API, which carries Turnstile, rate
  limits and HTTP status codes.
- **Audiences** are a player (all their connections, via `Clients.User`), the
  friends of a player (resolved with one query per batch, so no group bookkeeping
  when friendships change), the Edge watchers (a group), or everyone.
- **Batching**: use cases publish events into an in-memory channel; every second
  the publisher groups them per audience and **coalesces** state events with the
  same key (a player's standing, my hands left): only the latest is sent. Events
  where each occurrence matters (a hand received, a friend request) are kept.
- **Scale-out**: with `ConnectionStrings:Redis`, the SignalR Redis backplane
  routes messages to connections held by other instances. Each instance batches
  the events it produced.
- World counters are refreshed by a query every 10 seconds rather than one event
  per hand.
- Real time is best effort: clients reload their state on reconnection and on
  `night.resolved`; nothing depends on receiving every message.

## Consequences

- A burst of hands on one player produces one update per second per viewer.
- Tested end to end with real SignalR clients over long polling: the giver,
  receiver, the receiver's friends and Edge watchers each get their events;
  anonymous connections are refused.
