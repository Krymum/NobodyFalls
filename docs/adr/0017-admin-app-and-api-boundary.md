# 17. Admin application and API boundary

- Status: accepted
- Date: 2026-11-14

## Context

GAME_DESIGN §9 asks for an administration "under `/admin`, in a separate bundle that
does not weigh on the game", with server-side permissions, two-factor sign-in and a
full audit trail. §14 allows an `Admin.Api` project "if relevant". The game's bundle
must not grow (phase 2 plan, done criteria).

## Decision

- **One deployable, one origin.** The admin API is served by the same process,
  under `/api/v1/admin`, so cookies stay simple (no CORS, no third-party cookies)
  and the same middleware applies (rate limits, `X-NF-Request`, problem details).
- **Code boundary inside `NobodyFalls.Api`** (folder and namespace `Admin`), not a
  separate project. A separate `Admin.Api` project could not reach the API's
  internal plumbing (player claims, problem details, header filter) without moving
  it to yet another shared project; the value of the boundary is the access gate,
  not the assembly. The gate is enforced instead by:
  - one route group with `AdminAccessFilter`, which **fails closed**: an endpoint
    that declares no access is refused;
  - a test that lists every route mapped under `/api/v1/admin` and requires it to
    match the authorization table (ADR 0018), so a new endpoint cannot ship
    untested.
- **A separate front end**: `web/apps/admin`, its own Vite build into
  `wwwroot/admin/`, served by `/admin/{**path}` with `Cache-Control: no-cache`,
  `X-Robots-Tag: noindex`, `X-Frame-Options: DENY` and a strict Content Security
  Policy (scripts from this origin only). Static files are served **before**
  routing, so `/admin/assets/…` never falls into the page route.
- **The game never imports the admin**: an ESLint rule forbids it, the game's
  service worker ignores `/admin`, and the budget check fails if the game's initial
  JavaScript grows more than 1 KB over its recorded baseline (`web/budgets.json`).
  The admin has its own budget (200 KB JS, 30 KB CSS).
- **Same layout system** as the game (ADR 0016): shared tokens and `layout.css`,
  desktop first (sidebar from 1024 px), a drawer on tablets and phones; French and
  English with the same "no hard-coded text" rule and parity tests.

## Consequences

- Deploying the admin is deploying the game; there is no second service to secure.
- The admin's attack surface is the same origin as the game: everything rests on
  the gate (ADR 0018–0019) and on the `SameSite=Strict` admin cookie.
