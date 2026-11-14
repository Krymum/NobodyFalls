import type { Look } from '../look.ts';
import type { Ctx2D } from '../math.ts';
import type { Skeleton } from '../rig/skeleton.ts';

/**
 * Layers of a character, back to front. A clothing item draws inside the layer of
 * its slot (headwear in `head`, gloves in `arms`…).
 */
export const LAYERS = ['legs', 'back', 'torso', 'head', 'scarf', 'arms'] as const;

export type LayerId = (typeof LAYERS)[number];

/**
 * `full`: lit volumes, face, clothes. `silhouette`: a flat shape in one colour, for
 * the far and middle planes of the scene.
 */
export type PartMode = { readonly kind: 'full' } | { readonly kind: 'silhouette'; readonly color: string };

/**
 * The only thing that changes between the code-drawn prototype and illustrated
 * sprites (ART_DIRECTION, "Contraintes techniques"): given a skeleton and a look,
 * draw one layer in the character's local space (the context is already placed,
 * swayed and scaled). An `AtlasPartSet` will draw sprite parts pinned to the same
 * joints, with lighting baked in.
 */
export interface PartSet {
  readonly layers: readonly LayerId[];
  draw(ctx: Ctx2D, layer: LayerId, skeleton: Skeleton, look: Look, mode: PartMode): void;
}
