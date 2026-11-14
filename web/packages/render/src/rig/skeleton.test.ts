import { sway as swayTokens } from '@nobodyfalls/theme';
import { describe, expect, it } from 'vitest';
import type { Vec2 } from '../math.ts';
import { RIG, solvePose, swayAt, toScene, type Posture } from './skeleton.ts';

const distance = (a: Vec2, b: Vec2): number => Math.hypot(a[0] - b[0], a[1] - b[1]);
const postures: Posture[] = ['held', 'attention', 'danger', 'relieved', 'fallen'];

describe('solvePose', () => {
  it.each(postures)('%s keeps the arms anatomically sized', (posture) => {
    for (const t of [0, 0.7, 3.1]) {
      const { joints } = solvePose({ posture, phase: 1 }, t);
      for (const side of ['L', 'R'] as const) {
        const shoulder = joints[`shoulder${side}`];
        const elbow = joints[`elbow${side}`];
        const hand = joints[`hand${side}`];
        expect(distance(shoulder, elbow)).toBeCloseTo(RIG.upperArm, 5);
        if (distance(shoulder, hand) < RIG.upperArm + RIG.forearm - 0.01) {
          expect(distance(elbow, hand)).toBeCloseTo(RIG.forearm, 5);
        }
      }
    }
  });

  it('held: two hands on two ropes, calm', () => {
    const pose = solvePose({ posture: 'held', phase: 0 }, 1);
    expect(pose.grip).toBe('two');
    expect(pose.joints.handL).toEqual([-14, 0]);
    expect(pose.joints.handR).toEqual([14, 0]);
    expect(pose.mouth).toBe('none');
  });

  it('attention: one hand on a rope, the other arm reaching into the void, head tilted', () => {
    const pose = solvePose({ posture: 'attention', phase: 0 }, 1);
    expect(pose.grip).toBe('one');
    expect(pose.joints.handL[0]).toBeLessThan(-20);
    expect(pose.joints.handL[1]).toBeGreaterThan(25);
    expect(pose.headTilt).not.toBe(0);
  });

  it('danger: clinging to a thread, legs kicking, eyes wide, mouth open', () => {
    const a = solvePose({ posture: 'danger', phase: 0 }, 0.2);
    const b = solvePose({ posture: 'danger', phase: 0 }, 0.7);
    expect(a.grip).toBe('thread');
    expect(a.eyesWide).toBe(true);
    expect(a.mouth).toBe('open');
    expect(a.joints.footL).not.toEqual(b.joints.footL);
  });

  it('relieved: holds the thrown rope and smiles', () => {
    const pose = solvePose({ posture: 'relieved', phase: 0 }, 0);
    expect(pose.grip).toBe('thread');
    expect(pose.mouth).toBe('smile');
  });

  it('fallen: arms hanging below the shoulders, eyes closed', () => {
    const pose = solvePose({ posture: 'fallen', phase: 0 }, 0);
    expect(pose.grip).toBe('none');
    expect(pose.joints.handL[1]).toBeGreaterThan(pose.joints.belt[1]);
    expect(pose.eyesClosed).toBe(true);
  });

  it('sways more the worse it gets', () => {
    const peak = (posture: Posture): number =>
      Math.max(
        ...Array.from({ length: 400 }, (_, i) =>
          Math.abs(solvePose({ posture, phase: 0 }, i * 0.05).sway.angle),
        ),
      );
    expect(peak('danger')).toBeGreaterThan(peak('attention'));
    expect(peak('attention')).toBeGreaterThan(peak('held'));
    expect(peak('held')).toBeLessThanOrEqual(swayTokens.held * 1.35 + 1e-9);
  });
});

describe('toScene', () => {
  it('matches the reference scene placement (translate, rotate around the hands, scale)', () => {
    // Reference: world(c, sw, lx, ly) in docs/reference/scene-demo.dc.html.
    const placement = { x: 195, y: 282, scale: 1.15 };
    const sway = swayAt(0.018, 1.1, 2.3);
    const reference = (lx: number, ly: number): Vec2 => {
      const x = lx * 1.15;
      const y = ly * 1.15;
      return [
        195 + sway.drift + x * Math.cos(sway.angle) - y * Math.sin(sway.angle),
        282 + x * Math.sin(sway.angle) + y * Math.cos(sway.angle),
      ];
    };

    expect(toScene(placement, sway, [-14, 0])).toEqual(reference(-14, 0));
    expect(toScene(placement, sway, [0, 60.2])).toEqual(reference(0, 60.2));
  });
});
