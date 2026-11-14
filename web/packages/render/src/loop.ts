import { motion } from '@nobodyfalls/theme';
import type { Ctx2D } from './math.ts';

export interface Frame {
  readonly ctx: Ctx2D;
  /** Seconds since the loop started (0 forever with reduced motion). */
  readonly t: number;
  /** Size in CSS pixels. */
  readonly width: number;
  readonly height: number;
  readonly pixelRatio: number;
}

export interface LoopOptions {
  /** Draw one still frame and never animate (`prefers-reduced-motion`). */
  readonly reducedMotion: boolean;
  /** Called when the canvas size or pixel ratio changes (pre-rendering lives here). */
  readonly onResize?: (width: number, height: number, pixelRatio: number) => void;
}

/**
 * Drives a canvas with requestAnimationFrame (GAME_DESIGN §13): pixel ratio capped
 * at 2, paused while the tab is hidden, a single still frame when motion is reduced.
 * Returns a function that stops everything.
 */
export function startRenderLoop(
  canvas: HTMLCanvasElement,
  draw: (frame: Frame) => void,
  options: LoopOptions,
): () => void {
  const ctx = canvas.getContext('2d');
  if (!ctx) {
    return () => undefined;
  }

  let width = 0;
  let height = 0;
  let pixelRatio = 1;
  let handle = 0;
  let start: number | null = null;

  const render = (now: number): void => {
    start ??= now;
    const t = options.reducedMotion ? 0 : (now - start) / 1000;
    ctx.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);
    draw({ ctx, t, width, height, pixelRatio });
  };

  const tick = (now: number): void => {
    render(now);
    if (!options.reducedMotion && !document.hidden) {
      handle = requestAnimationFrame(tick);
    }
  };

  const resize = (): void => {
    const rect = canvas.getBoundingClientRect();
    pixelRatio = Math.min(window.devicePixelRatio || 1, motion.maxDevicePixelRatio);
    width = rect.width;
    height = rect.height;
    canvas.width = Math.round(width * pixelRatio);
    canvas.height = Math.round(height * pixelRatio);
    options.onResize?.(width, height, pixelRatio);
    render(performance.now());
  };

  const onVisibility = (): void => {
    cancelAnimationFrame(handle);
    if (!document.hidden && !options.reducedMotion) {
      handle = requestAnimationFrame(tick);
    }
  };

  const observer = new ResizeObserver(resize);
  observer.observe(canvas);
  document.addEventListener('visibilitychange', onVisibility);
  resize();
  if (!options.reducedMotion) {
    handle = requestAnimationFrame(tick);
  }

  return () => {
    cancelAnimationFrame(handle);
    observer.disconnect();
    document.removeEventListener('visibilitychange', onVisibility);
  };
}

/** The player asked for less motion: draw still frames, change states without animation. */
export function prefersReducedMotion(): boolean {
  return typeof matchMedia === 'function' && matchMedia('(prefers-reduced-motion: reduce)').matches;
}
