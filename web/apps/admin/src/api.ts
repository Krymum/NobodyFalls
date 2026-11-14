/** The admin API (`/api/v1/admin`, ADR 0017). Types are written by hand, as in the game (ADR 0010). */

export type Locale = 'fr' | 'en';
export type Role = 'admin' | 'moderator' | 'support';
export const ROLES: readonly Role[] = ['admin', 'moderator', 'support'];

export interface AdminSession {
  playerId: string;
  pseudo: string;
  roles: Role[];
  permissions: string[];
  totpEnrolled: boolean;
  signedIn: boolean;
  reauthValidUntil: string | null;
  serverNow: string;
}

export interface Enrollment {
  secret: string;
  uri: string;
}

export interface TeamMember {
  playerId: string;
  pseudo: string;
  roles: Role[];
  totpEnrolled: boolean;
  grantedAt: string;
}

export type Json = null | boolean | number | string | Json[] | { [key: string]: Json };

export interface AuditEntry {
  id: number;
  at: string;
  actorId: string | null;
  actorPseudo: string | null;
  actorRoles: string[];
  action: string;
  targetType: string;
  targetId: string | null;
  before: Json;
  after: Json;
  reason: string | null;
  ip: string | null;
}

export interface AuditPage {
  entries: AuditEntry[];
  nextBeforeId: number | null;
}

export interface AuditFilter {
  action?: string | undefined;
  targetId?: string | undefined;
  before?: number | undefined;
}

export interface AuditVerification {
  valid: boolean;
  checked: number;
  firstBrokenId: number | null;
}

/** A refused request. `code` is stable and translated by the interface (`errors.<code>`). */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (method !== 'GET') {
    // Required on every state-changing call (ADR 0007).
    headers['X-NF-Request'] = '1';
  }
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json';
  }
  const init: RequestInit = { method, headers, credentials: 'same-origin' };
  if (body !== undefined) {
    init.body = JSON.stringify(body);
  }

  let response: Response;
  try {
    response = await fetch(path, init);
  } catch {
    throw new ApiError(0, 'network.offline', 'The network is unavailable.');
  }
  if (response.ok) {
    return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
  }

  let code = response.status === 401 ? 'auth.required' : 'request.failed';
  let title = response.statusText;
  try {
    const problem = (await response.json()) as { code?: string; title?: string };
    code = problem.code ?? code;
    title = problem.title ?? title;
  } catch {
    // Not a problem document: keep the generic code.
  }
  throw new ApiError(response.status, code, title);
}

const base = '/api/v1/admin';
const get = <T>(path: string): Promise<T> => request<T>('GET', base + path);
const post = <T>(path: string, body?: unknown): Promise<T> => request<T>('POST', base + path, body ?? {});

function query(params: Record<string, string | number | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== '') {
      search.set(key, String(value));
    }
  }
  const text = search.toString();
  return text ? `?${text}` : '';
}

export const adminApi = {
  session: () => get<AdminSession>('/session'),
  enroll: () => post<Enrollment>('/session/totp/enroll'),
  confirm: (code: string) => post<{ recoveryCodes: string[] }>('/session/totp/confirm', { code }),
  login: (code: string | null, recoveryCode: string | null) =>
    post<{ method: string }>('/session/login', { code, recoveryCode }),
  reauth: (code: string) => post<{ reauthValidUntil: string }>('/session/reauth', { code }),
  logout: () => post<undefined>('/session/logout'),

  team: () => get<TeamMember[]>('/staff'),
  addMember: (pseudo: string, roles: Role[]) => post<TeamMember>('/staff', { pseudo, roles }),
  changeRoles: (playerId: string, roles: Role[]) =>
    request<TeamMember>('PUT', `${base}/staff/${playerId}`, { roles }),
  removeMember: (playerId: string) => request<undefined>('DELETE', `${base}/staff/${playerId}`),
  resetTotp: (playerId: string) => post<undefined>(`/staff/${playerId}/totp/reset`),

  audit: (filter: AuditFilter) =>
    get<AuditPage>(
      `/audit${query({ action: filter.action, targetId: filter.targetId, before: filter.before })}`,
    ),
  verifyAudit: () => get<AuditVerification>('/audit/verify'),
};
