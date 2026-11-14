import { describe, expect, it } from 'vitest';
import { changes, family } from './audit.ts';

describe('audit changes', () => {
  it('lists every field side by side, before and after', () => {
    expect(changes({ roles: ['support'] }, { roles: ['moderator', 'support'] })).toEqual([
      { field: 'roles', before: 'support', after: 'moderator, support' },
    ]);
  });

  it('shows a dash for what did not exist on one side', () => {
    expect(changes(null, { method: 'authenticator', attempts: 2 })).toEqual([
      { field: 'attempts', before: '—', after: '2' },
      { field: 'method', before: '—', after: 'authenticator' },
    ]);
    expect(changes({ roles: ['admin'] }, null)).toEqual([{ field: 'roles', before: 'admin', after: '—' }]);
  });

  it('keeps nested values readable', () => {
    expect(changes(null, { filter: { action: 'staff' } })[0]?.after).toBe('{"action":"staff"}');
  });

  it('groups actions by family', () => {
    expect(family('staff.roles_changed')).toBe('staff');
    expect(family('audit')).toBe('audit');
  });
});
