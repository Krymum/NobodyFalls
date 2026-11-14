import { motion, rope as tokens } from '@nobodyfalls/theme';
import { clamp, easeOutCubic, lerp, type Ctx2D, type Vec2 } from './math.ts';

export interface RopeSpec {
  readonly from: Vec2;
  readonly to: Vec2;
  /** Thickness: nearer ropes are thicker. */
  readonly width: number;
  /**
   * 0 = slack (hangs in a curve), 1 = carrying weight (almost straight)
   * (ART_DIRECTION, "Cordes"). Ignored when `sag` is given.
   */
  readonly tension?: number;
  /** Explicit downward bulge at the middle, in canvas units. */
  readonly sag?: number;
  /** 0..1: how much of the rope is unrolled (a rope being thrown). */
  readonly progress?: number;
  readonly opacity?: number;
}

const SEGMENTS = 30;

/** A slack rope sags by a share of its length; a taut one barely does. */
export function sagOf(spec: RopeSpec): number {
  if (spec.sag !== undefined) {
    return spec.sag;
  }
  const length = Math.hypot(spec.to[0] - spec.from[0], spec.to[1] - spec.from[1]);
  return (1 - clamp(spec.tension ?? 1, 0, 1)) * length * 0.35;
}

/** Points along the rope (quadratic curve through the sagging middle), cut at `progress`. */
export function ropePoints(spec: RopeSpec): Vec2[] {
  const [ax, ay] = spec.from;
  const [bx, by] = spec.to;
  const mx = (ax + bx) / 2;
  const my = (ay + by) / 2 + sagOf(spec);
  const end = Math.max(1, Math.round(SEGMENTS * clamp(spec.progress ?? 1, 0, 1)));
  const points: Vec2[] = [];
  for (let i = 0; i <= end; i++) {
    const t = i / SEGMENTS;
    const u = 1 - t;
    points.push([u * u * ax + 2 * u * t * mx + t * t * bx, u * u * ay + 2 * u * t * my + t * t * by]);
  }
  return points;
}

/**
 * Giving a hand = throwing the coiled rope: it unrolls (~0.8 s), then tightens.
 * Returns the rope's progress and tension `elapsed` seconds after the throw.
 */
export function throwAt(
  elapsed: number,
  finalTension = 0.96,
  duration: number = motion.ropeThrowSeconds,
): {
  progress: number;
  tension: number;
} {
  const eased = easeOutCubic(elapsed / duration);
  return { progress: eased, tension: lerp(0.0, finalTension, eased) };
}

/** Real ropes: twisted fibres, shadow, highlight (ART_DIRECTION, "Cordes"). */
export class RopeRenderer {
  /** Draws a rope and returns where it currently ends. */
  rope(ctx: Ctx2D, spec: RopeSpec): Vec2 {
    const points = ropePoints(spec);
    const opacity = spec.opacity ?? 1;
    const w = spec.width;
    const path = (ox: number, oy: number): void => {
      ctx.beginPath();
      points.forEach(([x, y], i) => {
        if (i === 0) {
          ctx.moveTo(x + ox, y + oy);
        } else {
          ctx.lineTo(x + ox, y + oy);
        }
      });
    };

    ctx.save();
    ctx.globalAlpha = opacity;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    ctx.strokeStyle = tokens.shadow;
    ctx.lineWidth = w;
    path(0.5, 0.5);
    ctx.stroke();
    ctx.strokeStyle = tokens.body;
    ctx.lineWidth = w * 0.7;
    path(0, 0);
    ctx.stroke();
    ctx.globalAlpha = opacity * 0.6;
    ctx.strokeStyle = tokens.highlight;
    ctx.lineWidth = w * 0.22;
    path(-w * 0.18, -w * 0.12);
    ctx.stroke();

    // Twist: short diagonal strokes along the rope.
    ctx.globalAlpha = opacity * 0.55;
    ctx.strokeStyle = tokens.shadow;
    ctx.lineWidth = 0.6;
    for (let i = 1; i < points.length; i++) {
      const [x0, y0] = points[i - 1] ?? [0, 0];
      const [x1, y1] = points[i] ?? [0, 0];
      const dx = x1 - x0;
      const dy = y1 - y0;
      const length = Math.hypot(dx, dy) || 1;
      const nx = -dy / length;
      const ny = dx / length;
      const tx = dx / length;
      const ty = dy / length;
      const h = w * 0.42;
      const steps = Math.max(1, Math.round(length / (w * 0.9)));
      for (let k = 0; k < steps; k++) {
        const px = x0 + dx * (k / steps);
        const py = y0 + dy * (k / steps);
        ctx.beginPath();
        ctx.moveTo(px - nx * h - tx * h * 0.6, py - ny * h - ty * h * 0.6);
        ctx.lineTo(px + nx * h + tx * h * 0.6, py + ny * h + ty * h * 0.6);
        ctx.stroke();
      }
    }
    ctx.restore();
    return points[points.length - 1] ?? spec.to;
  }

  /** A frayed thread: danger, visible before reading any label. */
  thread(ctx: Ctx2D, from: Vec2, to: Vec2): void {
    const fx = from[0] + (to[0] - from[0]) * 0.35;
    const fy = from[1] + (to[1] - from[1]) * 0.35;
    ctx.save();
    ctx.strokeStyle = tokens.thread;
    ctx.lineWidth = 0.9;
    ctx.beginPath();
    ctx.moveTo(from[0], from[1]);
    ctx.lineTo(fx, fy - 6);
    ctx.stroke();
    ctx.lineWidth = 0.4;
    for (const [offset, jitter] of [
      [-1.6, 0],
      [0.4, 0.8],
      [1.8, -0.6],
    ] as const) {
      ctx.beginPath();
      ctx.moveTo(fx + offset * 0.3, fy - 6);
      ctx.quadraticCurveTo(fx + offset * 2.2, fy + jitter, fx, fy + 6);
      ctx.stroke();
    }
    ctx.lineWidth = 0.9;
    ctx.beginPath();
    ctx.moveTo(fx, fy + 6);
    ctx.lineTo(to[0], to[1]);
    ctx.stroke();
    ctx.restore();
  }

  /** A free hand: the rope coiled at the hip, its end swinging. */
  coil(ctx: Ctx2D, belt: Vec2, t: number, scale = 1): void {
    const [bx, by] = belt;
    ctx.save();
    ctx.lineCap = 'round';
    const loops = [
      [8, 12, 7, 9],
      [10, 16, 6.5, 8.5],
      [6.5, 14, 7.5, 9.5],
    ] as const;
    loops.forEach(([ox, oy, rx, ry], i) => {
      for (const [color, width] of [
        [tokens.shadow, 3],
        [tokens.body, 2],
      ] as const) {
        ctx.strokeStyle = color;
        ctx.lineWidth = width * scale;
        ctx.beginPath();
        ctx.ellipse(bx + ox * scale, by + oy * scale, rx * scale, ry * scale, 0.3 + i * 0.2, 0, Math.PI * 2);
        ctx.stroke();
      }
    });
    ctx.restore();
    this.rope(ctx, {
      from: [bx + 12 * scale, by + 22 * scale],
      to: [bx + (20 + Math.sin(t * 1.3) * 2) * scale, by + 52 * scale],
      sag: 2 * scale,
      width: 2.6 * scale,
    });
  }
}
