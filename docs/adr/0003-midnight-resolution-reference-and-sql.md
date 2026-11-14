# 3. Midnight resolution: domain reference implementation and set-based SQL

- Status: accepted
- Date: 2026-11-13

## Context

At midnight UTC the server decides who falls, awards Guardian points, records
life debts and updates the world streak and death counters. The job must be
idempotent, protected by a PostgreSQL advisory lock and written as set-based
SQL to scale to many players (`GAME_DESIGN.md` §2). Set-based SQL is hard to
read and easy to get subtly wrong; the rules themselves must stay obvious.

## Decision

- `NightResolver` in the Domain is the **reference implementation**: plain C#
  over an in-memory `DaySnapshot`, exhaustively unit-tested and property-tested.
- The Infrastructure layer implements the same resolution as set-based SQL in a
  single transaction (advisory lock, `resolved_at` guard for idempotency).
- **Differential tests** (Testcontainers, real PostgreSQL) generate random
  days, run both implementations and require identical outcomes.

## Consequences

- Rules are written twice, but the SQL version is proven equivalent on every
  CI run; a rule change starts in the domain and the differential tests show
  where the SQL must follow.
- The domain implementation can also be used by the dev tools to preview a
  night without touching the database.
