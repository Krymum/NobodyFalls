# 6. One clock for game logic, simulated in development

- Status: accepted
- Date: 2026-11-13

## Context

The whole game revolves around midnight UTC. Developers and end-to-end tests
must reach midnight in seconds and watch the night resolve, on a stack that may
run several processes.

## Decision

- Game logic reads time only from `IClock` (Application). The domain never reads
  time: it receives it. SQL never uses `now()` for rules; timestamps are passed
  as parameters.
- In production, `SystemClock` wraps `TimeProvider.System`.
- In development, `SimulatedClock` = real time + an offset stored in the
  single-row table `dev_clock`. Every instance refreshes the offset every second,
  so the API and the night worker agree. The dev tools (step c) move it forward,
  for instance to "midnight minus 10 seconds".
- The night worker ticks every 2 seconds and resolves every day whose midnight
  has passed, in order, so jumping several days ahead plays each night.

## Consequences

- Moving the clock backwards is possible but never replays a resolved night.
- Data created under a simulated clock carries simulated dates; a dev database
  is disposable.
