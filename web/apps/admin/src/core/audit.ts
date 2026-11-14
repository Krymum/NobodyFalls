import type { Json } from '../api.ts';

export interface FieldChange {
  field: string;
  before: string;
  after: string;
}

function show(value: Json | undefined): string {
  if (value === undefined || value === null) {
    return '—';
  }
  if (Array.isArray(value)) {
    return value.map((v) => show(v)).join(', ');
  }
  return typeof value === 'object' ? JSON.stringify(value) : String(value);
}

function fields(value: Json): Record<string, Json> {
  return value !== null && typeof value === 'object' && !Array.isArray(value) ? value : {};
}

/**
 * The fields of an audit entry, side by side: what it was, what it became. Entries keep
 * only the fields that changed (ADR 0019), so every field present is listed.
 */
export function changes(before: Json, after: Json): FieldChange[] {
  const b = fields(before);
  const a = fields(after);
  const names = [...new Set([...Object.keys(b), ...Object.keys(a)])].sort();
  return names.map((field) => ({ field, before: show(b[field]), after: show(a[field]) }));
}

/** `staff.roles_changed` → `staff`: the family an action belongs to, for filters. */
export function family(action: string): string {
  return action.split('.')[0] ?? action;
}
