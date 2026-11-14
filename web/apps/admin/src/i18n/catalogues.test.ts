import { describe, expect, it } from 'vitest';
import en from './en.json';
import fr from './fr.json';

type Tree = Record<string, unknown>;

function keys(tree: unknown, prefix = ''): string[] {
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

describe('admin translations (FR and EN, GAME_DESIGN §9)', () => {
  it('French and English have exactly the same keys', () => {
    expect(keys(en).sort()).toEqual(keys(fr).sort());
  });

  it('every text uses the same placeholders in both languages', () => {
    const french = placeholders(fr);
    for (const [key, names] of placeholders(en)) {
      expect({ key, names: french.get(key) }).toEqual({ key, names });
    }
  });

  it('every error code the admin API can return has a text', () => {
    const codes = [
      'auth.required',
      'admin.forbidden',
      'admin.totp_required',
      'admin.reauth_required',
      'admin.totp_not_enrolled',
      'admin.totp_already_enrolled',
      'admin.totp_invalid',
      'admin.totp_locked',
      'staff.self',
      'staff.no_roles',
      'staff.unknown_role',
      'staff.not_found',
      'staff.player_not_found',
      'staff.already_member',
      'staff.last_admin',
      'staff.not_human',
      'sanction.kind',
      'sanction.reason',
      'sanction.public_message',
      'sanction.period',
      'sanction.no_period',
      'sanction.self',
      'sanction.staff',
      'sanction.not_found',
      'sanction.already_revoked',
      'sanction.player_not_found',
      'report.not_found',
      'report.already_resolved',
      'report.outcome',
      'report.note',
      'report.sanction_needs_actioned',
      'ticket.not_found',
      'ticket.message',
      'ticket.closed',
      'ticket.status',
      'ticket.own_sanction',
      'idea.not_found',
      'idea.status',
      'idea.answer',
      'idea.merge_self',
      'idea.merge_target',
      'request.failed',
      'request.rate_limited',
      'request.missing_header',
      'network.offline',
    ];
    const all = new Set(keys(fr));
    for (const code of codes) {
      expect(all, code).toContain(`errors.${code}`);
    }
  });

  it('every audit action the admin writes has a label', () => {
    const actions = [
      'staff.bootstrapped',
      'staff.added',
      'staff.roles_changed',
      'staff.removed',
      'staff.totp_reset',
      'admin.totp_enrolled',
      'admin.signed_in',
      'admin.sign_in_failed',
      'admin.sign_in_locked',
      'audit.viewed',
      'audit.verified',
      'sanction.issued',
      'sanction.revoked',
      'report.assigned',
      'report.resolved',
      'ticket.replied',
      'ticket.noted',
      'ticket.status_changed',
      'ticket.assigned',
      'idea.updated',
      'idea.merged',
    ];
    const all = new Set(keys(fr));
    for (const action of actions) {
      expect(all, action).toContain(`audit.actions.${action}`);
    }
  });
});
