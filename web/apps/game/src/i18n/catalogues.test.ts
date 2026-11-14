import { describe, expect, it } from 'vitest';
import en from './en.json';
import fr from './fr.json';

type Tree = Record<string, unknown>;

function keys(tree: unknown, prefix = ''): string[] {
  if (Array.isArray(tree)) {
    return tree.flatMap((item, i) => keys(item, `${prefix}[${i}]`));
  }
  if (tree && typeof tree === 'object') {
    return Object.entries(tree as Tree).flatMap(([k, v]) => keys(v, prefix ? `${prefix}.${k}` : k));
  }
  return [prefix];
}

function placeholders(tree: unknown): Map<string, string> {
  const result = new Map<string, string>();
  const walk = (node: unknown, prefix: string): void => {
    if (typeof node === 'string') {
      result.set(
        prefix,
        [...node.matchAll(/{{(\w+)}}/g)]
          .map((m) => m[1])
          .sort()
          .join(','),
      );
    } else if (node && typeof node === 'object') {
      for (const [k, v] of Object.entries(node as Tree)) {
        walk(v, `${prefix}.${k}`);
      }
    }
  };
  walk(tree, '');
  return result;
}

describe('translations', () => {
  it('French and English have exactly the same keys', () => {
    expect(keys(en).sort()).toEqual(keys(fr).sort());
  });

  it('every text uses the same placeholders in both languages', () => {
    const frenchPlaceholders = placeholders(fr);
    for (const [key, names] of placeholders(en)) {
      expect({ key, names: frenchPlaceholders.get(key) }).toEqual({ key, names });
    }
  });

  it('every error code the API can return has a text', () => {
    const codes = [
      'hand.season_closed',
      'hand.night_resolving',
      'hand.receiver_not_found',
      'hand.self',
      'hand.receiver_is_watcher',
      'hand.receiver_not_in_play',
      'hand.none_left',
      'hand.duplicate',
      'friend.self',
      'friend.not_found',
      'friend.already_friends',
      'friend.already_requested',
      'friend.limit_reached',
      'friend.target_limit_reached',
      'friend_request.not_found',
      'pseudo.length',
      'pseudo.characters',
      'pseudo.reserved',
      'pseudo.taken',
      'appearance.skin_tone',
      'appearance.missing_slot',
      'appearance.unknown_item',
      'appearance.wrong_slot',
      'appearance.not_owned',
      'appearance.color',
      'account.already_registered',
      'account.locale',
      'account.verification_failed',
      'account.not_signed_in',
      'settings.map_name_visibility',
      'push.invalid_subscription',
      'request.rate_limited',
      'request.missing_header',
      'network.offline',
      'auth.required',
    ];
    const all = new Set(keys(fr));
    for (const code of codes) {
      expect(all, code).toContain(`errors.${code}`);
    }
  });
});
