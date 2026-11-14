# Security policy

NobodyFalls is a live multiplayer game: accounts, friendships, and a score that
players care about. Thank you for helping keep it safe.

## Reporting a vulnerability

**Please do not open a public issue.** Report it privately through GitHub:
[**Report a vulnerability**](https://github.com/Krymum/NobodyFalls/security/advisories/new)
(the _Security_ tab of the repository).

Please include what you found, how to reproduce it, and its impact as you see it.
We aim to answer within a week. Once a fix is released, we will credit you
in the advisory unless you prefer otherwise.

## Scope

In scope: this repository and the game at nobodyfalls.com — for example, giving
or receiving hands outside the rules, avoiding a fall, acting as another player,
reading another player's private data, bypassing rate limits or the bot checks,
or leaking secrets.

Out of scope: denial of service by volume, social engineering, reports from
automated scanners without a demonstrated impact, and the development tools
(`/dev`), which are refused at start-up in production.

## Supported versions

Only the latest version on `main`, which is what the game runs.
