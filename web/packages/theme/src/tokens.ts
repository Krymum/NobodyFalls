/**
 * Design tokens (docs/ART_DIRECTION.md, "Palette"). Every visual goes through them,
 * so the art direction can change without touching game logic. Values match the
 * validated reference scene (docs/reference/scene-demo.dc.html).
 */

export const palette = {
  /** The void: background gradient, top to bottom. */
  backgroundTop: '#0E141D',
  backgroundMiddle: '#090D13',
  backgroundBottom: '#030405',
  /** Page colour behind the canvas and under the bottom text fade. */
  page: '#05070A',

  /** Main text ("bone"). */
  bone: '#E4E1DA',
  textSecondary: '#A3ACB6',
  label: '#7F8B98',
  header: '#8E99A6',

  /** Primary action: bone on near-black. */
  action: '#D9D3C7',
  onAction: '#0B0E13',
  outline: '#4A535E',

  /** Status colours are reserved for statuses: no clothing may use them. */
  attention: '#D8A85A',
  danger: '#D0645F',
} as const;

/** Atmosphere: the only things computed at run time (ART_DIRECTION, "Contraintes techniques"). */
export const atmosphere = {
  /** Cold key light from the top left. */
  keyLight: [140, 165, 195] as const,
  keyLightTop: 0.13,
  keyLightMiddle: 0.035,
  haze: [110, 130, 155] as const,
  hazeAlpha: 0.075,
  dust: [200, 212, 226] as const,
  /** Rising cold haze at the bottom: the void. */
  voidHaze: [60, 75, 95] as const,
  voidHazeAlpha: 0.22,
  vignetteAlpha: 0.72,
  /** Darkening laid over the far layer to push it back. */
  farVeil: 'rgba(10,15,22,0.38)',
} as const;

export const rope = {
  body: '#6B5E4F',
  shadow: '#1E1915',
  highlight: '#B3A48A',
  /** The frayed thread of a player in danger. */
  thread: 'rgba(178,170,155,0.7)',
} as const;

export const character = {
  eye: '#0A0B0D',
  eyeGlint: 'rgba(235,240,245,0.75)',
  cheek: 'rgba(150,80,80,0.18)',
  neckShadow: 'rgba(0,0,0,0.45)',
  rimLight: 'rgba(210,225,240,0.42)',
  torsoRim: 'rgba(205,220,235,0.35)',
  handGlint: 'rgba(210,225,240,0.25)',
  bootGlint: 'rgba(190,205,220,0.18)',
  belt: '#17191D',
  buckle: '#8A8F96',
  /** Silhouettes of the far and middle planes. */
  farSilhouette: '#1F2731',
  midSilhouette: '#28313D',
} as const;

/** Pendulum amplitude (radians) by posture: calm when held, wild in danger. */
export const sway = {
  held: 0.02,
  attention: 0.035,
  danger: 0.06,
  relieved: 0.03,
  fallen: 0.012,
} as const;

export const motion = {
  /** A thrown rope unrolls in about 0.8 s, then tightens. */
  ropeThrowSeconds: 0.85,
  /** A fall: the character slides into the haze. */
  fallSeconds: 2.4,
  /** Devices are capped at 2× to keep 60 fps on mid-range phones. */
  maxDevicePixelRatio: 2,
} as const;

export const typography = {
  display: "'Barlow Condensed', 'Arial Narrow', sans-serif",
  body: "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif",
  titleWeight: 600,
  labelWeight: 500,
  labelTracking: '0.18em',
  headerTracking: '0.32em',
} as const;

export const space = {
  gutter: '24px',
  gap: '10px',
  radius: '2px',
  buttonHeight: '52px',
  secondaryHeight: '44px',
} as const;

/**
 * Responsive layout (ART_DIRECTION, "Responsive"): breakpoints on the real width, never
 * the user agent. Below `tablet` is a phone; from `desktop` on, navigation is a rail.
 */
export const breakpoints = {
  tablet: 640,
  desktop: 1024,
} as const;

/** Sizes of the shared layout, in CSS pixels (ADR 0016). */
export const layout = {
  /** Vertical navigation rail on desktop. */
  railWidth: 88,
  /** Side information panel on desktop: clamp(panelMin, panelViewport × width, panelMax). */
  panelMin: 360,
  panelMax: 420,
  panelViewport: 0.3,
  /** Reading column of list screens. */
  columnTablet: 640,
  columnMax: 760,
  /** Bottom text panel on tablets. */
  panelTablet: 560,
} as const;

/**
 * Fluid type and spacing (`clamp()`): at 390 px wide they equal the phone sizes the
 * screens were designed with; they grow up to the desktop sizes.
 */
export const fluid = {
  title: 'clamp(38px, 3vw + 35px, 64px)',
  heading: 'clamp(36px, 1.6vw + 34px, 56px)',
  number: 'clamp(44px, 1.6vw + 42px, 64px)',
  text: 'clamp(15px, 0.2vw + 14.2px, 17px)',
  label: 'clamp(13px, 0.1vw + 12.6px, 14px)',
  gutter: 'clamp(24px, 2vw + 16px, 40px)',
  section: 'clamp(28px, 2vw + 20px, 44px)',
} as const;

/** Panel width at a given window width (the same formula as the CSS clamp). */
export function panelWidth(windowWidth: number): number {
  return Math.min(layout.panelMax, Math.max(layout.panelMin, windowWidth * layout.panelViewport));
}

export const theme = {
  palette,
  atmosphere,
  rope,
  character,
  sway,
  motion,
  typography,
  space,
  breakpoints,
  layout,
  fluid,
} as const;

export type Theme = typeof theme;

/** `[r, g, b]` and an alpha to a CSS colour. */
export function rgba(rgb: readonly [number, number, number], alpha: number): string {
  return `rgba(${rgb[0]},${rgb[1]},${rgb[2]},${alpha.toFixed(3)})`;
}
