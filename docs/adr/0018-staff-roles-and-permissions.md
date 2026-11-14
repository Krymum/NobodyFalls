# 18. Staff roles and the permission matrix

- Status: accepted
- Date: 2026-11-14

## Context

GAME_DESIGN §9: Admin, Moderator and Support roles, permissions checked on the
server, and every admin endpoint tested with each role and without one.

## Decision

- **Team members are players** (`staff_members`: player, roles `text[]`, granted by,
  granted at). They sign in with their game account, then with a TOTP code (ADR 0019).
  A member may hold several roles; permissions add up.
- **One matrix**, `PermissionMatrix` in the domain, is the single source of truth:
  the server enforces it, the admin interface hides what it forbids (from
  `GET /session`), and the authorization tests are generated from it. Admins hold
  every permission; moderators handle reports, sanctions (not permanent bans) and
  appeals; support answers tickets and exports data. Permissions have stable wire
  names (`players.view_links`).
- **Each endpoint declares its access** with endpoint metadata: `MemberOnly` (any
  role, before two-factor sign-in), `SignedInOnly` (role and admin session), or
  `Requires(permission, sensitive)`. "Sensitive" is per endpoint (reading the rules
  is not, changing them is) and asks for a fresh code (ADR 0019).
- **Roles are read on every request**, with a 15-second cache that the instance
  making a change forgets at once: taking a role away applies immediately.
- **Rules**: nobody changes their own roles or resets their own authenticator; the
  team always keeps at least one admin (team changes take a table lock, so two
  admins cannot remove each other at the same time); only human players can join.
- **The first admin** is made by an operator command,
  `dotnet NobodyFalls.Api.dll staff grant <pseudo>`, which works only while there is
  no admin; afterwards roles are given from the admin, audited. Dev tools can grant
  roles directly (audited as "dev tools").
- **Tests**: for every admin endpoint, no session → 401; a player without a role →
  403 `admin.forbidden`; a role without an admin session → 403 `admin.totp_required`
  (except member-only endpoints); each role → allowed or 403 as the matrix says;
  sensitive endpoints with a code older than five minutes → 401
  `admin.reauth_required`.

## Consequences

- Adding a permission is one enum value and one line in the matrix; the tests and
  the interface follow.
- A staff member's own game account is a player like any other; staff actions have
  no effect on their game.
