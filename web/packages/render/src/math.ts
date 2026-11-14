/** A point or vector in canvas units: `[x, y]`. */
export type Vec2 = readonly [number, number];

export type Ctx2D = CanvasRenderingContext2D | OffscreenCanvasRenderingContext2D;

export const TAU = Math.PI * 2;

export function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

export function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}

export function easeOutCubic(t: number): number {
  return 1 - (1 - clamp(t, 0, 1)) ** 3;
}

/**
 * Lightens (k > 0) or darkens (k < 0) a `#RRGGBB` colour towards white or black.
 * The shading of every volume (lit face, shadow side) is derived this way.
 */
export function shade(hex: string, k: number): string {
  const n = parseInt(hex.slice(1, 7), 16);
  const target = k < 0 ? 0 : 255;
  const p = Math.abs(k);
  const channel = (c: number): number => Math.round(c + (target - c) * p);
  return `rgb(${channel((n >> 16) & 255)},${channel((n >> 8) & 255)},${channel(n & 255)})`;
}

/** Deterministic pseudo-random numbers (mulberry32): scenes look the same on every load. */
export function seededRandom(seed: number): () => number {
  let s = seed;
  return () => {
    s = (s + 0x6d2b79f5) | 0;
    let t = Math.imul(s ^ (s >>> 15), 1 | s);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/**
 * Two-bone inverse kinematics: the middle joint (elbow, knee) of a limb going from
 * `start` to `end`. `bend` (+1 or −1) chooses on which side the joint folds.
 */
export function solveTwoBone(start: Vec2, end: Vec2, upper: number, lower: number, bend: 1 | -1): Vec2 {
  const dx = end[0] - start[0];
  const dy = end[1] - start[1];
  const distance = Math.min(Math.hypot(dx, dy), upper + lower - 0.01);
  const angle = Math.atan2(dy, dx);
  const cos = (upper * upper + distance * distance - lower * lower) / (2 * upper * distance);
  const fold = Math.acos(clamp(cos, -1, 1));
  const direction = angle + bend * fold;
  return [start[0] + Math.cos(direction) * upper, start[1] + Math.sin(direction) * upper];
}
