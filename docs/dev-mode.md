# Development mode

A day of NobodyFalls lasts 24 hours and needs many players. Development mode lets
you play one in a minute, alone: a test sign-in, fake players, bots, and a clock
you can move forward.

It is on in `docker compose` (and with `dotnet run` in the `Development`
environment), controlled by `DevTools:Enabled` (`DEV_TOOLS` in `.env`). The API
**refuses to start** if it is enabled in `Production`.

## Signing in

The sign-in screen shows a **development login**: type any name and you are in,
with no Google or Discord account. Each name is a separate account, so you can
play several players in separate browser profiles or private windows.

Cloudflare Turnstile uses Cloudflare's public test keys by default: the bot check
always passes and never calls Cloudflare, so everything works offline.

## The DEV panel

Once signed in, the dashed **DEV** button (top right) opens the panel:

| Button | What it does |
| --- | --- |
| **Midnight − 10 s** | Moves the clock to 10 seconds before midnight UTC. The night worker resolves the night by itself a few seconds later, and every screen moves to the next night. |
| **+1 h** | Moves the clock one hour forward (to cross 21:00 or 23:00, when reminders and the Last stand bonus start). |
| **Real time** | Brings the clock back to the real time. See the note below. |
| **+20 players** | Creates 20 fake players (bots) with random characters and friendships, in play today. |
| **Bots play** | Each bot gives its hands, mostly to those who need them most, through the real rules (70 % of bots play). |
| **Run the night** | Runs the night cycle now: resolves every day whose midnight has passed. |

A typical session: sign up, **+20 players**, **Bots play**, give your own hands
from the Edge, **Midnight − 10 s**, and watch who falls.

## The simulated clock

The server's clock is the real time plus an **offset** kept in the database
(`dev_clock`), shared by the API, the workers and every client
([ADR 0006](adr/0006-simulated-clock.md)). Clients get the server time with each game
view, so countdowns follow the simulated clock.

Move the clock **forward only**. Going back (**Real time** after a few midnights)
would put the clock before days already resolved; the game will not undo them.
To start over, reset the world (below).

## The dev API

Everything the panel does is a plain HTTP call under `/dev`, handy in scripts and
tests. Like the game API, calls need the `X-NF-Request: 1` header.

```sh
H='X-NF-Request: 1'
curl -X POST localhost:8080/dev/reset -H "$H"                     # empty world, real time, a season opening today
curl -X POST localhost:8080/dev/seed  -H "$H" -H 'Content-Type: application/json' -d '{"players":200,"friendsPerPlayer":3}'
curl -X POST localhost:8080/dev/bots/play -H "$H" -H 'Content-Type: application/json' -d '{"ratio":0.7}'
curl -X POST localhost:8080/dev/clock -H "$H" -H 'Content-Type: application/json' -d '{"toMidnightMinusSeconds":10}'
curl -X POST localhost:8080/dev/clock -H "$H" -H 'Content-Type: application/json' -d '{"advanceMinutes":90}'
curl -X POST localhost:8080/dev/night/tick -H "$H"                # resolve every past midnight now
curl localhost:8080/dev/clock                                     # current simulated time and offset
```

`POST /dev/login-as/{playerId}` signs the current browser in as an existing
player (a seeded bot, for instance), to see the game from their side.

The Playwright tests start every test with `POST /dev/reset`, so they never depend
on each other.

## Other development pages

- `/dev/scene` — the reference scene ([`docs/reference/scene-demo.dc.html`](reference/scene-demo.dc.html))
  rebuilt with the real renderers, to compare them side by side.
- `/openapi/v1.json` — the API description (development only).

## Push notifications locally

Web Push works on `http://localhost` in Chromium and Firefox. In development the
server generates its VAPID keys once and keeps them in the database, so
subscriptions survive restarts. Production sets `Push__PublicKey` and
`Push__PrivateKey`.
