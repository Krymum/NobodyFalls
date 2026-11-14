import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { breakpoints, panelWidth } from '@nobodyfalls/theme';
import { describe, expect, it } from 'vitest';
import { layoutOf, MEDIA } from './index.ts';

const web = fileURLToPath(new URL('../../../', import.meta.url));

function cssFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (name === 'node_modules' || name === 'dist') {
      return [];
    }
    return statSync(path).isDirectory() ? cssFiles(path) : name.endsWith('.css') ? [path] : [];
  });
}

describe('layout', () => {
  it.each([
    [320, 'mobile'],
    [390, 'mobile'],
    [639, 'mobile'],
    [640, 'tablet'],
    [820, 'tablet'],
    [1023, 'tablet'],
    [1024, 'desktop'],
    [1440, 'desktop'],
  ] as const)('%i px wide is %s', (width, kind) => {
    expect(layoutOf(width)).toBe(kind);
  });

  it('media queries follow the tokens', () => {
    expect(MEDIA.desktop).toBe(`(min-width: ${breakpoints.desktop}px)`);
    expect(MEDIA.mobile).toBe(`(max-width: ${breakpoints.tablet - 1}px)`);
  });

  it('the side panel is 360–420 px wide', () => {
    expect(panelWidth(1024)).toBe(360);
    expect(panelWidth(1300)).toBe(390);
    expect(panelWidth(2560)).toBe(420);
  });

  it('every media query in both apps uses the shared breakpoints only', () => {
    const allowed = new Set([
      breakpoints.tablet,
      breakpoints.tablet - 1,
      breakpoints.desktop,
      breakpoints.desktop - 1,
    ]);
    const files = [...cssFiles(join(web, 'apps')), ...cssFiles(join(web, 'packages'))];
    expect(files.length).toBeGreaterThan(3);
    for (const file of files) {
      const css = readFileSync(file, 'utf8');
      for (const media of css.matchAll(/@media[^{]*/g)) {
        for (const [, value] of media[0].matchAll(/(?:min|max)-width:\s*(\d+)px/g)) {
          expect(allowed.has(Number(value)), `${file}: ${value}px`).toBe(true);
        }
      }
    }
  });
});
