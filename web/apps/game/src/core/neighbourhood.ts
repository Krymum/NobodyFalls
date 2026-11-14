import { DEFAULT_LOOK, resolveLook, type Look, type Posture, type RenderKeys } from '@nobodyfalls/render';
import type { EdgeEntry, PlayerRef, Tonight } from '../api/types.ts';
import { myState, postureFor, postureOf, phaseOf, toneOf, type Tone } from './standing.ts';

/** Composition space of the Tonight scene: the reference phone screen. */
export const SCENE_WIDTH = 390;
export const SCENE_HEIGHT = 844;

export type Role = 'me' | 'holder' | 'held' | 'candidate';

export interface Person {
  readonly id: string;
  readonly role: Role;
  readonly player: PlayerRef | null;
  readonly x: number;
  readonly y: number;
  readonly scale: number;
  readonly posture: Posture;
  readonly phase: number;
  readonly look: Look;
  readonly tone: Tone;
  /** "1/2" under the name, for the people I hold or could hold. */
  readonly count: string | null;
  /** Which side of the character its name goes, so that names never overlap. */
  readonly labelSide: 'right' | 'left';
  /** Vertical offset of the name from the hands, in composition units. */
  readonly labelDy: number;
}

/** A rope between two people, or from the top of the screen ("the world") to someone. */
export interface Link {
  /** A person's id, or `'sky'` for a rope coming from above the screen. */
  readonly from: string;
  readonly to: string;
  /** Which hand of the receiver takes the rope. */
  readonly hand: 'left' | 'right' | 'grip';
}

export interface Neighbourhood {
  readonly people: readonly Person[];
  readonly links: readonly Link[];
  /** Coiled ropes at my hip: hands I can still give. */
  readonly freeHands: number;
  /** Shown hanging by a thread: someone on the Edge I could hold now. */
  readonly candidate: Person | null;
  /** People not drawn for lack of room ("+3"). */
  readonly hiddenHolders: number;
  readonly hiddenHeld: number;
}

const MAX_PER_ROW = 3;

/**
 * Rows of the composition. Higher than in the reference scene: the text panel and the
 * tab bar take the bottom of the screen, and nobody should hang behind them.
 */
export const ROWS = { holders: 120, me: 255, held: 425, candidate: 455 } as const;
const ROW_X: Record<number, readonly number[]> = {
  1: [195],
  2: [106, 296],
  3: [70, 195, 320],
};

function look(player: PlayerRef | null, keys: RenderKeys): Look {
  return player?.appearance ? resolveLook(player.appearance, keys) : DEFAULT_LOOK;
}

/**
 * Lays out my neighbourhood like the reference scene: those who hold me above, tied
 * at their belts; me in the middle; those I hold below; and, when I have a hand
 * left, someone from the Edge hanging by a thread on the right. Pure: data in, a
 * composition out.
 */
export function layoutNeighbourhood(
  tonight: Tonight,
  me: PlayerRef,
  keys: RenderKeys,
  candidate: EdgeEntry | null,
): Neighbourhood {
  const state = myState(tonight);
  const people: Person[] = [];
  const links: Link[] = [];

  const holders = tonight.heldBy.slice(0, MAX_PER_ROW);
  holders.forEach((link, i) => {
    people.push({
      id: link.player.id,
      role: 'holder',
      player: link.player,
      x: ROW_X[holders.length]?.[i] ?? 195,
      y: holders.length === 2 ? ROWS.holders + 8 - i * 18 : ROWS.holders,
      scale: 0.8,
      posture: 'held',
      phase: phaseOf(link.player.id),
      look: look(link.player, keys),
      tone: 'calm',
      count: null,
      labelSide: 'right',
      labelDy: 6,
    });
  });

  // My hands: on the holders' ropes; the world's ropes on a first night; a thread when no one holds me.
  const posture = postureFor(state);
  if (holders.length > 0) {
    holders.forEach((link, i) => {
      links.push({ from: link.player.id, to: me.id, hand: i % 2 === 0 ? 'left' : 'right' });
    });
    if (holders.length === 1 && posture === 'held') {
      links.push({ from: 'sky', to: me.id, hand: 'right' });
    }
  } else if (posture === 'held') {
    links.push({ from: 'sky', to: me.id, hand: 'left' }, { from: 'sky', to: me.id, hand: 'right' });
  } else if (posture === 'danger') {
    links.push({ from: 'sky', to: me.id, hand: 'grip' });
  }

  people.push({
    id: me.id,
    role: 'me',
    player: me,
    x: 195,
    y: ROWS.me,
    scale: 1.15,
    posture: posture === 'attention' && holders.length === 0 ? 'danger' : posture,
    phase: phaseOf(me.id),
    look: look(me, keys),
    tone: toneOf(tonight.standing),
    count: null,
    labelSide: 'right',
    labelDy: 6,
  });

  const held = tonight.holding.slice(0, MAX_PER_ROW);
  held.forEach((link, i) => {
    people.push({
      id: link.player.id,
      role: 'held',
      player: link.player,
      x: (ROW_X[held.length]?.[i] ?? 195) - (candidate ? 20 : 0),
      y: ROWS.held + i * 18,
      scale: 0.85,
      posture: postureOf(link.standing),
      phase: phaseOf(link.player.id),
      look: look(link.player, keys),
      tone: toneOf(link.standing),
      count: link.standing ? `${link.standing.received}/${link.standing.required}` : null,
      // The last one keeps its name on its left when someone hangs further right.
      labelSide: candidate && i === held.length - 1 && held.length > 1 ? 'left' : 'right',
      labelDy: 6,
    });
    // My rope reaches their right hand; if they are fully held, someone off-screen holds the other.
    links.push({ from: me.id, to: link.player.id, hand: 'right' });
    if (postureOf(link.standing) === 'held') {
      links.push({ from: 'sky', to: link.player.id, hand: 'left' });
    }
  });

  const alreadyHeld = new Set(tonight.holding.map((h) => h.player.id));
  const showCandidate =
    candidate !== null &&
    tonight.handsLeft > 0 &&
    candidate.player.id !== me.id &&
    !alreadyHeld.has(candidate.player.id);
  const candidatePerson: Person | null = showCandidate
    ? {
        id: candidate.player.id,
        role: 'candidate',
        player: candidate.player,
        x: 318,
        y: ROWS.candidate,
        scale: 0.8,
        posture: candidate.received === 0 ? 'danger' : 'attention',
        phase: phaseOf(candidate.player.id),
        look: look(candidate.player, keys),
        tone: candidate.received === 0 ? 'danger' : 'attention',
        count: `${candidate.received}/${candidate.required}`,
        labelSide: 'left',
        labelDy: -40,
      }
    : null;
  if (candidatePerson) {
    people.push(candidatePerson);
  }

  return {
    people,
    links,
    freeHands: Math.max(0, tonight.handsLeft),
    candidate: candidatePerson,
    hiddenHolders: Math.max(0, tonight.heldBy.length - MAX_PER_ROW),
    hiddenHeld: Math.max(0, tonight.holding.length - MAX_PER_ROW),
  };
}
