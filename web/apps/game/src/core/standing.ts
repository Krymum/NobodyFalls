import type { Posture } from '@nobodyfalls/render';
import type { DayStanding, LifeStatus, Tonight } from '../api/types.ts';

/** Where I stand tonight, which decides the words and the posture of the Tonight screen. */
export type MyState = 'noSeason' | 'waitlisted' | 'firstNight' | 'held' | 'attention' | 'danger' | 'fallen';

export function myState(tonight: Tonight): MyState {
  if (tonight.status === 'waitlisted') {
    return 'waitlisted';
  }
  if (tonight.status === 'fallen') {
    return 'fallen';
  }
  const standing = tonight.standing;
  if (!tonight.seasonOpen || !standing) {
    return 'noSeason';
  }
  if (standing.immune) {
    return 'firstNight';
  }
  if (standing.received >= standing.required) {
    return 'held';
  }
  return standing.received === 0 ? 'danger' : 'attention';
}

/**
 * Posture of anyone, from their hands today (ART_DIRECTION, "Postures"). A newcomer
 * on their immune first night is drawn held: the world holds them that night.
 */
export function postureOf(standing: DayStanding | null, status: LifeStatus = 'alive'): Posture {
  if (status === 'fallen') {
    return 'fallen';
  }
  if (!standing) {
    return 'held';
  }
  if (standing.immune || standing.received >= standing.required) {
    return 'held';
  }
  return standing.received === 0 ? 'danger' : 'attention';
}

export function postureFor(state: MyState): Posture {
  switch (state) {
    case 'fallen':
      return 'fallen';
    case 'danger':
      return 'danger';
    case 'attention':
      return 'attention';
    default:
      return 'held';
  }
}

/** Visual tone of a status, for labels: colours are only ever an extra cue. */
export type Tone = 'calm' | 'attention' | 'danger';

export function toneOf(standing: DayStanding | null): Tone {
  if (!standing || standing.immune || standing.received >= standing.required) {
    return 'calm';
  }
  return standing.received === 0 ? 'danger' : 'attention';
}

/** A stable number from an id, to give each character its own swing. */
export function phaseOf(id: string): number {
  let hash = 0;
  for (const char of id) {
    hash = (hash * 31 + char.charCodeAt(0)) | 0;
  }
  return ((hash >>> 0) % 628) / 100;
}
