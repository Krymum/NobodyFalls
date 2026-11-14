# Architecture

How the pieces of NobodyFalls fit together, for someone about to change them.
The _why_ of each choice is in the [decision records](adr/); the rules are in
[GAME_DESIGN.md](GAME_DESIGN.md).

## The shape

```
 Browser (React PWA, Canvas 2D)
   │  HTTPS: /api/v1 (JSON), /auth (cookies)     WebSocket: /hubs/game (SignalR)
   ▼
 ┌──────────────────────── NobodyFalls.Api (one process, one image) ────────────────────────┐
 │ Minimal API endpoints · sign-in · rate limits · SignalR hub · serves the built front end  │
 │ Background workers: NightWorker (midnight) · NotificationWorker (push) · stats broadcast  │
 └───────────────┬───────────────────────────────────────────────┬───────────────────────────┘
                 ▼                                               ▼
           PostgreSQL 17                                  Redis (SignalR backplane,
   game state, rules, outbox, keys                        several API instances)
```

One deployable: the API serves the front end from the same origin, so cookies stay
simple and there is no CORS. Several instances can run side by side: midnight and
the outbox are protected by PostgreSQL locks, and SignalR messages go through
Redis.

## Back end: four layers

[ADR 0002](adr/0002-layered-dotnet-solution.md). Dependencies point inwards.

| Project | Holds | Depends on |
| --- | --- | --- |
| `NobodyFalls.Domain` | The rules: thresholds, fragile newcomers, storms, scoring, falls and returns, the night resolver. Pure C#, no I/O. | nothing |
| `NobodyFalls.Application` | Use cases (`GiveHand`, `NightCycle`, `RegisterPlayer`, friends, settings) and the ports they need (`IClock`, stores, `IRealtimePublisher`, `INotificationScheduler`). | Domain |
| `NobodyFalls.Infrastructure` | PostgreSQL (raw SQL with Npgsql; EF Core only for migrations), the set-based midnight, Web Push, workers, clocks. | Application |
| `NobodyFalls.Api` | HTTP endpoints, authentication, SignalR, hosting. | all |

### Giving a hand

`POST /api/v1/hands` → `GiveHand` checks the rules in the domain, then
`PostgresHandLedger` inserts the hand. The database has the last word: unique
indexes make "three hands a day" and "never twice to the same player" impossible
to break, even with parallel requests; a lost race on a slot retries the next one
([ADR 0004](adr/0004-hand-giving-under-concurrency.md)). Hands take a shared
advisory lock, midnight an exclusive one, so no hand lands in a night being
resolved. The rules (threshold, hands per day…) are versioned in the database and
frozen for each day ([ADR 0005](adr/0005-game-rules-in-the-database.md)).

### Midnight

`NightWorker` wakes every 2 seconds; when a day's midnight has passed,
`NightCycle` resolves it. The resolution is **set-based SQL in one transaction**
under an exclusive lock: count hands, apply thresholds and immunity, mark falls,
award Guardian points, record pairs and counters, open the next day. It is
idempotent (`resolved_at`) and catches up on several missed days in order.

The domain holds a straightforward reference (`NightResolver`); differential
tests run both on random days and require identical outcomes
([ADR 0003](adr/0003-midnight-resolution-reference-and-sql.md)). A rule change
touches both.

Watchers — accounts run by the game — give a hand at 22:00 to players who still
have none. Their hands count for survival but earn no points.

### Time

Everything asks `IClock`, never `DateTime.UtcNow`. In development the clock is the
real time plus an offset stored in the database, shared by every process
([ADR 0006](adr/0006-simulated-clock.md), [dev mode](dev-mode.md)).

### Sign-in

Google and Discord (OAuth, enabled when configured) and a development login.
A short-lived external cookie carries the identity until the account exists, then
the session cookie (`__Host-` prefixed, HttpOnly, SameSite). Mutating requests
need the `X-NF-Request` header, which a cross-site form cannot send
([ADR 0007](adr/0007-authentication-and-session.md)). Cloudflare Turnstile, invisible,
guards registration and every hand given.

## Real time

One SignalR hub, `/hubs/game`. Use cases publish small events (a hand received,
an Edge change, the night resolved) to an audience: a player, their friends, or
everyone. `BatchingRealtimePublisher` coalesces them and sends **one `batch`
message per second per channel**, so a burst at 23:59 stays cheap
([ADR 0008](adr/0008-realtime-batches.md)). Clients apply events to their store
and refetch a view when an event says it changed.

## Push notifications

Use cases write notifications into an **outbox table** in the same transaction as
the change. `NotificationWorker` leases rows (`FOR UPDATE SKIP LOCKED`), renders
the text in the player's language (`.resx`, French and English) and sends Web Push
with VAPID; failed devices are retried, gone ones removed
([ADR 0009](adr/0009-notification-outbox-and-web-push.md)). Scheduled kinds: a
reminder at 21:00 UTC when a friend is below the threshold, the storm announcement
at 18:00 UTC, the night's outcome after midnight.

## Front end

`web/` is a pnpm workspace ([ADR 0014](adr/0014-web-workspace.md)):

- `packages/theme` — design tokens from the art direction, generated as CSS
  variables.
- `packages/render` — Canvas 2D: a character rig drawn from part sets, ropes, and
  the scene with its depth layers. No `ctx.filter` (Safari): distant silhouettes
  are blurred once into offscreen canvases ([ADR 0011](adr/0011-character-rig-and-part-sets.md),
  [ADR 0012](adr/0012-depth-without-canvas-filter.md)).
- `apps/game` — React 19, wouter, zustand. Pure logic in `src/core` (layout of the
  neighbourhood, label placement, standing, countdowns) with unit tests; the store
  calls the API and applies real-time events; screens read the store and render
  ([ADR 0010](adr/0010-front-end-stack-and-budgets.md)).

French and English everywhere, with `/fr` and `/en` URLs; the API returns stable
error codes that the interface translates ([ADR 0013](adr/0013-internationalisation.md)).
The app is an installable PWA with a hand-written service worker.

**Budgets** (GAME_DESIGN §13): initial JS ≤ 150 KB and CSS ≤ 30 KB gzip — about
112 KB and 3 KB today; SignalR loads after the first screen. The build writes
brotli and gzip copies of every text file, served by `Accept-Encoding`.
Lighthouse mobile ≥ 90 in performance and accessibility. CI enforces all three.

## Tests

| Level | Where | What |
| --- | --- | --- |
| Domain | `tests/NobodyFalls.Domain.Tests` | every rule, in memory |
| Integration | `tests/NobodyFalls.Infrastructure.Tests` | real PostgreSQL (Testcontainers, one migrated template copied per test): midnight vs. the reference, concurrent hands, outbox |
| API | `tests/NobodyFalls.Api.Tests` | the real app over HTTP and SignalR: authorization of every endpoint, CSRF header, errors |
| Front end | Vitest next to the modules | layout, catalogues parity, store |
| End to end | `web/e2e` (Playwright) | the whole loop in Chromium, Firefox, WebKit, Pixel 7 and iPhone 14, against `docker compose` |

## Extension points for later phases

Phase 1 leaves hooks rather than half-built features:

- **Administration (phase 2):** the rules, storms and the wardrobe catalogue are
  rows in the database, versioned and frozen per day, so an admin screen only has
  to write them; storms can only be scheduled before their day starts
  (`StormPolicy`).
- **Map (phase 3):** players have nullable map and tile coordinates and a
  name-visibility setting; the tab bar keeps a place for the map tab.
- **Wardrobe (phase 3):** `skin_items` already carries rarity and unlock
  conditions; the character rig draws any part set.
- **End of season and leaderboard (phase 3):** seasons, scores and per-season
  counters are recorded from day one.

See the [phase 1 plan](plan/PHASE_1_PLAN.md).
