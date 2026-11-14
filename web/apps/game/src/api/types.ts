import type { Appearance } from '@nobodyfalls/render';

// Shapes of the API's JSON (src/NobodyFalls.Application/Queries/Views.cs and
// src/NobodyFalls.Api/Endpoints/*). Kept by hand for now; the API tests pin the
// server side of each field.

export type LifeStatus = 'waitlisted' | 'alive' | 'fallen';
export type ReturnOutcome = 'backToday' | 'backTomorrow';

export interface PlayerRef {
  readonly id: string;
  readonly pseudo: string;
  readonly isWatcher: boolean;
  readonly appearance: Appearance | null;
}

export interface DayStanding {
  readonly received: number;
  readonly required: number;
  readonly immune: boolean;
}

export interface HandLink {
  readonly player: PlayerRef;
  readonly givenAt: string;
  readonly standing: DayStanding | null;
}

export interface Tonight {
  readonly day: string;
  readonly nightNumber: number | null;
  readonly serverNow: string;
  readonly midnightAt: string;
  readonly seasonOpen: boolean;
  readonly storm: boolean;
  readonly status: LifeStatus;
  readonly standing: DayStanding | null;
  readonly handsLeft: number;
  readonly heldBy: readonly HandLink[];
  readonly holding: readonly HandLink[];
  readonly fellOn: string | null;
  readonly returned: ReturnOutcome | null;
}

export interface EdgeEntry {
  readonly player: PlayerRef;
  readonly received: number;
  readonly required: number;
  readonly immune: boolean;
  readonly isFriend: boolean;
}

export interface EdgePage {
  readonly entries: readonly EdgeEntry[];
  readonly nextOffset: number | null;
}

export interface World {
  readonly day: string;
  readonly nightNumber: number | null;
  readonly seasonName: string | null;
  readonly streak: number;
  readonly lastNightFalls: number | null;
  readonly alive: number;
  readonly underThreshold: number;
  readonly deathsSeason: number;
  readonly deathsAllTime: number;
  readonly stormTonight: boolean;
}

export interface SessionPlayer {
  readonly id: string;
  readonly pseudo: string;
  readonly status: LifeStatus;
  readonly locale: Locale;
  readonly appearance: Appearance | null;
}

export type Locale = 'fr' | 'en';

export interface Session {
  readonly state: 'anonymous' | 'pendingRegistration' | 'player';
  readonly player: SessionPlayer | null;
  readonly returned: ReturnOutcome | null;
  readonly providers: readonly string[];
  readonly turnstileSiteKey: string;
  readonly devTools: boolean;
}

export interface LifeDebt {
  readonly player: PlayerRef;
  readonly day: string;
}

export type MapNameVisibility = 'friends' | 'everyone' | 'nobody';

export interface MyProfile {
  readonly me: PlayerRef;
  readonly status: LifeStatus;
  readonly guardianScore: number;
  readonly fallsCount: number;
  readonly inviteCode: string;
  readonly locale: Locale;
  readonly mapNameVisibility: MapNameVisibility;
  readonly savedBy: readonly LifeDebt[];
  readonly saved: readonly LifeDebt[];
}

export interface Friend {
  readonly player: PlayerRef;
  readonly status: LifeStatus;
  readonly today: DayStanding | null;
  readonly since: string;
}

export interface FriendRequest {
  readonly id: number;
  readonly player: PlayerRef;
  readonly createdAt: string;
}

export interface FriendRequests {
  readonly incoming: readonly FriendRequest[];
  readonly outgoing: readonly FriendRequest[];
}

export interface Suggestion {
  readonly player: PlayerRef;
  readonly nights: number;
}

export type Slot = 'hair' | 'headwear' | 'coat' | 'scarf' | 'gloves' | 'trousers' | 'boots';

export interface CatalogItem {
  readonly id: string;
  readonly slot: Slot;
  readonly rarity: 'base' | 'unlockable' | 'exclusive';
  readonly renderKey: string;
  readonly colors: readonly string[];
  readonly unlock: { readonly metric: string; readonly threshold: number } | null;
  readonly owned: boolean;
}

export interface Catalog {
  readonly items: readonly CatalogItem[];
  readonly skinTones: readonly string[];
}

export interface GivenHand {
  readonly handId: number;
  readonly handsLeft: number;
  readonly receiverReceived: number;
  readonly receiverRequired: number;
}

export interface NotificationPreferences {
  readonly friendBelow: boolean;
  readonly friendFell: boolean;
  readonly savedMe: boolean;
  readonly fell: boolean;
  readonly storm: boolean;
}

export interface InvitePreview {
  readonly code: string;
  readonly inviter: PlayerRef;
}

export interface ListOf<T> {
  readonly items: readonly T[];
}

/** One real-time event (ADR 0008). */
export type GameEvent =
  | { readonly type: 'me.hands'; readonly handsLeft: number }
  | {
      readonly type: 'me.handReceived';
      readonly from: string;
      readonly received: number;
      readonly required: number;
    }
  | {
      readonly type: 'player.standing';
      readonly playerId: string;
      readonly received: number;
      readonly required: number;
    }
  | {
      readonly type: 'edge.changed';
      readonly playerId: string;
      readonly received: number;
      readonly required: number;
    }
  | { readonly type: 'friend.request'; readonly from: string }
  | { readonly type: 'friend.accepted'; readonly with: string }
  | { readonly type: 'world.stats'; readonly alive: number; readonly underThreshold: number }
  | {
      readonly type: 'night.resolved';
      readonly day: string;
      readonly falls: number;
      readonly streak: number;
    };
