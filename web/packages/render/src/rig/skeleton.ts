import { sway as swayTokens } from '@nobodyfalls/theme';
import { solveTwoBone, type Vec2 } from '../math.ts';

/** One posture per status, readable without colour (ART_DIRECTION, "Postures"). */
export type Posture = 'held' | 'attention' | 'danger' | 'relieved' | 'fallen';

export type Joint =
  | 'head'
  | 'neck'
  | 'shoulderL'
  | 'shoulderR'
  | 'elbowL'
  | 'elbowR'
  | 'handL'
  | 'handR'
  | 'hipL'
  | 'hipR'
  | 'kneeL'
  | 'kneeR'
  | 'footL'
  | 'footR'
  | 'belt';

/** How the hands hold on: two ropes, one rope (the other arm reaching out), a single thread, or nothing. */
export type Grip = 'two' | 'one' | 'thread' | 'none';

export type Mouth = 'none' | 'open' | 'smile';

export interface Sway {
  /** Pendulum angle around the hands (radians). */
  readonly angle: number;
  /** Horizontal drift of the body. */
  readonly drift: number;
}

/**
 * The pose of a character at an instant, in its local space (before position, sway
 * and scale). Parts are attached to these joints; an illustrated sprite atlas will
 * use the same attachment points (ART_DIRECTION, "Contraintes techniques").
 */
export interface Skeleton {
  readonly posture: Posture;
  readonly grip: Grip;
  readonly joints: Readonly<Record<Joint, Vec2>>;
  readonly headTilt: number;
  readonly eyesWide: boolean;
  readonly eyesClosed: boolean;
  readonly mouth: Mouth;
  readonly sway: Sway;
}

export interface PoseInput {
  readonly posture: Posture;
  /** Phase offset so that characters do not swing in unison. */
  readonly phase: number;
  /** Overrides the posture's sway amplitude. */
  readonly amplitude?: number;
}

/** Limb lengths of the rig (local units). */
export const RIG = {
  upperArm: 18,
  forearm: 18,
  headRadiusX: 11.5,
  headRadiusY: 12.8,
  scaleToSway: 22,
} as const;

/** The pendulum: slow main swing plus a faster small one. Amplitude follows the posture. */
export function swayAt(amplitude: number, phase: number, t: number): Sway {
  const angle = amplitude * Math.sin(t * 0.9 + phase) + amplitude * 0.35 * Math.sin(t * 2.1 + phase * 1.7);
  return { angle, drift: angle * RIG.scaleToSway };
}

function gripOf(posture: Posture): Grip {
  switch (posture) {
    case 'held':
      return 'two';
    case 'attention':
      return 'one';
    case 'danger':
    case 'relieved':
      return 'thread';
    case 'fallen':
      return 'none';
  }
}

/** Pure: posture + time → skeleton. Everything that moves is computed here, nothing is drawn. */
export function solvePose(input: PoseInput, t: number): Skeleton {
  const { posture, phase } = input;
  const grip = gripOf(posture);
  const sway = swayAt(input.amplitude ?? swayTokens[posture], phase, t);

  // The body shifts towards the rope it holds with one hand.
  const bx = grip === 'one' ? 5 : 0;
  const shoulderL: Vec2 = [bx - 11, 33];
  const shoulderR: Vec2 = [bx + 11, 33];

  // Hands: on the ropes, on one rope while the other reaches into the void, on a thread, or hanging.
  let handL: Vec2;
  let handR: Vec2;
  switch (grip) {
    case 'two':
      handL = [-14, 0];
      handR = [14, 0];
      break;
    case 'one':
      handL = [bx - 30 + Math.sin(t * 1.4 + phase) * 2, 34 + Math.sin(t * 1.1) * 3];
      handR = [6, 0];
      break;
    case 'thread':
      handL = [-3, 0];
      handR = [3, 0];
      break;
    case 'none':
      handL = [bx - 13, 66 + Math.sin(t * 0.8 + phase)];
      handR = [bx + 13, 66 + Math.sin(t * 0.8 + phase + 1)];
      break;
  }

  const elbowL = solveTwoBone(
    shoulderL,
    handL,
    RIG.upperArm,
    RIG.forearm,
    grip === 'one' || grip === 'none' ? 1 : -1,
  );
  const elbowR = solveTwoBone(shoulderR, handR, RIG.upperArm, RIG.forearm, grip === 'none' ? -1 : 1);

  // Legs: still, or kicking in danger.
  const hipL: Vec2 = [bx - 5, 66];
  const hipR: Vec2 = [bx + 5, 66];
  let kneeL: Vec2 = [bx - 6, 82];
  let kneeR: Vec2 = [bx + 6.5, 82];
  let footL: Vec2 = [bx - 5.5, 98];
  let footR: Vec2 = [bx + 6, 98];
  if (posture === 'danger') {
    const kick = Math.sin(t * 3.2 + phase) * 5;
    kneeL = [bx - 9, 78 - kick * 0.4];
    footL = [bx - 4, 92 + kick * 0.3];
    kneeR = [bx + 8, 80 + kick * 0.4];
    footR = [bx + 11, 95 - kick * 0.3];
  }
  if (grip === 'one') {
    kneeR = [bx + 7, 81];
    footR = [bx + 4, 96];
  }
  if (posture === 'fallen') {
    kneeL = [bx - 5, 82];
    kneeR = [bx + 5, 82];
    footL = [bx - 5, 99];
    footR = [bx + 5, 99];
  }

  return {
    posture,
    grip,
    joints: {
      head: [bx, 20],
      neck: [bx, 31],
      shoulderL,
      shoulderR,
      elbowL,
      elbowR,
      handL,
      handR,
      hipL,
      hipR,
      kneeL,
      kneeR,
      footL,
      footR,
      belt: [bx, 60.2],
    },
    headTilt: grip === 'one' ? -0.18 : posture === 'danger' ? 0.12 : posture === 'fallen' ? 0.28 : 0,
    eyesWide: posture === 'danger',
    eyesClosed: posture === 'fallen',
    mouth: posture === 'danger' ? 'open' : posture === 'relieved' ? 'smile' : 'none',
    sway,
  };
}

/** Where a character is in the scene. The pendulum pivots around the hands (local origin). */
export interface Placement {
  readonly x: number;
  readonly y: number;
  readonly scale: number;
}

/** Local point → scene point, applying sway and scale like the canvas transform does. */
export function toScene(placement: Placement, sway: Sway, local: Vec2): Vec2 {
  const x = local[0] * placement.scale;
  const y = local[1] * placement.scale;
  const cos = Math.cos(sway.angle);
  const sin = Math.sin(sway.angle);
  return [placement.x + sway.drift + x * cos - y * sin, placement.y + x * sin + y * cos];
}
