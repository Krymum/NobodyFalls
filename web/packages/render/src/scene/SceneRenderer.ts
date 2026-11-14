import { atmosphere, character as characterTokens, palette, rgba } from '@nobodyfalls/theme';
import type { CharacterRenderer } from '../CharacterRenderer.ts';
import { DEFAULT_LOOK } from '../look.ts';
import { seededRandom, type Ctx2D } from '../math.ts';
import type { RopeRenderer } from '../RopeRenderer.ts';
import { swayAt, toScene, type Placement, type Sway } from '../rig/skeleton.ts';
import { boxBlur } from './blur.ts';

/** A crowd figure in the far or middle plane: a blurred silhouette hanging from ropes. */
interface Figure extends Placement {
  readonly amplitude: number;
  readonly phase: number;
  /** Where its ropes meet the top of the screen, relative to the figure. */
  readonly ropeLean: number;
}

interface Mote {
  readonly x: number;
  readonly y: number;
  readonly radius: number;
  readonly speed: number;
  readonly phase: number;
  readonly alpha: number;
}

interface Sprite {
  readonly image: CanvasImageSource;
  /** Offset of the figure's pivot (hands) inside the sprite, in scene units. */
  readonly originX: number;
  readonly originY: number;
  readonly width: number;
  readonly height: number;
}

/** An offscreen surface for pre-rendering, and its 2D context (null when unavailable). */
export interface Surface {
  readonly image: CanvasImageSource;
  readonly ctx: Ctx2D | null;
}

/** Creates offscreen surfaces; replaced in tests. */
export interface CanvasFactory {
  create(width: number, height: number): Surface;
}

export const browserCanvases: CanvasFactory = {
  create(width, height) {
    if (typeof OffscreenCanvas !== 'undefined') {
      const canvas = new OffscreenCanvas(width, height);
      return { image: canvas, ctx: canvas.getContext('2d') };
    }
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    return { image: canvas, ctx: canvas.getContext('2d') };
  },
};

/** The reference scene is composed for a 390 × 844 phone screen; everything scales from it. */
export const REFERENCE_WIDTH = 390;
export const REFERENCE_HEIGHT = 844;

/**
 * The world around the characters (ART_DIRECTION, "Univers"): the void's gradient, a
 * cold key light from the top left, far and middle planes of hanging people lost in
 * the haze, drifting haze banks; then, in front, the rising haze of the void, dust in
 * the light and a vignette. Call `drawBackground`, draw the cast, then `drawAtmosphere`.
 */
export class SceneRenderer {
  private far: Figure[] = [];
  private middle: Figure[] = [];
  private dust: Mote[] = [];
  private farSprites: Sprite[] = [];
  private middleSprites: Sprite[] = [];
  private width = REFERENCE_WIDTH;
  private height = REFERENCE_HEIGHT;

  constructor(
    private readonly characters: CharacterRenderer,
    private readonly ropes: RopeRenderer,
    private readonly canvases: CanvasFactory = browserCanvases,
  ) {}

  /**
   * Builds the crowd and pre-renders the blurred silhouettes. Call again when the
   * size or the pixel ratio changes.
   *
   * Figures are sized on the height (ART_DIRECTION, "Responsive"): a wider window shows
   * more of the crowd, never bigger people. The reference 390 × 844 frame is one "tile";
   * wider windows repeat the crowd tile after tile, so a landscape screen fills with
   * distant figures. At the reference size the scene is exactly the reference scene.
   */
  prepare(width: number, height: number, pixelRatio: number, seed = 77): void {
    this.setSize(width, height);
    const unit = this.unit();
    const sy = height / REFERENCE_HEIGHT;
    const tile = REFERENCE_WIDTH * unit;
    const tiles = Math.max(1, Math.ceil(width / tile));
    const random = seededRandom(seed);

    this.far = Array.from({ length: 18 * tiles }, (_, i) => ({
      x: (Math.floor(i / 18) + random()) * tile,
      y: (40 + random() * 620) * sy,
      scale: (0.2 + random() * 0.16) * unit,
      amplitude: 0.02 + random() * 0.03,
      phase: random() * Math.PI * 2,
      ropeLean: (random() * 40 - 20) * unit,
    }));
    const middle = [
      { x: 22, y: 250, scale: 0.46, amplitude: 0.025, phase: 1.2, ropeLean: 10 },
      { x: 360, y: 330, scale: 0.5, amplitude: 0.03, phase: 2.7, ropeLean: -14 },
      { x: 40, y: 690, scale: 0.44, amplitude: 0.02, phase: 4.1, ropeLean: 18 },
      { x: 372, y: 40, scale: 0.42, amplitude: 0.02, phase: 0.4, ropeLean: -6 },
    ];
    // Tiles beyond the first are mirrored and shifted, so the pattern never visibly repeats.
    this.middle = Array.from({ length: tiles }, (_, k) =>
      middle.map((f) => {
        const x = k % 2 === 0 ? f.x : REFERENCE_WIDTH - f.x;
        return {
          ...f,
          x: (k * REFERENCE_WIDTH + x) * unit,
          y: (k === 0 ? f.y : (f.y + 137 * k) % 760) * sy,
          scale: f.scale * unit,
          ropeLean: f.ropeLean * unit,
          phase: f.phase + k,
        };
      }),
    ).flat();
    const area = (width * height) / (REFERENCE_WIDTH * REFERENCE_HEIGHT);
    this.dust = Array.from({ length: Math.min(200, Math.round(60 * Math.max(1, area))) }, () => ({
      x: random() * width,
      y: random() * height,
      radius: 0.4 + random() * 1.1,
      speed: 2 + random() * 6,
      phase: random() * Math.PI * 2,
      alpha: 0.08 + random() * 0.28,
    }));

    this.farSprites = this.far.map((f) =>
      this.silhouette(f.scale, characterTokens.farSilhouette, 1.4, pixelRatio),
    );
    this.middleSprites = this.middle.map((f) =>
      this.silhouette(f.scale, characterTokens.midSilhouette, 0.6, pixelRatio),
    );
  }

  /**
   * Cheap: follows a new window size at once (gradients, light, haze cover it), while
   * the crowd keeps its last layout until `prepare` runs again (debounced by callers).
   */
  setSize(width: number, height: number): void {
    this.width = width;
    this.height = height;
  }

  /** Size of a reference unit on screen: on the height, never wider than the window allows. */
  private unit(): number {
    return Math.min(this.height / REFERENCE_HEIGHT, this.width / REFERENCE_WIDTH);
  }

  drawBackground(ctx: Ctx2D, t: number): void {
    const { width: w, height: h } = this;
    const sx = w / REFERENCE_WIDTH;
    const unit = this.unit();

    const background = ctx.createLinearGradient(0, 0, 0, h);
    background.addColorStop(0, palette.backgroundTop);
    background.addColorStop(0.5, palette.backgroundMiddle);
    background.addColorStop(1, palette.backgroundBottom);
    ctx.fillStyle = background;
    ctx.fillRect(0, 0, w, h);

    // Key light shaft from above left.
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    const shaft = ctx.createLinearGradient(0, 0, 0, h);
    shaft.addColorStop(0, rgba(atmosphere.keyLight, atmosphere.keyLightTop));
    shaft.addColorStop(0.6, rgba(atmosphere.keyLight, atmosphere.keyLightMiddle));
    shaft.addColorStop(1, rgba(atmosphere.keyLight, 0));
    ctx.fillStyle = shaft;
    ctx.beginPath();
    ctx.moveTo(30 * sx, -10);
    ctx.lineTo(170 * sx, -10);
    ctx.lineTo(360 * sx, h);
    ctx.lineTo(-90 * sx, h);
    ctx.closePath();
    ctx.fill();
    ctx.restore();

    // Far plane: tiny figures in the haze, their ropes vanishing upwards.
    ctx.save();
    ctx.strokeStyle = 'rgba(40,50,62,0.7)';
    ctx.lineWidth = 0.7;
    this.far.forEach((figure, i) => {
      const sway = swayAt(figure.amplitude, figure.phase, t);
      const [hx, hy] = toScene(figure, sway, [0, 0]);
      ctx.beginPath();
      ctx.moveTo(hx - 3, hy);
      ctx.lineTo(hx - 3 + figure.ropeLean * 0.3, -10);
      ctx.moveTo(hx + 3, hy);
      ctx.lineTo(hx + 3 + figure.ropeLean * 0.3, -10);
      ctx.stroke();
      this.drawSprite(ctx, this.farSprites[i], figure, sway);
    });
    ctx.restore();
    ctx.fillStyle = atmosphere.farVeil;
    ctx.fillRect(0, 0, w, h);

    // Middle plane.
    this.middle.forEach((figure, i) => {
      const sway = swayAt(figure.amplitude, figure.phase, t);
      const left = toScene(figure, sway, [-14, 0]);
      const right = toScene(figure, sway, [14, 0]);
      this.ropes.rope(ctx, {
        from: [left[0] - 4 + figure.ropeLean, -20],
        to: left,
        sag: 0,
        width: 2.2,
        opacity: 0.45,
      });
      this.ropes.rope(ctx, {
        from: [right[0] + 4 + figure.ropeLean, -20],
        to: right,
        sag: 0,
        width: 2.2,
        opacity: 0.45,
      });
      this.drawSprite(ctx, this.middleSprites[i], figure, sway);
    });

    // Haze banks drifting slowly, repeated across wide windows.
    const tile = REFERENCE_WIDTH * unit;
    for (let k = 0; k * tile < w; k++) {
      for (const [x, y, phase] of [
        [80, 240, 0],
        [300, 470, 2],
        [150, 700, 4],
      ] as const) {
        const cx = k * tile + (x + Math.sin(t * 0.07 + phase + k) * 40) * unit;
        const cy = y * (h / REFERENCE_HEIGHT);
        const haze = ctx.createRadialGradient(cx, cy, 10 * unit, cx, cy, 230 * unit);
        haze.addColorStop(0, rgba(atmosphere.haze, atmosphere.hazeAlpha));
        haze.addColorStop(1, rgba(atmosphere.haze, 0));
        ctx.fillStyle = haze;
        ctx.fillRect(0, 0, w, h);
      }
    }
  }

  drawAtmosphere(ctx: Ctx2D, t: number): void {
    const { width: w, height: h } = this;

    // The void: cold haze rising from the bottom.
    const voidTop = h * (560 / REFERENCE_HEIGHT);
    const rising = ctx.createLinearGradient(0, voidTop, 0, h);
    rising.addColorStop(0, rgba(atmosphere.voidHaze, 0));
    rising.addColorStop(1, rgba(atmosphere.voidHaze, atmosphere.voidHazeAlpha));
    ctx.fillStyle = rising;
    ctx.fillRect(0, voidTop, w, h - voidTop);

    // Dust drifting in the light.
    for (const mote of this.dust) {
      const y = (((mote.y - t * mote.speed) % h) + h) % h;
      const x = mote.x + Math.sin(t * 0.4 + mote.phase) * 6;
      ctx.fillStyle = rgba(atmosphere.dust, mote.alpha * (0.6 + 0.4 * Math.sin(t + mote.phase)));
      ctx.beginPath();
      ctx.arc(x, y, mote.radius, 0, Math.PI * 2);
      ctx.fill();
    }

    // Sized on the diagonal, so that it frames portrait and landscape windows alike
    // (equal to the reference vignette at 390 × 844).
    const half = Math.hypot(w, h) / 2;
    const vignette = ctx.createRadialGradient(w / 2, h * 0.43, half * 0.344, w / 2, h * 0.47, half * 1.292);
    vignette.addColorStop(0, 'rgba(0,0,0,0)');
    vignette.addColorStop(1, `rgba(0,0,0,${atmosphere.vignetteAlpha})`);
    ctx.fillStyle = vignette;
    ctx.fillRect(0, 0, w, h);
  }

  /** A figure's silhouette, drawn once at its scale, then blurred. */
  private silhouette(scale: number, color: string, blur: number, pixelRatio: number): Sprite {
    // Local bounds of a hanging character, plus room for the blur.
    const pad = Math.ceil(blur * 3) + 2;
    const left = -36 * scale - pad;
    const top = -26 * scale - pad;
    const width = 72 * scale + pad * 2;
    const height = 132 * scale + pad * 2;
    const pixelWidth = Math.ceil(width * pixelRatio);
    const pixelHeight = Math.ceil(height * pixelRatio);
    const { image: canvas, ctx } = this.canvases.create(pixelWidth, pixelHeight);
    if (ctx) {
      ctx.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);
      this.characters.draw(
        ctx,
        {
          x: -left,
          y: -top,
          scale,
          posture: 'held',
          phase: 0,
          amplitude: 0,
          look: DEFAULT_LOOK,
          mode: { kind: 'silhouette', color },
        },
        0,
      );
      const image = ctx.getImageData(0, 0, pixelWidth, pixelHeight);
      boxBlur(image.data, pixelWidth, pixelHeight, blur * pixelRatio);
      ctx.putImageData(image, 0, 0);
    }
    return { image: canvas, originX: -left, originY: -top, width, height };
  }

  private drawSprite(ctx: Ctx2D, sprite: Sprite | undefined, figure: Figure, sway: Sway): void {
    if (!sprite) {
      return;
    }
    ctx.save();
    ctx.translate(figure.x + sway.drift, figure.y);
    ctx.rotate(sway.angle);
    ctx.drawImage(sprite.image, -sprite.originX, -sprite.originY, sprite.width, sprite.height);
    ctx.restore();
  }
}
