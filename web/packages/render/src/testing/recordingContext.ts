import type { Ctx2D } from '../math.ts';

export interface Call {
  readonly name: string;
  readonly args: readonly unknown[];
}

/**
 * A 2D context that records what is drawn, for tests without a browser. Setting
 * `filter` throws: renderers must not depend on it (Safari, ADR 0012).
 */
export function recordingContext(width = 390, height = 844): { ctx: Ctx2D; calls: Call[]; sets: Call[] } {
  const calls: Call[] = [];
  const sets: Call[] = [];
  const gradient = { addColorStop: (): void => undefined };
  const state: Record<string, unknown> = {
    canvas: { width, height },
    globalAlpha: 1,
  };

  const target = {
    createLinearGradient: (...args: unknown[]) => (
      calls.push({ name: 'createLinearGradient', args }),
      gradient
    ),
    createRadialGradient: (...args: unknown[]) => (
      calls.push({ name: 'createRadialGradient', args }),
      gradient
    ),
    getImageData: (_x: number, _y: number, w: number, h: number) => ({
      data: new Uint8ClampedArray(w * h * 4),
      width: w,
      height: h,
    }),
  } as Record<string | symbol, unknown>;

  const proxy = new Proxy(target, {
    get(object, property) {
      if (property in object) {
        return object[property];
      }
      if (typeof property === 'string' && property in state) {
        return state[property];
      }
      return (...args: unknown[]) => {
        calls.push({ name: String(property), args });
      };
    },
    set(_object, property, value) {
      if (property === 'filter') {
        throw new Error('ctx.filter is not available everywhere (Safari): do not use it.');
      }
      state[String(property)] = value;
      sets.push({ name: String(property), args: [value] });
      return true;
    },
  });

  return { ctx: proxy as unknown as Ctx2D, calls, sets };
}
