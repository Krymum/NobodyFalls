import type { LayoutKind } from '@nobodyfalls/layout';
import { layout, panelWidth } from '@nobodyfalls/theme';
import { SCENE_HEIGHT, SCENE_WIDTH } from './neighbourhood.ts';

/**
 * Where the composition (390 × 844 units, the reference phone) lands on the screen:
 * `screen = offset + composition × scale`. One scale for both axes: nothing is ever
 * stretched (ART_DIRECTION, "Responsive").
 */
export interface Camera {
  readonly scale: number;
  readonly offsetX: number;
  readonly offsetY: number;
}

/** Parts of the window covered by the interface, in CSS pixels. */
export interface Insets {
  readonly left: number;
  readonly right: number;
  readonly top: number;
  readonly bottom: number;
}

/**
 * What the camera must keep in view, in composition units. `full` is the whole
 * composition, whose bottom leaves room for the text panel of phones and tablets.
 * `content` is where the characters actually are: on desktop the text moves to a
 * side panel, so the camera frames the people and they grow with the height.
 */
export const FOCUS = {
  full: { top: 0, bottom: SCENE_HEIGHT },
  content: { top: 30, bottom: 600 },
} as const;

export type Focus = keyof typeof FOCUS;

/** Characters stay comfortable: never smaller than this, never larger (× the reference). */
export const MIN_SCALE = 0.7;
export const MAX_SCALE = 1.45;

/**
 * Frames the composition in the free part of the window: sized on the height (the
 * characters keep a comfortable size whatever the width), never wider than the free
 * width, bounded, and centred. In landscape, the room left on the sides shows more of
 * the void and the crowd, which the background fills.
 */
export function frame(width: number, height: number, insets: Insets, focus: Focus): Camera {
  const box = FOCUS[focus];
  const freeWidth = Math.max(1, width - insets.left - insets.right);
  const freeHeight = Math.max(1, height - insets.top - insets.bottom);
  const fit = Math.min(freeHeight / (box.bottom - box.top), freeWidth / SCENE_WIDTH);
  const scale = Math.min(MAX_SCALE, Math.max(MIN_SCALE, fit));
  return {
    scale,
    offsetX: insets.left + (freeWidth - SCENE_WIDTH * scale) / 2,
    offsetY: insets.top + (freeHeight - (box.bottom - box.top) * scale) / 2 - box.top * scale,
  };
}

/** How the scene is shown: in full on "Tonight", dimmed behind list screens. */
export type SceneMode = 'full' | 'dimmed';

/** The interface around the scene for a layout (same sizes as the CSS, from the theme tokens). */
export function sceneInsets(
  kind: LayoutKind,
  mode: SceneMode,
  width: number,
): { insets: Insets; focus: Focus } {
  if (kind !== 'desktop') {
    return { insets: { left: 0, right: 0, top: 0, bottom: 0 }, focus: 'full' };
  }
  return {
    insets: { left: layout.railWidth, right: mode === 'full' ? panelWidth(width) : 0, top: 0, bottom: 0 },
    focus: 'content',
  };
}

/** A composition point on the screen. */
export function project(camera: Camera, x: number, y: number): [number, number] {
  return [camera.offsetX + x * camera.scale, camera.offsetY + y * camera.scale];
}
