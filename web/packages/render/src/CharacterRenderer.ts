import type { Look } from './look.ts';
import type { Ctx2D, Vec2 } from './math.ts';
import type { PartMode, PartSet } from './parts/PartSet.ts';
import { solvePose, toScene, type Placement, type Posture, type Skeleton } from './rig/skeleton.ts';

/** A character to draw: data only. */
export interface CharacterInstance extends Placement {
  readonly posture: Posture;
  readonly look: Look;
  /** Phase of the pendulum, so that characters do not swing in unison. */
  readonly phase: number;
  /** Overrides the posture's sway amplitude (a rescue calming down, for instance). */
  readonly amplitude?: number;
  readonly mode?: PartMode;
  /** Fades the character (fallen players sinking into the haze). */
  readonly opacity?: number;
}

/** Scene positions where ropes attach, after sway. */
export interface Anchors {
  readonly skeleton: Skeleton;
  readonly handL: Vec2;
  readonly handR: Vec2;
  /** Between the hands: where a single rope or thread is held. */
  readonly grip: Vec2;
  /** Metal ring at the belt, where a holder's rope is tied. */
  readonly belt: Vec2;
}

const FULL: PartMode = { kind: 'full' };

/**
 * Draws characters from data (ART_DIRECTION, "Contraintes techniques"): solves the
 * pose, places the character (position, pendulum, scale) and lets the part set draw
 * each layer. No game logic.
 */
export class CharacterRenderer {
  constructor(private readonly parts: PartSet) {}

  /** Where ropes attach, without drawing (to draw ropes behind the bodies first). */
  anchors(character: CharacterInstance, t: number): Anchors {
    const skeleton = solvePose(
      character.amplitude === undefined
        ? { posture: character.posture, phase: character.phase }
        : { posture: character.posture, phase: character.phase, amplitude: character.amplitude },
      t,
    );
    const at = (local: Vec2): Vec2 => toScene(character, skeleton.sway, local);
    return {
      skeleton,
      handL: at(skeleton.joints.handL),
      handR: at(skeleton.joints.handR),
      grip: at([0, 0]),
      belt: at(skeleton.joints.belt),
    };
  }

  draw(ctx: Ctx2D, character: CharacterInstance, t: number): Anchors {
    const anchors = this.anchors(character, t);
    const { skeleton } = anchors;
    ctx.save();
    if (character.opacity !== undefined) {
      ctx.globalAlpha *= character.opacity;
    }
    ctx.translate(character.x + skeleton.sway.drift, character.y);
    ctx.rotate(skeleton.sway.angle);
    ctx.scale(character.scale, character.scale);
    for (const layer of this.parts.layers) {
      this.parts.draw(ctx, layer, skeleton, character.look, character.mode ?? FULL);
    }
    ctx.restore();
    return anchors;
  }
}
