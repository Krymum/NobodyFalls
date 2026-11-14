import { create } from 'zustand';
import { adminApi, ApiError, type AdminSession } from './api.ts';

/** Where the member stands, from the first request on. */
export type Gate =
  | { kind: 'loading' }
  | { kind: 'signed-out' }
  | { kind: 'no-access' }
  | { kind: 'enroll'; session: AdminSession }
  | { kind: 'login'; session: AdminSession }
  | { kind: 'ready'; session: AdminSession }
  | { kind: 'error'; code: string };

interface Reauth {
  resolve: () => void;
  reject: (reason: unknown) => void;
}

interface AdminState {
  gate: Gate;
  reauth: Reauth | null;
  load: () => Promise<void>;
  /** Runs an action; if the server wants a fresh code first, asks for it, then runs it again once. */
  sensitive: <T>(action: () => Promise<T>) => Promise<T>;
  confirmReauth: (code: string) => Promise<void>;
  cancelReauth: () => void;
  signOut: () => Promise<void>;
}

export function gateOf(session: AdminSession): Gate {
  if (!session.totpEnrolled) {
    return { kind: 'enroll', session };
  }
  return session.signedIn ? { kind: 'ready', session } : { kind: 'login', session };
}

export const useAdmin = create<AdminState>((set, get) => ({
  gate: { kind: 'loading' },
  reauth: null,

  load: async () => {
    try {
      set({ gate: gateOf(await adminApi.session()) });
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        set({ gate: { kind: 'signed-out' } });
      } else if (e instanceof ApiError && e.code === 'admin.forbidden') {
        set({ gate: { kind: 'no-access' } });
      } else {
        set({ gate: { kind: 'error', code: e instanceof ApiError ? e.code : 'request.failed' } });
      }
    }
  },

  sensitive: async (action) => {
    try {
      return await action();
    } catch (e) {
      if (e instanceof ApiError && e.code === 'admin.totp_required') {
        // The admin session ended (idle or 8 hours): back to the sign-in screen.
        await get().load();
      }
      if (!(e instanceof ApiError) || e.code !== 'admin.reauth_required') {
        throw e;
      }
      await new Promise<void>((resolve, reject) => {
        set({ reauth: { resolve, reject } });
      });
      return action();
    }
  },

  confirmReauth: async (code) => {
    await adminApi.reauth(code);
    const pending = get().reauth;
    set({ reauth: null });
    pending?.resolve();
  },

  cancelReauth: () => {
    const pending = get().reauth;
    set({ reauth: null });
    pending?.reject(new ApiError(401, 'admin.reauth_cancelled', 'Cancelled.'));
  },

  signOut: async () => {
    await adminApi.logout();
    await get().load();
  },
}));

export function can(gate: Gate, permission: string): boolean {
  return gate.kind === 'ready' && gate.session.permissions.includes(permission);
}
