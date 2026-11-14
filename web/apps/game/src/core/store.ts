import type { RenderKeys } from '@nobodyfalls/render';
import { create } from 'zustand';
import { api, ApiError } from '../api/client.ts';
import { connectRealtime, type RealtimeConnection } from '../api/realtime.ts';
import { humanToken } from '../api/turnstile.ts';
import type {
  EdgeEntry,
  Friend,
  FriendRequests,
  GameEvent,
  MyProfile,
  Session,
  Suggestion,
  Tonight,
  World,
} from '../api/types.ts';
import { syncServerTime } from './time.ts';

/** A rope I just threw, animated on the Tonight scene. */
export interface Throw {
  readonly to: string;
  readonly at: number;
}

interface GameState {
  session: Session | null;
  renderKeys: RenderKeys;
  tonight: Tonight | null;
  world: World | null;
  edge: { entries: readonly EdgeEntry[]; nextOffset: number | null; loaded: boolean };
  friends: readonly Friend[];
  requests: FriendRequests;
  suggestions: readonly Suggestion[];
  profile: MyProfile | null;
  lastThrow: Throw | null;
  /** Players I am giving a hand to right now (button disabled). */
  pending: ReadonlySet<string>;
}

const initial: GameState = {
  session: null,
  renderKeys: new Map(),
  tonight: null,
  world: null,
  edge: { entries: [], nextOffset: null, loaded: false },
  friends: [],
  requests: { incoming: [], outgoing: [] },
  suggestions: [],
  profile: null,
  lastThrow: null,
  pending: new Set(),
};

/**
 * The game's client state, outside the components (GAME_DESIGN §14). Screens read
 * it with `useGame(selector)` and call the actions below; real-time events and API
 * answers are the only things that change it.
 */
export const useGame = create<GameState>(() => initial);

const set = useGame.setState;
const get = useGame.getState;

let realtime: RealtimeConnection | null = null;

export const game = {
  async loadSession(): Promise<Session> {
    const session = await api.session();
    set({ session });
    if (session.state === 'player') {
      void game.loadCatalog();
      void game.startRealtime();
    }
    return session;
  },

  async loadCatalog(): Promise<void> {
    const catalog = await api.catalog();
    set({ renderKeys: new Map(catalog.items.map((item) => [item.id, item.renderKey])) });
  },

  async loadTonight(): Promise<void> {
    const tonight = await api.tonight();
    syncServerTime(tonight.serverNow);
    set({ tonight });
  },

  async loadWorld(): Promise<void> {
    set({ world: await api.world() });
  },

  async loadEdge(more = false): Promise<void> {
    const offset = more ? (get().edge.nextOffset ?? 0) : 0;
    const page = await api.edge(offset);
    set((state) => ({
      edge: {
        entries: more ? [...state.edge.entries, ...page.entries] : page.entries,
        nextOffset: page.nextOffset,
        loaded: true,
      },
    }));
  },

  async loadFriends(): Promise<void> {
    const [friends, requests, suggestions] = await Promise.all([
      api.friends(),
      api.friendRequests(),
      api.suggestions(),
    ]);
    set({ friends: friends.items, requests, suggestions: suggestions.items });
  },

  async loadProfile(): Promise<void> {
    set({ profile: await api.profile() });
  },

  /** Give one of today's hands. Throws an `ApiError` the screen translates. */
  async hold(playerId: string): Promise<void> {
    const session = get().session;
    if (!session || get().pending.has(playerId)) {
      return;
    }
    set((state) => ({ pending: new Set([...state.pending, playerId]) }));
    try {
      const token = await humanToken(session.turnstileSiteKey);
      const hand = await api.hold(playerId, token);
      set((state) => ({
        lastThrow: { to: playerId, at: performance.now() },
        tonight: state.tonight ? { ...state.tonight, handsLeft: hand.handsLeft } : null,
        edge: {
          ...state.edge,
          entries: state.edge.entries
            .map((e) => (e.player.id === playerId ? { ...e, received: hand.receiverReceived } : e))
            .filter((e) => e.received < e.required),
        },
      }));
      await game.loadTonight();
    } finally {
      set((state) => {
        const pending = new Set(state.pending);
        pending.delete(playerId);
        return { pending };
      });
    }
  },

  async startRealtime(): Promise<void> {
    if (realtime) {
      return;
    }
    try {
      realtime = await connectRealtime(
        (events) => {
          game.apply(events);
        },
        () => {
          void game.refreshAll();
        },
      );
    } catch {
      // Real time is a bonus: screens still load their data, and reload on focus.
      realtime = null;
    }
  },

  watchEdge(watching: boolean): void {
    realtime?.watchEdge(watching);
  },

  /** Applies a batch of real-time events (ADR 0008). */
  apply(events: readonly GameEvent[]): void {
    let reloadTonight = false;
    let reloadFriends = false;
    for (const event of events) {
      switch (event.type) {
        case 'me.hands':
          set((s) => ({ tonight: s.tonight ? { ...s.tonight, handsLeft: event.handsLeft } : null }));
          break;
        case 'me.handReceived':
          reloadTonight = true;
          break;
        case 'player.standing':
          set((s) => ({
            friends: s.friends.map((f) =>
              f.player.id === event.playerId && f.today
                ? { ...f, today: { ...f.today, received: event.received, required: event.required } }
                : f,
            ),
            tonight: s.tonight
              ? {
                  ...s.tonight,
                  holding: s.tonight.holding.map((h) =>
                    h.player.id === event.playerId && h.standing
                      ? { ...h, standing: { ...h.standing, received: event.received } }
                      : h,
                  ),
                }
              : null,
          }));
          break;
        case 'edge.changed':
          set((s) => ({
            edge: {
              ...s.edge,
              entries: s.edge.entries
                .map((e) => (e.player.id === event.playerId ? { ...e, received: event.received } : e))
                .filter((e) => e.received < e.required),
            },
          }));
          break;
        case 'friend.request':
        case 'friend.accepted':
          reloadFriends = true;
          break;
        case 'world.stats':
          set((s) => ({
            world: s.world ? { ...s.world, alive: event.alive, underThreshold: event.underThreshold } : null,
          }));
          break;
        case 'night.resolved':
          void game.refreshAll();
          return;
      }
    }
    if (reloadTonight) {
      void game.loadTonight();
    }
    if (reloadFriends) {
      void game.loadFriends();
    }
  },

  /** After midnight, a reconnection or a return to the app: reload what is on screen. */
  async refreshAll(): Promise<void> {
    const loaded = get();
    await Promise.allSettled([
      game.loadTonight(),
      game.loadWorld(),
      loaded.edge.loaded ? game.loadEdge() : Promise.resolve(),
      loaded.friends.length > 0 ? game.loadFriends() : Promise.resolve(),
      loaded.profile ? game.loadProfile() : Promise.resolve(),
    ]);
  },

  async logout(): Promise<void> {
    await api.logout();
    realtime?.stop();
    realtime = null;
    set(initial);
  },
};

/** The translation key of an error, falling back to a generic message. */
export function errorKey(error: unknown): string {
  return error instanceof ApiError ? `errors.${error.code}` : 'errors.request.failed';
}
