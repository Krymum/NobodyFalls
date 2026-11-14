# Contributing to NobodyFalls

Thank you for helping. A few rules keep the game fair and the code easy to change.

## Before you start

- **The rules live in [`docs/GAME_DESIGN.md`](docs/GAME_DESIGN.md).** If a change
  touches a rule and the document is silent or ambiguous, open an issue and ask
  first: we settle the rule, then write the code.
- **Structural decisions get an ADR** in [`docs/adr/`](docs/adr/) (a new library,
  a new storage, a change of approach). Copy the format of the existing records.
- For anything larger than a fix, open an issue to agree on the approach before
  writing the code.

## Setting up

See the [README](README.md#working-on-the-code): `docker compose up --build` runs
everything; `dotnet test` and `pnpm test` run the tests. You need Docker, the
.NET 10 SDK, and Node 24 with `corepack enable` (pnpm comes from `web/package.json`).

[`docs/dev-mode.md`](docs/dev-mode.md) explains how to play a whole day in a
minute: fake players, bots, and a clock you can move to midnight.

## Code

- **English** for code, comments, commits and documentation. Comments explain
  _why_, not what.
- **Back end:** the domain stays pure (no I/O, no framework); rules are tested
  there first. Midnight is resolved in SQL and checked against the domain
  reference by differential tests ([ADR 0003](docs/adr/0003-midnight-resolution-reference-and-sql.md)):
  a rule change touches both. Warnings are errors; `dotnet format` must pass.
- **Front end:** TypeScript strict, logic in `src/core` (pure, unit-tested),
  screens only read the store and render. No text in components: every string goes
  through i18n, in **both** French and English (a test checks parity).
- **Budgets** are limits, not wishes: initial JS ≤ 150 KB and CSS ≤ 30 KB gzip,
  Lighthouse mobile ≥ 90 ([GAME_DESIGN §13](docs/GAME_DESIGN.md)). CI checks them.
- **Dependencies:** exact versions, at least two weeks old; prefer the standard
  library and small packages.

## Tests

Every change comes with tests at the right level:

| Change | Tests |
| --- | --- |
| A rule | `tests/NobodyFalls.Domain.Tests` (and the differential tests for midnight) |
| SQL, concurrency, workers | `tests/NobodyFalls.Infrastructure.Tests` (Testcontainers) |
| An endpoint | `tests/NobodyFalls.Api.Tests`, including authorization |
| Front-end logic | Vitest next to the module |
| A player journey | Playwright in `web/e2e` |

## Commits and pull requests

- [Conventional commits](https://www.conventionalcommits.org/): `feat(night): …`,
  `fix(web): …`, `docs(adr): …`. Small, coherent commits; one logical change per
  pull request.
- Fill in the pull request template; CI must be green (build, format, lint,
  tests, budgets, end-to-end, secret scan).
- Never commit secrets: `.env` is ignored, `.env.example` holds safe local values,
  and gitleaks scans every push.

By contributing, you agree that your contributions are licensed under the
[MIT License](LICENSE) and that you follow the [code of conduct](CODE_OF_CONDUCT.md).
