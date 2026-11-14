# 21. Session versions: ending sessions remotely

- Status: accepted (sanctions, step c, will rely on it)
- Date: 2026-11-14

## Context

Cookies are self-contained (ADR 0007): until phase 2, nothing could end a session
before it expired. A ban must sign a player out everywhere, at once; so must the
reset of a team member's authenticator.

## Decision

- Each player has a `session_version` (integer, 0 by default). It is written into
  the player cookie and the admin cookie when they are issued.
- On every authenticated request, the cookie's version is compared with the
  player's current one (15-second cache per instance, forgotten at once by the
  instance that bumps it). A stale cookie is refused and removed.
- Bumping the version ends every session of that player, on every device. Used by
  the authenticator reset now, and by bans in step (c).
- Cookies issued before this change carry no version and count as version 0: nobody
  was signed out by the deployment.

## Consequences

- One small, cached query per authenticated request.
- Another instance sees a bump within 15 seconds at most.
