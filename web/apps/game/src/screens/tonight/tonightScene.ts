import {
  CharacterRenderer,
  CodePartSet,
  RopeRenderer,
  SceneRenderer,
  throwAt,
  type Anchors,
  type Frame,
  type Vec2,
} from '@nobodyfalls/render';
import { frame as frameCamera, type Camera, type Focus, type Insets } from '../../core/camera.ts';
import type { Link, Neighbourhood, Person } from '../../core/neighbourhood.ts';
import type { Throw } from '../../core/store.ts';

/**
 * The Tonight scene: my neighbourhood drawn like the reference scene, over the whole
 * window. The composition (390 × 844) is projected by the camera: one uniform scale
 * sized on the height, centred in the part of the window the interface leaves free.
 */
export interface TonightScene {
  resize(width: number, height: number, pixelRatio: number): void;
  /** What the interface covers, and what the camera must keep in view. */
  view(insets: Insets, focus: Focus): void;
  /** `null`: the background alone (onboarding, loading). */
  show(neighbourhood: Neighbourhood | null, lastThrow: Throw | null, reducedMotion: boolean): void;
  draw(frame: Frame): void;
  /** Stops pending work (the debounced crowd rebuild). */
  dispose(): void;
}

/** Rebuilding the blurred crowd waits until the window stops changing size. */
const REBUILD_DELAY_MS = 150;

export function createTonightScene(): TonightScene {
  const characters = new CharacterRenderer(new CodePartSet());
  const ropes = new RopeRenderer();
  const scene = new SceneRenderer(characters, ropes);
  let current: Neighbourhood | null = null;
  let thrown: Throw | null = null;
  let instant = false;
  let insets: Insets = { left: 0, right: 0, top: 0, bottom: 0 };
  let focus: Focus = 'full';
  let prepared = false;
  let rebuild: ReturnType<typeof setTimeout> | undefined;

  const place = (person: Person, camera: Camera): Person => ({
    ...person,
    x: camera.offsetX + person.x * camera.scale,
    y: camera.offsetY + person.y * camera.scale,
    scale: person.scale * camera.scale,
  });

  const handOf = (anchors: Anchors, person: Person, link: Link): Vec2 => {
    // People hanging on a thread or catching a rope hold it with both hands together.
    if (link.hand === 'grip' || person.posture === 'danger' || person.posture === 'relieved') {
      return anchors.grip;
    }
    return link.hand === 'left' ? anchors.handL : anchors.handR;
  };

  /** 0..1 progress of the rope I just threw to `id`, or 1 when it is not being thrown. */
  const throwProgress = (id: string): number => {
    if (!thrown || thrown.to !== id || instant) {
      return 1;
    }
    return throwAt((performance.now() - thrown.at) / 1000).progress;
  };

  return {
    resize(width, height, pixelRatio) {
      // The first layout is immediate; while a window is being resized, the size follows
      // at once and the crowd is rebuilt once it settles (no flicker, no wasted blurs).
      clearTimeout(rebuild);
      if (!prepared) {
        prepared = true;
        scene.prepare(width, height, pixelRatio);
        return;
      }
      scene.setSize(width, height);
      rebuild = setTimeout(() => {
        scene.prepare(width, height, pixelRatio);
      }, REBUILD_DELAY_MS);
    },

    view(nextInsets, nextFocus) {
      insets = nextInsets;
      focus = nextFocus;
    },

    dispose() {
      clearTimeout(rebuild);
    },

    show(neighbourhood, lastThrow, reducedMotion) {
      current = neighbourhood;
      thrown = lastThrow;
      instant = reducedMotion;
    },

    draw({ ctx, t, width, height }) {
      scene.drawBackground(ctx, t);
      if (!current) {
        scene.drawAtmosphere(ctx, t);
        return;
      }

      const camera = frameCamera(width, height, insets, focus);
      const unit = camera.scale;
      const people = current.people.map((p) => place(p, camera));
      const byId = new Map(people.map((p) => [p.id, p]));
      const anchors = new Map(people.map((p) => [p.id, characters.anchors({ ...p, opacity: 1 }, t)]));
      const me = people.find((p) => p.role === 'me');
      const myAnchors = me ? anchors.get(me.id) : undefined;

      // Ropes first, behind the bodies.
      for (const link of current.links) {
        const receiver = byId.get(link.to);
        const receiverAnchors = anchors.get(link.to);
        if (!receiver || !receiverAnchors) {
          continue;
        }
        const end = handOf(receiverAnchors, receiver, link);
        if (link.from === 'sky') {
          const lean = link.hand === 'left' ? -8 : 8;
          if (link.hand === 'grip') {
            ropes.thread(ctx, [end[0] + 40 * unit, -10], end);
          } else {
            ropes.rope(ctx, { from: [end[0] + lean * unit, -20], to: end, sag: 0, width: 2.6 * unit });
          }
          continue;
        }
        const giver = anchors.get(link.from);
        if (!giver) {
          continue;
        }
        const progress = throwProgress(link.to);
        const eased = Math.min(1, progress);
        ropes.rope(ctx, {
          from: giver.belt,
          to: end,
          sag: (receiver.role === 'held' ? 6 : 4) * unit + (1 - eased) * 38 * unit,
          width: 3 * unit,
          progress: eased,
        });
      }

      // Someone on the Edge, hanging by a frayed thread; or catching the rope I just threw.
      const candidate = current.candidate ? byId.get(current.candidate.id) : undefined;
      const candidateAnchors = candidate ? anchors.get(candidate.id) : undefined;
      if (candidate && candidateAnchors) {
        ropes.thread(ctx, [candidateAnchors.grip[0] + 54 * unit, -10], candidateAnchors.grip);
        if (thrown?.to === candidate.id && myAnchors) {
          const eased = throwProgress(candidate.id);
          ropes.rope(ctx, {
            from: myAnchors.belt,
            to: candidateAnchors.grip,
            sag: (46 - 38 * eased) * unit,
            width: 3 * unit,
            progress: eased,
          });
        }
      }

      const order = ['holder', 'held', 'candidate', 'me'] as const;
      for (const role of order) {
        for (const person of people.filter((p) => p.role === role)) {
          const caught =
            person.role === 'candidate' && thrown?.to === person.id && throwProgress(person.id) >= 1;
          characters.draw(
            ctx,
            {
              ...person,
              posture: caught ? 'relieved' : person.posture,
              opacity: person.posture === 'fallen' ? 0.55 : 1,
            },
            t,
          );
        }
      }

      // Hands I can still give: a rope coiled at my hip.
      if (myAnchors && current.freeHands > 0 && !(thrown && throwProgress(thrown.to) < 1)) {
        ropes.coil(ctx, myAnchors.belt, t, unit);
      }

      scene.drawAtmosphere(ctx, t);
    },
  };
}
