import { describe, expect, it } from 'vitest';
import type { EdgeEntry, HandLink, PlayerRef, Tonight } from '../api/types.ts';
import { layoutNeighbourhood } from './neighbourhood.ts';
import { myState, postureOf } from './standing.ts';
import { countdown, isLastHour } from './time.ts';

const player = (id: string): PlayerRef => ({ id, pseudo: id, isWatcher: false, appearance: null });
const link = (id: string, received = 2, required = 2): HandLink => ({
  player: player(id),
  givenAt: '2026-11-14T10:00:00Z',
  standing: { received, required, immune: false },
});

const base: Tonight = {
  day: '2026-11-14',
  nightNumber: 41,
  serverNow: '2026-11-14T20:00:00Z',
  midnightAt: '2026-11-15T00:00:00Z',
  seasonOpen: true,
  storm: false,
  status: 'alive',
  standing: { received: 2, required: 2, immune: false },
  handsLeft: 1,
  heldBy: [link('lea'), link('kenji')],
  holding: [link('tom'), link('ines', 1, 2)],
  fellOn: null,
  returned: null,
};

const yuki: EdgeEntry = { player: player('yuki'), received: 0, required: 2, immune: false, isFriend: false };
const me = player('me');

describe('layoutNeighbourhood', () => {
  it('reproduces the reference scene: holders above, me, held below, someone on a thread', () => {
    const scene = layoutNeighbourhood(base, me, new Map(), yuki);

    const byId = new Map(scene.people.map((p) => [p.id, p]));
    expect(byId.get('lea')?.y).toBeLessThan(byId.get('me')?.y ?? 0);
    expect(byId.get('tom')?.y).toBeGreaterThan(byId.get('me')?.y ?? 0);
    expect(byId.get('me')?.posture).toBe('held');
    expect(byId.get('ines')?.posture).toBe('attention');
    expect(byId.get('ines')?.count).toBe('1/2');
    expect(scene.candidate?.id).toBe('yuki');
    expect(scene.candidate?.posture).toBe('danger');
    expect(scene.links).toContainEqual({ from: 'lea', to: 'me', hand: 'left' });
    expect(scene.links).toContainEqual({ from: 'me', to: 'tom', hand: 'right' });
    expect(scene.links).toContainEqual({ from: 'sky', to: 'tom', hand: 'left' });
    expect(scene.freeHands).toBe(1);
    // Names never collide: the candidate's goes left and above, the last held one left too.
    expect(scene.candidate?.labelSide).toBe('left');
    expect(byId.get('ines')?.labelSide).toBe('left');
    expect(byId.get('tom')?.labelSide).toBe('right');
  });

  it('no candidate without a hand left, nor someone I already hold', () => {
    expect(layoutNeighbourhood({ ...base, handsLeft: 0 }, me, new Map(), yuki).candidate).toBeNull();
    const tom: EdgeEntry = { ...yuki, player: player('tom') };
    expect(layoutNeighbourhood(base, me, new Map(), tom).candidate).toBeNull();
  });

  it('alone with no hand: a thread from the sky; on a first night: the world holds you', () => {
    const alone = layoutNeighbourhood(
      { ...base, heldBy: [], standing: { received: 0, required: 2, immune: false } },
      me,
      new Map(),
      null,
    );
    expect(alone.people.find((p) => p.role === 'me')?.posture).toBe('danger');
    expect(alone.links).toEqual(expect.arrayContaining([{ from: 'sky', to: 'me', hand: 'grip' }]));

    const newcomer = layoutNeighbourhood(
      { ...base, heldBy: [], standing: { received: 0, required: 2, immune: true } },
      me,
      new Map(),
      null,
    );
    expect(newcomer.people.find((p) => p.role === 'me')?.posture).toBe('held');
    expect(newcomer.links.filter((l) => l.from === 'sky' && l.to === 'me')).toHaveLength(2);
  });

  it('crowds are capped and counted', () => {
    const many = layoutNeighbourhood(
      { ...base, heldBy: ['a', 'b', 'c', 'd', 'e'].map((id) => link(id)) },
      me,
      new Map(),
      null,
    );
    expect(many.people.filter((p) => p.role === 'holder')).toHaveLength(3);
    expect(many.hiddenHolders).toBe(2);
  });
});

describe('standing', () => {
  it('maps the day to my state', () => {
    expect(myState(base)).toBe('held');
    expect(myState({ ...base, standing: { received: 1, required: 2, immune: false } })).toBe('attention');
    expect(myState({ ...base, standing: { received: 0, required: 2, immune: false } })).toBe('danger');
    expect(myState({ ...base, standing: { received: 0, required: 2, immune: true } })).toBe('firstNight');
    expect(myState({ ...base, status: 'fallen' })).toBe('fallen');
    expect(myState({ ...base, status: 'waitlisted' })).toBe('waitlisted');
  });

  it('postures follow the hands received', () => {
    expect(postureOf({ received: 2, required: 2, immune: false })).toBe('held');
    expect(postureOf({ received: 1, required: 2, immune: false })).toBe('attention');
    expect(postureOf({ received: 0, required: 2, immune: false })).toBe('danger');
    expect(postureOf(null, 'fallen')).toBe('fallen');
  });
});

describe('time', () => {
  it('counts down to midnight and spots the last hour', () => {
    const now = Date.parse('2026-11-14T20:58:53Z');
    expect(countdown('2026-11-15T00:00:00Z', now)).toBe('03:01:07');
    expect(countdown('2026-11-14T00:00:00Z', now)).toBe('00:00:00');
    expect(isLastHour('2026-11-15T00:00:00Z', Date.parse('2026-11-14T23:30:00Z'))).toBe(true);
    expect(isLastHour('2026-11-15T00:00:00Z', now)).toBe(false);
  });
});
