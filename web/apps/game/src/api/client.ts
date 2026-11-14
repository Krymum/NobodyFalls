import type {
  Catalog,
  EdgePage,
  Friend,
  FriendRequests,
  GivenHand,
  InvitePreview,
  ListOf,
  Locale,
  MapNameVisibility,
  MyProfile,
  NotificationPreferences,
  PlayerRef,
  Session,
  Suggestion,
  Tonight,
  World,
} from './types.ts';
import type { Appearance } from '@nobodyfalls/render';

/** A refused request. `code` is stable and translated by the interface (`errors.<code>`). */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
    readonly retryAfterSeconds: number | null = null,
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
  const retry = Number(response.headers.get('Retry-After'));
  throw new ApiError(response.status, code, title, Number.isFinite(retry) && retry > 0 ? retry : null);
}

const get = <T>(path: string): Promise<T> => request<T>('GET', path);

/** The game API (`/api/v1`). One function per endpoint, nothing else. */
export const api = {
  session: () => get<Session>('/api/v1/session'),
  register: (body: {
    pseudo: string;
    appearance: Appearance;
    locale: Locale;
    inviteCode: string | null;
    turnstileToken: string;
  }) => request<{ id: string }>('POST', '/api/v1/registration', body),
  invite: (code: string) => get<InvitePreview>(`/api/v1/invites/${encodeURIComponent(code)}`),
  catalog: () => get<Catalog>('/api/v1/skins/catalog'),

  tonight: () => get<Tonight>('/api/v1/tonight'),
  world: () => get<World>('/api/v1/world'),
  edge: (offset: number, limit = 30) => get<EdgePage>(`/api/v1/edge?offset=${offset}&limit=${limit}`),
  hold: (receiverId: string, turnstileToken: string) =>
    request<GivenHand>('POST', '/api/v1/hands', { receiverId, turnstileToken }),

  profile: () => get<MyProfile>('/api/v1/me/profile'),
  updateSettings: (settings: { locale?: Locale; mapNameVisibility?: MapNameVisibility }) =>
    request<undefined>('PATCH', '/api/v1/me/settings', settings),
  notificationPreferences: () => get<NotificationPreferences>('/api/v1/me/notifications'),
  saveNotificationPreferences: (preferences: NotificationPreferences) =>
    request<undefined>('PUT', '/api/v1/me/notifications', preferences),

  friends: () => get<ListOf<Friend>>('/api/v1/friends'),
  friendRequests: () => get<FriendRequests>('/api/v1/friends/requests'),
  suggestions: () => get<ListOf<Suggestion>>('/api/v1/friends/suggestions'),
  askFriend: (pseudo: string) =>
    request<{ outcome: 'sent' | 'becameFriends' }>('POST', '/api/v1/friends/requests', { pseudo }),
  acceptRequest: (id: number) => request<undefined>('POST', `/api/v1/friends/requests/${id}/accept`),
  declineRequest: (id: number) => request<undefined>('POST', `/api/v1/friends/requests/${id}/decline`),
  cancelRequest: (id: number) => request<undefined>('DELETE', `/api/v1/friends/requests/${id}`),
  removeFriend: (id: string) => request<undefined>('DELETE', `/api/v1/friends/${id}`),
  blocked: () => get<ListOf<PlayerRef>>('/api/v1/blocks'),
  block: (id: string) => request<undefined>('PUT', `/api/v1/blocks/${id}`),
  unblock: (id: string) => request<undefined>('DELETE', `/api/v1/blocks/${id}`),

  vapidKey: () => get<{ publicKey: string }>('/api/v1/push/vapid-key'),
  subscribePush: (subscription: { endpoint: string; keys: { p256dh: string; auth: string } }) =>
    request<undefined>('POST', '/api/v1/push/subscriptions', subscription),
  unsubscribePush: (endpoint: string) =>
    request<undefined>('DELETE', `/api/v1/push/subscriptions?endpoint=${encodeURIComponent(endpoint)}`),

  logout: () => request<undefined>('POST', '/auth/logout'),
};

/** Development tools (`/dev`), only answered when the server runs with dev tools. */
export const devApi = {
  clock: (body: { advanceMinutes?: number; toMidnightMinusSeconds?: number; reset?: boolean }) =>
    request<{ now: string }>('POST', '/dev/clock', body),
  seed: (players: number) =>
    request<{ players: number }>('POST', '/dev/seed', { players, friendsPerPlayer: 3 }),
  bots: (ratio: number) => request<{ bots: number; handsGiven: number }>('POST', '/dev/bots/play', { ratio }),
  tick: () => request<unknown>('POST', '/dev/night/tick'),
};
