# 19. TOTP, admin session and re-authentication

- Status: accepted
- Date: 2026-11-14

## Context

GAME_DESIGN §9: two-factor authentication (TOTP) is mandatory for any account with a
role, and sensitive actions ask for re-authentication. Team members sign in with
Google or Discord like players (ADR 0007).

## Decision

- **TOTP, owned code.** RFC 6238 (HMAC-SHA1, 6 digits, 30-second steps, one step
  of drift), about forty lines in the domain, checked against the RFC's test
  vectors; no dependency. Codes follow **real time** (`TimeProvider`), never the
  simulated game clock (ADR 0006). A code works once: the last used step is stored
  and only newer steps are accepted, atomically.
- **Enrolment**: the first visit of a member without an authenticator shows a QR code
  (`otpauth://`, drawn as SVG squares, no HTML injection) and the base32 key; the
  member confirms with a first code and receives **ten recovery codes**, shown once,
  stored salted and hashed. The secret is encrypted at rest with the shared Data
  Protection keys. Until enrolment, no admin endpoint beyond the session ones answers.
- **The admin session is a second cookie**, `nf.admin` (`__Host-` prefixed,
  HttpOnly, **SameSite=Strict**), issued only after a valid code, on top of the
  player cookie. It expires after **30 minutes idle** (sliding) and **8 hours at
  most** (checked against the sign-in time it carries). It carries the time of the
  last confirmed code: **sensitive actions require a code less than five minutes
  old**, else the API answers 401 `admin.reauth_required` and the interface asks for
  a code, then replays the action once.
- **Brute force**: five wrong codes lock the authenticator for fifteen minutes (each
  failure and the lock are audited), on top of a per-player rate limit on the
  sign-in endpoints. Recovery codes are not accepted for re-authentication.
- **Reset**: an admin can reset another member's authenticator (sensitive, audited);
  it ends all of that member's sessions (ADR 0021) and they enrol again.
- **Dev tools**: `GET /dev/totp/{playerId}` returns the code an authenticator would
  show, skipping an already used step, for end-to-end tests. Disabled in production
  with the rest of the dev tools.

## Consequences

- Losing both the phone and the recovery codes requires another admin.
- The admin session is independent from the game session: signing out of the admin
  keeps the player signed in to the game.
