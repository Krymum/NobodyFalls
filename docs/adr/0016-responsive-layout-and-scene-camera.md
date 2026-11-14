# 16. Responsive layout and scene camera

- Status: accepted
- Date: 2026-11-14

## Context

Phase 1 was built mobile-first and, on a computer, showed a phone-sized column in
the middle of a black screen. ART_DIRECTION, "Responsive", asks for three layouts
(< 640 px, 640–1023 px, ≥ 1024 px, on the real width), a scene that always fills
the window with a camera that frames it by ratio, a navigation rail and a side
panel on desktops, reading columns for lists, fluid type, visible hover and focus,
discreet keyboard shortcuts, and the same system for the admin to come. No rule
and no feature may change, and the performance budgets stay.

## Decision

- **Tokens** (`packages/theme`): breakpoints, layout sizes (rail 88 px, side panel
  `clamp(360px, 30vw, 420px)`, columns 640 and 760 px) and fluid type and spacing
  in `clamp()`. At 390 px wide the fluid sizes equal the phone sizes the screens were
  designed with, so phones look exactly as before.
- **`packages/layout`**, shared by the game and the admin: `layoutOf(width)`,
  `watchLayout` (media queries, framework-free; apps wrap it in
  `useSyncExternalStore`), keyboard helpers, and `layout.css` (focus rings, reading
  column, shortcut hints, reduced motion). Media queries cannot read custom
  properties: a test checks that every media query in both apps uses the shared
  breakpoints only.
- **Shell**: the scene is a fixed layer mounted once by the shell. In full on
  "Tonight"; dimmed behind list screens from tablets on; the void alone before
  sign-up; on phones only "Tonight" draws it, to save battery. The tab bar becomes
  a vertical rail on desktops, carrying the brand, the countdown and the shortcuts.
  "Tonight" moves its text from the bottom fade to a side panel on desktops. The
  Edge shows the list next to a large preview of the chosen player on desktops.
- **Camera** (`core/camera.ts`, pure and tested): the neighbourhood is still laid
  out in the 390 × 844 reference composition, so its rules and tests do not change.
  The camera projects it with **one uniform scale**, sized on the height, never
  wider than the free width, bounded to 0.7–1.45 × the reference, and centred in
  the part of the window the interface leaves free (minus the rail and the side
  panel on desktops). On phones and tablets it frames the whole composition, whose
  bottom leaves room for the text; on desktops it frames the people only, so they
  grow with the height. At 390 × 844 it is the identity.
- **Crowd** (`SceneRenderer`): figures are sized on the height and the reference
  frame is repeated across the width (mirrored and shifted), so landscape windows
  show more of the crowd, never bigger people; dust follows the area; the vignette
  follows the diagonal. At 390 × 844 the scene is pixel for pixel the reference.
- **Resizing**: the backing store follows the CSS size × the pixel ratio (capped
  at 2) at once; the blurred crowd is rebuilt once the size settles (150 ms), and
  the pending rebuild is cancelled when the scene unmounts.
- **Keyboard (desktop)**: 1–4 sections, W the world, ↑ ↓ and Enter on the Edge,
  ? the list of shortcuts, Escape closes panels; ignored while typing or with a
  modifier. Hints (`kbd`) show only on desktops with a pointer, and are hidden
  from accessible names.
- **Tests**: Playwright projects at 390 × 844, 820 × 1180 and 1440 × 900 check the
  structure (full-window scene, rail or tab bar, side panel, column widths, no
  horizontal scroll, 44 px targets, shortcuts, visible focus) and compare a
  reference screenshot of every main screen. Screenshots are made and compared
  inside the official Playwright Linux image (`pnpm e2e:screenshots`), locally and
  in CI, so fonts render identically; random texts (invite codes) are replaced and
  the countdown masked; a 2 % tolerance absorbs the characters' id-based sway phase.

## Consequences

- Initial JavaScript of the game: 111.6 → 114.1 KB gzip (budget 150), CSS 2.7 →
  4.0 KB (budget 30); Lighthouse mobile unchanged (98 / 100).
- The admin (phase 2) starts from the same tokens and `layout.css`.
- Changing the look of a screen means updating its references:
  `pnpm e2e:screenshots --update`, then reviewing the images in the pull request.
