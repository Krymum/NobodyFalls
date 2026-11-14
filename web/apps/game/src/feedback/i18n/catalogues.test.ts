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

describe('"Ideas and contact" translations (loaded with the screen)', () => {
  it('French and English have exactly the same keys', () => {
    expect(keys(en).sort()).toEqual(keys(fr).sort());
  });

  it('every error code of the feedback API has a text', () => {
    const codes = [
      'idea.title',
      'idea.body',
      'idea.category',
      'idea.limit_reached',
      'idea.not_found',
      'idea.own',
      'idea.closed',
      'ticket.subject',
      'ticket.message',
      'ticket.category',
      'ticket.limit_reached',
      'ticket.not_found',
      'ticket.closed',
      'report.category',
      'report.details',
      'report.self',
      'report.target_not_found',
      'report.already_open',
      'report.limit_reached',
    ];
    const all = new Set(keys(fr));
    for (const code of codes) {
      expect(all, code).toContain(`errors.${code}`);
    }
  });
});
