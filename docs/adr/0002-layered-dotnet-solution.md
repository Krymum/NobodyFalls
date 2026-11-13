# 2. Layered .NET solution with a pure domain

- Status: accepted
- Date: 2026-11-13

## Context

The rules of the game (thresholds, scoring, life debts, fragile players,
Watchers…) are the heart of the product and change through configuration.
They must be testable in isolation and readable by contributors who do not
know ASP.NET Core or EF Core.

## Decision

The back end is split into projects with a strict dependency direction:

```
Api ──► Application ──► Domain
 │            ▲
 └──► Infrastructure
```

- `NobodyFalls.Domain`: pure rules, no NuGet dependency, no I/O, no clock.
  Time is always passed in as a value.
- `NobodyFalls.Application`: use cases and ports (`IClock`, repositories,
  realtime publisher…). Depends only on Domain.
- `NobodyFalls.Infrastructure`: EF Core/PostgreSQL, Redis, Web Push, Turnstile.
- `NobodyFalls.Api`: Minimal APIs, authentication, SignalR hub, hosted services.
- `NobodyFalls.Admin.Api` will be added in phase 2 for the admin back office.

Build-wide settings live in `Directory.Build.props` (nullable reference types,
warnings as errors, .NET analyzers, code style enforced at build) and package
versions in `Directory.Packages.props` (central package management).

## Consequences

- Domain tests run in milliseconds and need no database.
- Dependency direction is checked by project references; Domain cannot
  reference infrastructure by accident.
- Some mapping code is needed between domain types and persistence models.
