# 7. Authentication, session and request forgery

- Status: accepted
- Date: 2026-11-14

## Context

Players sign in with Google or Discord (GAME_DESIGN §12); there is no password.
Someone who signs in for the first time has no character yet and must go through
the onboarding. The session is a cookie (HttpOnly, Secure, SameSite). The API is
served from the same origin as the front end. Developers and end-to-end tests
need to sign in without a real provider account.

## Decision

- **Two cookies.**
  - `nf.external` (30 minutes) records who the provider says the person is
    (provider + provider key). It is set by Google, Discord, or the dev login.
  - `nf` (30 days, sliding) is the player session; it only carries the player id.
- **Flow.** `/auth/login/{provider}` → provider → `/auth/complete`: if the
  identity is linked to a player, the session opens; otherwise the person is sent
  to `/{lang}/onboarding`, and `POST /api/v1/registration` (Turnstile verified)
  creates the character, links the identity and opens the session.
- **Providers are optional.** Google and Discord are registered only when their
  keys are configured; the front end gets the list from `/api/v1/session`.
- **Dev login** (`/auth/login/dev?name=…`) exists only with `DevTools:Enabled`,
  which the application refuses to start with in Production.
- **Cookies** are `__Host-` prefixed, `Secure`, `HttpOnly`, `SameSite=Lax`.
  Local HTTP development turns `Auth:SecureCookies` off (plain names, not Secure).
  Data Protection keys live in PostgreSQL so sessions survive restarts and work
  across instances.
- **Request forgery.** `SameSite=Lax` already keeps the cookie off cross-site
  POSTs; in addition every state-changing API call must send `X-NF-Request: 1`.
  A cross-site page cannot add a custom header without a CORS preflight, which
  the API never grants. This is simpler than synchronizer tokens and needs no
  token round trip in the SPA.
- **Return URLs** are restricted to local paths (no open redirect).
- **The API never redirects to a login page**: it answers 401 or 403.

## Consequences

- Every player endpoint is covered by an authorization test (anonymous → 401,
  pending registration → 401).
- Linking a second provider to an existing account (Google + Discord) is a
  follow-up: the `external_logins` table already allows several per player.
