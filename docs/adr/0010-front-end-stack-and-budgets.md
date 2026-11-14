# 10. Front-end stack and performance budgets

- Status: accepted
- Date: 2026-11-14

## Context

GAME_DESIGN §13 sets hard limits: at most 150 KB of initial JavaScript and 30 KB
of CSS (gzip), Lighthouse mobile ≥ 90, animations in requestAnimationFrame and
stopped when the tab is hidden, no heavy component library, no CSS at run time.
§14 asks for logic outside components. The game is mobile-first and a PWA.

## Decision

- **React 19 + Vite**, TypeScript strict. No component library: a handful of
  plain components styled with **CSS Modules** and the generated design tokens.
- **wouter** (~2 KB) for routing, with the language as the first segment
  (`/fr/…`, `/en/…`); **zustand** (~1 KB) for client state.
- **Logic outside components**: `src/core` holds pure modules (neighbourhood
  layout, label placement, standing → posture, wardrobe, countdown) with unit
  tests, and the store whose actions call the API and apply real-time events.
  Screens read the store and render.
- **Lazy loading**: the SignalR client is imported after the first screen; the
  reference-scene page (`/dev/scene`) is a separate chunk.
- **PWA by hand**: a small service worker (hashed assets cache-first, pages
  network-first with the cached shell offline, push display and click handling)
  and a manifest; no build plugin.
- **Turnstile** is invisible and requested per protected action. With
  Cloudflare's public test keys (development), the client skips the widget and
  the server accepts without calling Cloudflare, so everything works offline.
- **Push state per device** is remembered in `localStorage`; the push manager is
  only queried when the player taps (reading it on display froze some engines).
- API types are written by hand in `src/api/types.ts`, next to the client; the API
  integration tests pin the server side. Generating them from OpenAPI stays possible.

## Consequences

- Initial JavaScript ≈ 114 KB gzip, CSS ≈ 3 KB at the end of phase 1; CI checks
  the budgets (step g).
- End-to-end tests (Playwright: Chromium, Firefox, WebKit, Pixel 7, iPhone 14)
  play the whole loop against the docker compose stack; each test starts from
  `POST /dev/reset` so that tests never depend on each other.
