import { describe, expect, it } from 'vitest';
import en from './en.json';
import fr from './fr.json';

function keys(tree: unknown, prefix = ''): string[] {
  if (tree && typeof tree === 'object') {
    return Object.entries(tree as Record<string, unknown>).flatMap(([k, v]) =>
      keys(v, prefix ? `${prefix}.${k}` : k),
    );
  }
  return [prefix];
}

describe('notice screens translations (loaded with the screens)', () => {
  it('French and English have exactly the same keys', () => {
    expect(keys(en).sort()).toEqual(keys(fr).sort());
  });

  it('every error code these screens can meet has a text', () => {
    const all = new Set(keys(fr));
    for (const code of ['pseudo.no_reset', 'sanction.not_found']) {
      expect(all, code).toContain(`errors.${code}`);
    }
  });
});
