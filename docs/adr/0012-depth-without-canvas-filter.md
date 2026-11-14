# 12. Depth of field without `ctx.filter`

- Status: accepted
- Date: 2026-11-14

## Context

The reference scene blurs the far and middle planes with `ctx.filter = 'blur()'`
on every frame. `CanvasRenderingContext2D.filter` is not supported by Safari (iOS
included), and blurring a dozen figures 60 times a second costs too much on a
mid-range phone (60 fps target, 30 minimum, GAME_DESIGN §13).

## Decision

- Far and middle figures are drawn **once**, as silhouettes, into offscreen
  surfaces (`OffscreenCanvas`, or a detached canvas where it is missing), at their
  final scale and the device pixel ratio.
- They are blurred once with a software **box blur** (three passes ≈ gaussian,
  running sums so the cost does not depend on the radius), then drawn every frame
  with `drawImage`, rotated by their pendulum sway.
- Their thin ropes are drawn live (cheap lines), so they still follow the sway.
- Surfaces are rebuilt only when the canvas size or pixel ratio changes.
- Nothing in the renderers sets `ctx.filter`: the test context throws if anything
  does.

## Consequences

- Identical look on every browser, and a per-frame cost of a few `drawImage` calls.
- A blurred figure swings as a rigid block instead of moving its limbs, which is
  invisible at that size and blur.
