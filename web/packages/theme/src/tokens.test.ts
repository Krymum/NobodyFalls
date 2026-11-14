import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { toCssVariables } from './css.ts';
import { palette } from './tokens.ts';

/** WCAG relative luminance and contrast ratio. */
function luminance(hex: string): number {
  const channels = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255);
  const [r = 0, g = 0, b = 0] = channels.map((c) =>
    c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4,
  );
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

function contrast(a: string, b: string): number {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number];
  return (light + 0.05) / (dark + 0.05);
}

describe('tokens', () => {
  it('generated tokens.css is up to date', () => {
    const file = readFileSync(fileURLToPath(new URL('./tokens.css', import.meta.url)), 'utf8');
    expect(file).toBe(toCssVariables());
  });

  it.each([
    ['bone', palette.bone],
    ['secondary text', palette.textSecondary],
    ['labels', palette.label],
    ['attention', palette.attention],
    ['danger', palette.danger],
  ])('%s reaches AA contrast on the page', (_name, colour) => {
    expect(contrast(colour, palette.page)).toBeGreaterThanOrEqual(4.5);
  });

  it('the primary action is readable', () => {
    expect(contrast(palette.onAction, palette.action)).toBeGreaterThanOrEqual(4.5);
  });
});
