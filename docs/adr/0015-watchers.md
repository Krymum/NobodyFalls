# 15. Watchers

- Status: accepted
- Date: 2026-11-14

## Context

With few players, many nights end with players nobody held, and a new game that
kills most of its first players dies with them. GAME_DESIGN §3 answers with
Watchers: non-player characters, **always labelled as such**, who hold players
nobody holds while the population is small, and fade out as it grows. Their hands
count for survival but earn no points.

Open points settled in the plan (Q3): when they act, on whom, how many, and what
their hands count for.

## Decision

- **Stored as players** of kind `watcher`, with reserved pseudos (`veilleur.NN`,
  refused at registration). Their hands are ordinary rows in `hands`
  (`giver_kind = watcher`), so midnight, the Tonight screen and real time need no
  special path. They never receive hands and never fall. The interface always
  labels them as Watchers.
- **Number** frozen per day when the day opens (`game_days.watcher_count`):
  `clamp(ceil(max × (1 − alive / disappearAt)), 0, max)`, defaults 10 and 200 —
  ten Watchers in an empty world, none from 200 living players. They are created
  on demand and reused in a stable order, so the same faces come back night after
  night.
- **One pass a day** at a configurable time (default 22:00 UTC, late enough for
  players to hold each other first). It gives one hand to each active player with
  **zero** hands, fragile newcomers first, then in a per-day stable shuffle; hands
  are spread across Watchers, three each. Immune players are skipped: they cannot
  fall tonight.
- The pass runs in the night worker, in one transaction under the shared night
  lock (never during midnight) and the day row lock (one pass even with several
  instances), and is idempotent through `game_days.watcher_pass_at`.
- **Scoring:** a Watcher's hand counts towards the receiver's threshold; it earns
  no Guardian points, creates no "saved by" link and no life debt, and does not
  count as a pair that held. Its decision lives in `WatcherPolicy` (domain) and is
  checked against the SQL like the rest of midnight (ADR 0003).
- The rules (max, population, time, hands) are part of the versioned game rules
  (ADR 0005), so an admin can tune or disable them without a deploy.

## Consequences

- A player with zero hands at 22:00 still gets one; whether it is enough depends
  on the threshold (1 while there are fewer than 50 active players).
- Players can tell Watchers from people at a glance; Watchers never inflate scores
  or friendships.
- Once the population passes the threshold, Watchers stop appearing without any
  change of code.
