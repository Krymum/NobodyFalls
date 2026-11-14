import {
  CharacterRenderer,
  CodePartSet,
  easeOutCubic,
  REFERENCE_CAST,
  RopeRenderer,
  SceneRenderer,
  type Anchors,
  type CastId,
  type CastMember,
  type Frame,
} from '@nobodyfalls/render';
import { motion } from '@nobodyfalls/theme';

/**
 * The reference scene (docs/reference/scene-demo.dc.html) rebuilt with the
 * production renderers: you are held by Léa and Kenji, you hold Tom and Ines, and
 * Yuki hangs by a thread until you throw them your last rope.
 */
export interface ReferenceDemo {
  resize(width: number, height: number, pixelRatio: number): void;
  draw(frame: Frame): void;
  /** Throws the rope to Yuki; `t` is the frame time of the click. */
  rescue(t: number, reducedMotion: boolean): void;
}

export function createReferenceDemo(): ReferenceDemo {
  const characters = new CharacterRenderer(new CodePartSet());
  const ropes = new RopeRenderer();
  const scene = new SceneRenderer(characters, ropes);
  let rescuedAt: number | null = null;
  let instant = false;

  const rescueProgress = (t: number): number => {
    if (rescuedAt === null) {
      return 0;
    }
    return instant ? 1 : Math.min(1, (t - rescuedAt) / motion.ropeThrowSeconds);
  };

  return {
    resize(width, height, pixelRatio) {
      scene.prepare(width, height, pixelRatio);
    },

    rescue(t, reducedMotion) {
      rescuedAt ??= t;
      instant = reducedMotion;
    },

    draw({ ctx, t, width }) {
      const unit = width / 390;
      ctx.save();
      scene.drawBackground(ctx, t);
      ctx.restore();

      const progress = rescueProgress(t);
      const cast = REFERENCE_CAST.map((member): CastMember =>
        member.id === 'yuki' && progress >= 1 ? { ...member, posture: 'relieved', amplitude: 0.03 } : member,
      );

      ctx.save();
      ctx.scale(unit, unit);
      const at = new Map<CastId, Anchors>(cast.map((member) => [member.id, characters.anchors(member, t)]));
      const a = (id: CastId): Anchors => {
        const anchors = at.get(id);
        if (!anchors) {
          throw new Error(`Unknown cast member ${id}`);
        }
        return anchors;
      };

      // Ropes first, behind the bodies. Holders above, tied at their belts.
      const lea = a('lea');
      const kenji = a('kenji');
      const me = a('me');
      ropes.rope(ctx, { from: [lea.handL[0] - 8, -20], to: lea.handL, sag: 0, width: 2.6 });
      ropes.rope(ctx, { from: [lea.handR[0] + 6, -20], to: lea.handR, sag: 0, width: 2.6 });
      ropes.rope(ctx, { from: [kenji.handL[0] - 6, -20], to: kenji.handL, sag: 0, width: 2.6 });
      ropes.rope(ctx, { from: [kenji.handR[0] + 9, -20], to: kenji.handR, sag: 0, width: 2.6 });
      ropes.rope(ctx, { from: lea.belt, to: me.handL, sag: 4, width: 3.2 });
      ropes.rope(ctx, { from: kenji.belt, to: me.handR, sag: 4, width: 3.2 });
      ropes.rope(ctx, { from: [-30, 380], to: a('tom').handL, sag: 10, width: 2.8 });
      ropes.rope(ctx, { from: me.belt, to: a('tom').handR, sag: 6, width: 3 });
      ropes.rope(ctx, { from: me.belt, to: a('ines').handR, sag: 3, width: 3 });
      ropes.thread(ctx, [372, -10], a('yuki').grip);
      if (progress > 0) {
        const eased = easeOutCubic(progress);
        ropes.rope(ctx, {
          from: me.belt,
          to: a('yuki').grip,
          sag: 46 - 38 * eased,
          width: 3,
          progress: eased,
        });
      }

      for (const id of ['lea', 'kenji', 'tom', 'ines', 'yuki', 'me'] as const) {
        const member = cast.find((c) => c.id === id);
        if (member) {
          characters.draw(ctx, member, t);
        }
      }

      // Your last free hand: a rope coiled at the hip, until it is thrown.
      if (progress === 0) {
        ropes.coil(ctx, me.belt, t);
      }
      ctx.restore();

      scene.drawAtmosphere(ctx, t);
    },
  };
}
