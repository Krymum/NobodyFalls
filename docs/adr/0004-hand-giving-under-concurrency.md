# 4. Giving hands under concurrency

- Status: accepted
- Date: 2026-11-13

## Context

A player has three hands a day, can hold someone only once a day, and a hand
given is final. Players double-tap, open two tabs, or script requests; many
players can hold the same person at the same moment; and hands keep arriving
in the last second before midnight while the night is being resolved. None of
this may produce a fourth hand, a duplicate, a lost hand or a hand counted on
the wrong night.

## Decision

- **The database has the last word.** `hands` has two unique indexes:
  `(giver_id, day, slot)` with `slot` 1..n, and `(giver_id, receiver_id, day)`;
  plus `check (giver_id <> receiver_id)`.
- **One transaction per hand** (`PostgresHandLedger`): read the facts (hands
  used, block, friendship, receiver in play, day open), check them with the
  domain's `HandPolicy`, insert the hand with `slot = used + 1`, and increment
  `player_days.received` (a row update, so concurrent hands to one receiver
  queue instead of losing increments).
- **Races resolve through the constraints.** Two concurrent hands from the same
  giver compete for the same slot: the loser gets a unique violation and is
  retried with fresh facts, which then shows the next free slot or "no hand
  left". A duplicate receiver maps to `hand.duplicate`.
- **Midnight fence.** Giving a hand takes `pg_advisory_xact_lock_shared` on the
  night lock; resolving a night takes it exclusively. Resolution therefore waits
  for hands in flight, and a hand that arrives once the night is resolved sees
  the day closed and gets `hand.night_resolving` (the client retries on the new
  day). `received` is a cache; the resolution recounts from `hands`.

## Consequences

- Tested on a real PostgreSQL: ten simultaneous hands from one player yield
  exactly three; five identical hands yield one; 29 players holding one person
  at once are all counted; hands stamped in the last millisecond race the
  resolution and are always either scored or refused, never lost.
- Shared advisory locks do not block each other, so giving hands scales; only
  the resolution is exclusive, for the few seconds it runs.
