# 21. Sanctions and session versions

- Status: accepted
- Date: 2026-11-14

## Context

GAME_DESIGN §9 lists the sanctions: a warning, a pseudo reset, an exclusion from the
score, and a temporary or permanent ban, each with a reason. The decisions of
14/11/2026 set their effects. A player under a score exclusion keeps playing and stays
counted, but their score does not count. A ban takes the player out of the game
without counting as a death, and the hands they already gave stay valid. Cookies are
self-contained (ADR 0007), so until phase 2 nothing could end a session early. A ban
must sign a player out everywhere, and so must resetting a team member's
authenticator.

## Decision

### Session versions

- Each player has a `session_version`, 0 by default. It is written into the player
  cookie and the admin cookie when they are issued. On every authenticated request,
  the request is refused and the cookie removed if the version is out of date. The
  check uses a cache of 15 seconds per instance, which the instance making the change
  clears at once.
- Bumping the version ends every session of the player, on every device. A ban and
  an authenticator reset bump it. Cookies issued before this change carry no
  version and count as 0.

### Sanctions

- **Stored in `sanctions`.** Each sanction has its kind, the reason (for the team),
  the message to the player, start, end, issuer, the report it came from,
  acknowledgement and revocation. The effects are copied onto the player row:
  `banned_until`, `score_excluded_until` and `pseudo_reset_pending`. The SQL that runs
  each request and each midnight then never joins the history. A far-future instant
  stands for "no end".
- **Rules in the domain** (`SanctionRules`). Each kind needs its own permission: a
  moderator can ban for a while, and only an admin can ban for good. A permanent ban
  asks for a fresh code. A warning and a ban need a message for the player. An end
  date is in the future, at most a year away. Nobody sanctions themselves, and a team
  member must lose their role first.
- **Issued in one transaction**, under the shared night lock, together with its audit
  entry. Resolving a report with a sanction issues it in the same transaction.
  - **A ban** removes the player's row for today in `player_days` and bumps their
    session version. The night then neither saves them nor drops them. Hands they
    received count for nothing. Hands they gave still count for their receivers.
    While the ban lasts, the player is not put in play at the start of a day and is
    not counted among the living. Once it ends or is revoked, they come back at the
    next day start with the normal threshold.
  - **A score exclusion**: the player's hands earn no points, and their score is
    hidden from others. Both the domain reference and the midnight SQL apply this,
    and the differential tests now draw bans and exclusions.
  - **A pseudo reset** gives the player a neutral pseudo (`joueur_NNNNNN`). They choose
    a new one on their next visit, once.
- **Revoking** a sanction recomputes the player's restrictions from the sanctions
  still active.
- **What the player sees**: `GET /me/notices` returns the warnings to acknowledge, the
  current ban (message and end) and whether a new pseudo is due. The game shows these
  screens before anything else, in a separate chunk.
- **What a banned player can still use**: their session, their notices, appeal
  requests and the public world counters. Everything else answers 403
  `player.banned`.
- **Appeals** are handled by someone other than the issuer of the contested sanction;
  the server checks this.

## Consequences

- One cached lookup per game request, and a sanction takes effect within 15 seconds on
  other instances.
- A revoked ban does not put the player back in play in the middle of a day: the next
  day start does.
- The audit entries keep the full trail: the sanction, its reason, the report it came
  from, the old pseudo after a reset, and the revocation.
