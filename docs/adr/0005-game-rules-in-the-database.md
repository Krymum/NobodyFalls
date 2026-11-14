# 5. Game rules in the database, frozen per day

- Status: accepted
- Date: 2026-11-13

## Context

Thresholds, Watchers, storms, points and limits must be editable without
redeploying (GAME_DESIGN §3), later from the admin screen. Changing a rule in
the middle of a day would be unfair: a player who had enough hands at 20:00
could fall at midnight because the threshold changed at 21:00.

## Decision

- All tunable numbers form one immutable document, `GameRules` (domain),
  stored as JSON in `game_settings`, one row per **version**. Saving creates a
  new version after `GameRules.Validate()`; old versions are never modified
  (they also serve as history for the phase 2 audit log).
- When a day starts, `game_days.settings_version` records the current version.
  That day's thresholds are written into `player_days.required` and the night is
  resolved with that version. A change applies from the next day.
- A storm belongs to its day from the start and must be scheduled before the
  day starts; its push announcement still goes out 6 h before midnight. (Amended
  2026-11-14: an earlier draft allowed same-day storms, which changed thresholds
  under players' feet.)
- Each instance caches versions forever (they are immutable) and re-reads
  "which version is current" every 5 seconds. No message bus is needed for
  configuration.
- Missing JSON fields fall back to their defaults, so adding a rule never breaks
  stored versions.

## Consequences

- A rule change is visible to every instance within seconds and never affects a
  day already under way.
- Resolving an old day (catch-up after downtime) uses that day's rules.
