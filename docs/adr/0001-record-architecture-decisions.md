# 1. Record architecture decisions

- Status: accepted
- Date: 2026-11-13

## Context

NobodyFalls is developed in the open. Contributors need to understand why the
code is shaped the way it is, not only how.

## Decision

Every structural decision is recorded as an Architecture Decision Record in
`docs/adr/`, numbered sequentially, using the format of this file
(Context, Decision, Consequences). ADRs are written in English, like the code.
An ADR is never rewritten once accepted: a later ADR supersedes it.

## Consequences

- Reviewers can ask for an ADR when a pull request introduces a structural choice.
- Rules of the game stay in `docs/GAME_DESIGN.md`; ADRs explain technical choices.
