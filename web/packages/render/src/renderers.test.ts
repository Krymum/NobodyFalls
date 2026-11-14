import { describe, expect, it } from 'vitest';
import { CharacterRenderer } from './CharacterRenderer.ts';
import { REFERENCE_CAST } from './demo/referenceScene.ts';
import { DEFAULT_LOOK, resolveLook } from './look.ts';
import { seededRandom, shade, solveTwoBone } from './math.ts';
import { CodePartSet } from './parts/CodePartSet.ts';
import { RopeRenderer, ropePoints, sagOf, throwAt } from './RopeRenderer.ts';
import { boxBlur } from './scene/blur.ts';
import { SceneRenderer, type CanvasFactory } from './scene/SceneRenderer.ts';
import { recordingContext } from './testing/recordingContext.ts';

const characters = new CharacterRenderer(new CodePartSet());
const ropes = new RopeRenderer();

describe('math', () => {
  it('shades towards white or black', () => {
    expect(shade('#808080', 0)).toBe('rgb(128,128,128)');
    expect(shade('#808080', 1)).toBe('rgb(255,255,255)');
    expect(shade('#808080', -1)).toBe('rgb(0,0,0)');
  });

  it('is deterministic', () => {
    const a = seededRandom(77);
    const b = seededRandom(77);
    expect([a(), a(), a()]).toEqual([b(), b(), b()]);
  });

  it('folds an out-of-reach limb straight', () => {
    const [x, y] = solveTwoBone([0, 0], [100, 0], 18, 18, 1);
    // Like the reference, the limb keeps a hair of a fold (it never locks fully straight).
    expect(x).toBeCloseTo(18, 1);
    expect(Math.abs(y)).toBeLessThan(0.5);
  });
});

describe('look', () => {
  it('reads styles from catalogue render keys', () => {
    const keys = new Map([
      ['coat-hooded', 'coat.hooded'],
      ['headwear-brim', 'headwear.brim'],
      ['scarf-wool', 'scarf.wool'],
    ]);
    const look = resolveLook(
      {
        skinTone: '#8D6E5A',
        slots: {
          coat: { itemId: 'coat-hooded', color: '#2E4749' },
          headwear: { itemId: 'headwear-brim', color: '#34343A' },
          scarf: { itemId: 'scarf-wool', color: '#B5AD9C' },
        },
      },
      keys,
    );

    expect(look.coat).toEqual({ style: 'hooded', color: '#2E4749' });
    expect(look.headwear).toEqual({ style: 'brim', color: '#34343A' });
    expect(look.scarf?.style).toBe('wool');
    expect(look.hair).toBeUndefined();
  });

  it('falls back to plain styles for unknown items, so a new item never breaks rendering', () => {
    const look = resolveLook(
      { skinTone: '#C9C3B9', slots: { coat: { itemId: 'future-coat', color: '#123456' } } },
      new Map(),
    );
    expect(look.coat).toEqual({ style: 'long', color: '#123456' });
    expect(look.trousers).toBe(DEFAULT_LOOK.trousers);
  });
});

describe('ropes', () => {
  const from = [0, 0] as const;
  const to = [0, 200] as const;

  it('a rope carrying weight is almost straight; a slack one hangs', () => {
    expect(sagOf({ from, to, width: 3, tension: 1 })).toBe(0);
    expect(sagOf({ from, to, width: 3, tension: 0.2 })).toBeGreaterThan(
      sagOf({ from, to, width: 3, tension: 0.8 }),
    );
  });

  it('starts and ends where it is tied', () => {
    const points = ropePoints({ from: [10, 20], to: [110, 220], width: 3, tension: 0.5 });
    expect(points[0]).toEqual([10, 20]);
    expect(points.at(-1)?.[0]).toBeCloseTo(110);
    expect(points.at(-1)?.[1]).toBeCloseTo(220);
  });

  it('a thrown rope unrolls then tightens', () => {
    expect(throwAt(0)).toEqual({ progress: 0, tension: 0 });
    const half = throwAt(0.3);
    expect(half.progress).toBeGreaterThan(0.3);
    expect(half.progress).toBeLessThan(1);
    expect(throwAt(5).progress).toBe(1);
    expect(ropePoints({ from, to, width: 3, progress: 0.5 }).length).toBeLessThan(
      ropePoints({ from, to, width: 3 }).length,
    );
  });

  it('draws threads, coils and ropes without ctx.filter', () => {
    const { ctx, calls } = recordingContext();
    ropes.rope(ctx, { from, to, width: 3, tension: 0.9 });
    ropes.thread(ctx, from, to);
    ropes.coil(ctx, [50, 50], 1);
    expect(calls.filter((c) => c.name === 'stroke').length).toBeGreaterThan(20);
  });
});

describe('CharacterRenderer', () => {
  it('draws every cast member of the reference scene in every posture', () => {
    for (const member of REFERENCE_CAST) {
      for (const posture of ['held', 'attention', 'danger', 'relieved', 'fallen'] as const) {
        const { ctx, calls } = recordingContext();
        characters.draw(ctx, { ...member, posture }, 1.3);
        expect(calls.some((c) => c.name === 'fill')).toBe(true);
        expect(calls.filter((c) => c.name === 'save').length).toBe(
          calls.filter((c) => c.name === 'restore').length,
        );
      }
    }
  });

  it('anchors ropes at the hands and the belt, as in the reference scene', () => {
    const me = REFERENCE_CAST.find((c) => c.id === 'me');
    if (!me) {
      throw new Error('missing cast member');
    }
    const anchors = characters.anchors(me, 0);
    // At t = 0 the pendulum is near rest: hands at ±14 × 1.15 around (195, 282).
    expect(anchors.handL[0]).toBeCloseTo(
      195 + anchors.skeleton.sway.drift - 14 * 1.15 * Math.cos(anchors.skeleton.sway.angle),
      6,
    );
    expect(anchors.belt[1]).toBeGreaterThan(282 + 60);
  });

  it('a silhouette uses one colour and no lighting', () => {
    const { ctx, calls, sets } = recordingContext();
    characters.draw(
      ctx,
      {
        ...(REFERENCE_CAST[0] ?? { x: 0, y: 0, scale: 1, phase: 0, look: DEFAULT_LOOK }),
        posture: 'held',
        mode: { kind: 'silhouette', color: '#1F2731' },
      },
      0,
    );
    const colours = new Set(
      sets.filter((s) => s.name === 'fillStyle' || s.name === 'strokeStyle').map((s) => s.args[0]),
    );
    expect([...colours]).toEqual(['#1F2731']);
    expect(calls.some((c) => c.name.startsWith('create'))).toBe(false);
  });

  it('fades fallen characters', () => {
    const { ctx, sets } = recordingContext();
    characters.draw(
      ctx,
      { x: 0, y: 0, scale: 1, phase: 0, posture: 'fallen', look: DEFAULT_LOOK, opacity: 0.4 },
      0,
    );
    expect(sets.find((s) => s.name === 'globalAlpha')?.args[0]).toBe(0.4);
  });
});

describe('scene', () => {
  it('blurs by spreading light without creating it', () => {
    const size = 9;
    const pixels = new Uint8ClampedArray(size * size * 4);
    const centre = (4 * size + 4) * 4;
    pixels[centre + 3] = 255;
    boxBlur(pixels, size, size, 1);
    expect(pixels[centre + 3]).toBeLessThan(255);
    expect(pixels[(4 * size + 5) * 4 + 3]).toBeGreaterThan(0);
  });

  it('pre-renders the crowd once and draws frames without ctx.filter', () => {
    const created: { width: number; height: number }[] = [];
    const factory: CanvasFactory = {
      create(width, height) {
        created.push({ width, height });
        return { image: {} as CanvasImageSource, ctx: recordingContext(width, height).ctx };
      },
    };
    const scene = new SceneRenderer(characters, ropes, factory);
    scene.prepare(390, 844, 2);
    expect(created).toHaveLength(22); // 18 far figures + 4 in the middle plane

    const { ctx, calls } = recordingContext();
    scene.drawBackground(ctx, 2.5);
    scene.drawAtmosphere(ctx, 2.5);
    expect(calls.filter((c) => c.name === 'drawImage')).toHaveLength(22);
    expect(created).toHaveLength(22); // nothing re-rendered per frame
  });
});
