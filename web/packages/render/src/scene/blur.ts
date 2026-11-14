/**
 * Box blur on RGBA pixels, three passes ≈ a gaussian. Used once, when far figures
 * are pre-rendered: `ctx.filter` is not available everywhere (Safari) and blurring
 * every frame would be too costly anyway (ADR 0012).
 */
export function boxBlur(
  pixels: Uint8ClampedArray,
  width: number,
  height: number,
  radius: number,
  passes = 3,
): void {
  if (radius < 1) {
    return;
  }
  const r = Math.round(radius);
  const buffer = new Uint8ClampedArray(pixels.length);
  for (let pass = 0; pass < passes; pass++) {
    blurLine(pixels, buffer, width, height, r, true);
    blurLine(buffer, pixels, width, height, r, false);
  }
}

/** One direction of the blur, with a running sum (O(n) whatever the radius). */
function blurLine(
  source: Uint8ClampedArray,
  target: Uint8ClampedArray,
  width: number,
  height: number,
  r: number,
  horizontal: boolean,
): void {
  const lines = horizontal ? height : width;
  const length = horizontal ? width : height;
  const window = r * 2 + 1;
  const index = (line: number, i: number): number => {
    const clamped = Math.min(length - 1, Math.max(0, i));
    return (horizontal ? line * width + clamped : clamped * width + line) * 4;
  };

  for (let line = 0; line < lines; line++) {
    const sums = [0, 0, 0, 0];
    for (let i = -r; i <= r; i++) {
      const p = index(line, i);
      for (let c = 0; c < 4; c++) {
        sums[c] = (sums[c] ?? 0) + (source[p + c] ?? 0);
      }
    }
    for (let i = 0; i < length; i++) {
      const out = index(line, i);
      for (let c = 0; c < 4; c++) {
        target[out + c] = (sums[c] ?? 0) / window;
      }
      const add = index(line, i + r + 1);
      const remove = index(line, i - r);
      for (let c = 0; c < 4; c++) {
        sums[c] = (sums[c] ?? 0) + (source[add + c] ?? 0) - (source[remove + c] ?? 0);
      }
    }
  }
}
