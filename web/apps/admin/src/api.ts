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

// ---- Moderation (GAME_DESIGN §9) ----

export type SanctionKind = 'warning' | 'pseudo_reset' | 'score_exclusion' | 'ban';
export const SANCTION_KINDS: readonly SanctionKind[] = ['warning', 'pseudo_reset', 'score_exclusion', 'ban'];

export interface SanctionInput {
  kind: SanctionKind;
  reason: string;
  publicMessage: string | null;
  endsAt: string | null;
}

export interface Sanction {
  id: number;
  kind: SanctionKind;
  reason: string;
  publicMessage: string | null;
  startsAt: string;
  endsAt: string | null;
  issuedBy: string | null;
  reportId: number | null;
  acknowledgedAt: string | null;
  revokedAt: string | null;
  revokedBy: string | null;
  revokeReason: string | null;
  active: boolean;
}

export interface ReportSummary {
  id: number;
  targetType: 'player' | 'idea';
  targetId: string;
  targetPlayerId: string | null;
  targetLabel: string;
  category: 'pseudo' | 'harassment' | 'cheating' | 'other';
  status: 'open' | 'in_review' | 'actioned' | 'dismissed';
  reporterId: string;
  reporterPseudo: string | null;
  assigneePseudo: string | null;
  createdAt: string;
  resolvedAt: string | null;
}

export interface ReportDetail {
  summary: ReportSummary;
  details: string;
  targetSnapshot: Json;
  resolutionNote: string | null;
  sanctionId: number | null;
  otherReportsOnTarget: number;
}

export interface PlayerRow {
  id: string;
  pseudo: string;
  kind: string;
  status: string;
  bannedUntil: string | null;
  scoreExcludedUntil: string | null;
  openReports: number;
  createdAt: string;
  lastSeenAt: string | null;
}

export interface PlayerSheet {
  id: string;
  pseudo: string;
  kind: string;
  status: string;
  locale: string;
  guardianScore: number;
  fallsCount: number;
  createdAt: string;
  lastSeenAt: string | null;
  bannedUntil: string | null;
  scoreExcludedUntil: string | null;
  pseudoResetPending: boolean;
  friends: number;
  handsGiven7: number;
  handsReceived7: number;
  staffRoles: string[];
  sanctions: Sanction[];
  reportsAbout: ReportSummary[];
  reportsMade: ReportSummary[];
}

export interface AdminTicketSummary {
  id: number;
  playerId: string;
  pseudo: string;
  category: 'support' | 'appeal' | 'account_deletion';
  subject: string;
  status: 'open' | 'waiting_player' | 'resolved' | 'closed';
  assigneePseudo: string | null;
  sanctionId: number | null;
  updatedAt: string;
}

export interface AdminTicket {
  summary: AdminTicketSummary;
  messages: {
    id: number;
    authorKind: 'player' | 'staff';
    authorPseudo: string | null;
    body: string;
    internal: boolean;
    at: string;
  }[];
}

export interface AdminIdea {
  id: number;
  title: string;
  body: string;
  category: string;
  status: 'new' | 'under_review' | 'planned' | 'shipped' | 'declined';
  locale: string;
  authorId: string;
  authorPseudo: string | null;
  votes: number;
  staffAnswer: string | null;
  hidden: boolean;
  hiddenReason: string | null;
  mergedInto: number | null;
  createdAt: string;
}

export interface Dashboard {
  alive: number;
  players: number;
  visitedToday: number;
  streak: number;
  deathsAllTime: number;
  nights: { day: string; nightNumber: number; falls: number; aliveAtStart: number; storm: boolean }[];
  retention: { afterDays: number; cohort: number; returned: number }[];
  invitations: { day: string; joined: number }[];
  openReports: number;
  openTickets: number;
  sharedDevices: { deviceId: string; accounts: number; pseudos: string[] }[];
  signUpBursts: { ip: string; accounts: number; since: string }[];
}

export interface AccountLinks {
  addresses: { ip: string; days: number; lastAt: string }[];
  devices: { deviceId: string; agent: string | null; days: number; lastAt: string }[];
  accounts: {
    id: string;
    pseudo: string;
    status: string;
    sharesDevice: boolean;
    sharesIp: boolean;
    lastAt: string;
    bannedUntil: string | null;
  }[];
}

export const IDEA_STATUSES: readonly AdminIdea['status'][] = [
  'new',
  'under_review',
  'planned',
  'shipped',
  'declined',
];
export const TICKET_STATUSES: readonly AdminTicketSummary['status'][] = [
  'open',
  'waiting_player',
  'resolved',
  'closed',
];

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

  dashboard: () => get<Dashboard>('/dashboard'),
  links: (id: string) => get<AccountLinks>(`/players/${id}/links`),
  exportUrl: (id: string) => `${base}/players/${id}/export`,
  deletePlayer: (id: string, reason: string) =>
    request<undefined>('DELETE', `${base}/players/${id}`, { reason }),
  players: (q: string, filter: string) => get<PlayerRow[]>(`/players${query({ q, filter })}`),
  player: (id: string) => get<PlayerSheet>(`/players/${id}`),
  sanction: (playerId: string, input: SanctionInput) =>
    post<{ id: number }>(`/players/${playerId}/sanctions`, input),
  revoke: (sanctionId: number, reason: string) =>
    post<undefined>(`/sanctions/${sanctionId}/revoke`, { reason }),

  reports: (status: string, before?: number) =>
    get<{ items: ReportSummary[]; nextBeforeId: number | null }>(`/reports${query({ status, before })}`),
  report: (id: number) => get<ReportDetail>(`/reports/${id}`),
  assignReport: (id: number) => post<undefined>(`/reports/${id}/assign`, { toMe: true }),
  resolveReport: (
    id: number,
    outcome: 'actioned' | 'dismissed',
    note: string,
    sanction: SanctionInput | null,
  ) => post<{ sanctionId: number | null }>(`/reports/${id}/resolve`, { outcome, note, sanction }),

  tickets: (status: string) => get<AdminTicketSummary[]>(`/tickets${query({ status })}`),
  ticket: (id: number) => get<AdminTicket>(`/tickets/${id}`),
  replyTicket: (id: number, body: string, internal: boolean) =>
    post<undefined>(`/tickets/${id}/messages`, { body, internal }),
  ticketStatus: (id: number, status: AdminTicketSummary['status']) =>
    post<undefined>(`/tickets/${id}/status`, { status }),

  ideas: (status: string, hidden: boolean) =>
    get<AdminIdea[]>(`/ideas${query({ status, hidden: hidden ? 'true' : undefined })}`),
  updateIdea: (
    id: number,
    changes: { status?: string; staffAnswer?: string; hidden?: boolean; hiddenReason?: string },
  ) => request<AdminIdea>('PATCH', `${base}/ideas/${id}`, changes),
  mergeIdea: (id: number, intoId: number) => post<undefined>(`/ideas/${id}/merge`, { intoId }),
};
