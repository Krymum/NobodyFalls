import { character as tokens } from '@nobodyfalls/theme';
import type { Look } from '../look.ts';
import { shade, TAU, type Ctx2D, type Vec2 } from '../math.ts';
import { RIG, type Skeleton } from '../rig/skeleton.ts';
import { LAYERS, type LayerId, type PartMode, type PartSet } from './PartSet.ts';

/**
 * Parts drawn in code, reproducing the validated reference scene
 * (docs/reference/scene-demo.dc.html): lit face, shadow side, rim light from the
 * top left, occlusion at the neck. Coordinates are in the character's local space.
 */
export class CodePartSet implements PartSet {
  readonly layers = LAYERS;

  draw(ctx: Ctx2D, layer: LayerId, skeleton: Skeleton, look: Look, mode: PartMode): void {
    const flat = mode.kind === 'silhouette' ? mode.color : null;
    switch (layer) {
      case 'legs':
        drawLegs(ctx, skeleton, look, flat);
        break;
      case 'back':
        if (!flat) {
          drawBack(ctx, skeleton, look);
        }
        break;
      case 'torso':
        drawTorso(ctx, skeleton, look, flat);
        break;
      case 'head':
        drawHead(ctx, skeleton, look, flat);
        break;
      case 'scarf':
        if (!flat && look.scarf) {
          drawScarf(ctx, skeleton, look.scarf.color);
        }
        break;
      case 'arms':
        drawArms(ctx, skeleton, look, flat);
        break;
    }
  }
}

/** A limb as a stroked path with volume: shadow below, base, highlight on the lit side. */
function limb(ctx: Ctx2D, points: readonly Vec2[], width: number, base: string, flat: string | null): void {
  const path = (): void => {
    ctx.beginPath();
    points.forEach(([x, y], i) => {
      if (i === 0) {
        ctx.moveTo(x, y);
      } else {
        ctx.lineTo(x, y);
      }
    });
  };
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';
  if (flat) {
    ctx.strokeStyle = flat;
    ctx.lineWidth = width;
    path();
    ctx.stroke();
    return;
  }

  ctx.save();
  ctx.translate(width * 0.18, width * 0.12);
  ctx.strokeStyle = shade(base, -0.55);
  ctx.lineWidth = width;
  path();
  ctx.stroke();
  ctx.restore();

  ctx.strokeStyle = base;
  ctx.lineWidth = width * 0.86;
  path();
  ctx.stroke();

  ctx.save();
  ctx.globalAlpha = 0.45;
  ctx.translate(-width * 0.2, -width * 0.12);
  ctx.strokeStyle = shade(base, 0.35);
  ctx.lineWidth = width * 0.28;
  path();
  ctx.stroke();
  ctx.restore();
}

function ellipse(
  ctx: Ctx2D,
  x: number,
  y: number,
  rx: number,
  ry: number,
  rotation = 0,
  start = 0,
  end = TAU,
): void {
  ctx.beginPath();
  ctx.ellipse(x, y, rx, ry, rotation, start, end);
}

function drawLegs(ctx: Ctx2D, s: Skeleton, look: Look, flat: string | null): void {
  const j = s.joints;
  limb(ctx, [j.hipL, j.kneeL, j.footL], 7.5, look.trousers, flat);
  limb(ctx, [j.hipR, j.kneeR, j.footR], 7.5, look.trousers, flat);
  for (const [x, y] of [j.footL, j.footR]) {
    ctx.fillStyle = flat ?? look.boots;
    ellipse(ctx, x + 0.5, y + 2, 4.6, 3.6, 0.15);
    ctx.fill();
    if (!flat) {
      ctx.fillStyle = tokens.bootGlint;
      ellipse(ctx, x - 1.2, y + 0.8, 2, 1.2);
      ctx.fill();
    }
  }
}

/** What hangs behind the head: a raised hood, long hair, a lowered hood on a hooded coat. */
function drawBack(ctx: Ctx2D, s: Skeleton, look: Look): void {
  const [bx] = s.joints.head;
  if (look.headwear?.style === 'hood') {
    ctx.fillStyle = shade(look.headwear.color, -0.25);
    ellipse(ctx, bx, 19, 15.5, 17);
    ctx.fill();
  } else if (look.coat.style === 'hooded') {
    ctx.fillStyle = shade(look.coat.color, -0.3);
    ellipse(ctx, bx, 30, 12.5, 6.5);
    ctx.fill();
  }

  if (look.hair?.style === 'long') {
    ctx.fillStyle = look.hair.color;
    ctx.beginPath();
    ctx.moveTo(bx - 13, 18);
    ctx.quadraticCurveTo(bx - 15, 40, bx - 10, 44);
    ctx.lineTo(bx + 10, 44);
    ctx.quadraticCurveTo(bx + 15, 40, bx + 13, 18);
    ctx.closePath();
    ctx.fill();
  }
}

function drawTorso(ctx: Ctx2D, s: Skeleton, look: Look, flat: string | null): void {
  const [bx] = s.joints.neck;
  const coat = look.coat.color;
  // An oversized coat flares a little more at the hem.
  const flare = look.coat.style === 'oilskin' ? 1.5 : 0;

  ctx.beginPath();
  ctx.moveTo(bx - 12, 31);
  ctx.quadraticCurveTo(bx, 27.5, bx + 12, 31);
  ctx.quadraticCurveTo(bx + 14.5 + flare, 50, bx + 14 + flare, 68);
  ctx.quadraticCurveTo(bx, 71, bx - 14 - flare, 68);
  ctx.quadraticCurveTo(bx - 14.5 - flare, 50, bx - 12, 31);
  ctx.closePath();
  if (flat) {
    ctx.fillStyle = flat;
  } else {
    const gradient = ctx.createLinearGradient(bx - 14, 30, bx + 14, 60);
    gradient.addColorStop(0, shade(coat, 0.22));
    gradient.addColorStop(0.45, coat);
    gradient.addColorStop(1, shade(coat, -0.6));
    ctx.fillStyle = gradient;
  }
  ctx.fill();
  if (flat) {
    return;
  }

  // Seam, rim light on the lit side, belt and its metal ring (where ropes are tied).
  ctx.strokeStyle = shade(coat, -0.5);
  ctx.lineWidth = 0.8;
  ctx.beginPath();
  ctx.moveTo(bx + 1, 34);
  ctx.lineTo(bx + 1.5, 69);
  ctx.stroke();

  ctx.strokeStyle = tokens.torsoRim;
  ctx.lineWidth = 1.1;
  ctx.beginPath();
  ctx.moveTo(bx - 12, 32);
  ctx.quadraticCurveTo(bx - 14.5, 50, bx - 14, 67);
  ctx.stroke();

  const [, beltY] = s.joints.belt;
  ctx.fillStyle = tokens.belt;
  ctx.fillRect(bx - 13.5, beltY - 2.2, 27, 4.2);
  ctx.fillStyle = tokens.buckle;
  ellipse(ctx, bx, beltY, 2.3, 2.3);
  ctx.fill();
  ctx.fillStyle = tokens.belt;
  ellipse(ctx, bx, beltY, 1.1, 1.1);
  ctx.fill();
}

function drawHead(ctx: Ctx2D, s: Skeleton, look: Look, flat: string | null): void {
  const [hx, hy] = s.joints.head;
  if (!flat) {
    ctx.fillStyle = tokens.neckShadow;
    ellipse(ctx, hx, 31, 7, 3);
    ctx.fill();
  }

  ctx.save();
  ctx.translate(hx, hy);
  ctx.rotate(s.headTilt);

  if (flat) {
    ctx.fillStyle = flat;
    ellipse(ctx, 0, 0, RIG.headRadiusX, RIG.headRadiusY);
    ctx.fill();
    ctx.restore();
    return;
  }

  const skin = look.skin;
  const gradient = ctx.createRadialGradient(-4, -5, 2, 0, 0, 14);
  gradient.addColorStop(0, shade(skin, 0.12));
  gradient.addColorStop(0.55, skin);
  gradient.addColorStop(1, shade(skin, -0.62));
  ctx.fillStyle = gradient;
  ellipse(ctx, 0, 0, RIG.headRadiusX, RIG.headRadiusY);
  ctx.fill();

  drawFace(ctx, s);
  drawHeadwear(ctx, look);

  // Rim light: the key light comes from the top left.
  ctx.strokeStyle = tokens.rimLight;
  ctx.lineWidth = 1.2;
  ellipse(ctx, 0, 0, 11.6, 12.9, 0, Math.PI * 0.95, Math.PI * 1.55);
  ctx.stroke();
  ctx.restore();
}

/** Small black eyes with a glint, no mouth unless panicking or relieved, faint cheeks. */
function drawFace(ctx: Ctx2D, s: Skeleton): void {
  ctx.fillStyle = tokens.eye;
  if (s.eyesClosed) {
    ctx.strokeStyle = tokens.eye;
    ctx.lineWidth = 0.9;
    for (const x of [-4.2, 4.2]) {
      ctx.beginPath();
      ctx.moveTo(x - 1.6, 2);
      ctx.lineTo(x + 1.6, 2);
      ctx.stroke();
    }
  } else {
    const rx = s.eyesWide ? 1.9 : 1.5;
    const ry = s.eyesWide ? 2.5 : 2.0;
    for (const x of [-4.2, 4.2]) {
      ellipse(ctx, x, 1.5, rx, ry);
      ctx.fill();
    }
    ctx.fillStyle = tokens.eyeGlint;
    for (const x of [-4.7, 3.7]) {
      ellipse(ctx, x, 0.6, 0.45, 0.45);
      ctx.fill();
    }
  }

  ctx.fillStyle = tokens.cheek;
  for (const x of [-6.5, 6.5]) {
    ellipse(ctx, x, 5.5, 2.4, 1.4);
    ctx.fill();
  }

  if (s.mouth === 'open') {
    ctx.fillStyle = tokens.eye;
    ellipse(ctx, 0, 7.2, 1.3, 1.7);
    ctx.fill();
  } else if (s.mouth === 'smile') {
    ctx.strokeStyle = tokens.eye;
    ctx.lineWidth = 0.9;
    ctx.beginPath();
    ctx.arc(0, 5.6, 2, 0.25, Math.PI - 0.25);
    ctx.stroke();
  }
}

function drawHeadwear(ctx: Ctx2D, look: Look): void {
  const headwear = look.headwear;
  if (headwear?.style === 'beanie') {
    ctx.fillStyle = headwear.color;
    ellipse(ctx, 0, -4, 12.2, 10.5, 0, Math.PI, 0);
    ctx.fill();
    ctx.fillStyle = shade(headwear.color, -0.3);
    ctx.fillRect(-12.4, -5.5, 24.8, 4.2);
    ctx.fillStyle = 'rgba(205,220,235,0.25)';
    ellipse(ctx, -4, -11, 4, 2, -0.3);
    ctx.fill();
    return;
  }
  if (headwear?.style === 'hood') {
    ctx.strokeStyle = shade(headwear.color, -0.05);
    ctx.lineWidth = 3.2;
    ellipse(ctx, 0, 0.5, 13, 14, 0, Math.PI * 1.05, Math.PI * 1.95);
    ctx.stroke();
    return;
  }
  if (headwear?.style === 'brim' || headwear?.style === 'wide-brim') {
    const brim = headwear.style === 'wide-brim' ? 19 : 16;
    ctx.fillStyle = headwear.color;
    ellipse(ctx, 0, -5, brim, 3.4);
    ctx.fill();
    ellipse(ctx, 0, -8, 9.5, 7, 0, Math.PI, 0);
    ctx.fill();
    ctx.fillStyle = 'rgba(0,0,0,0.35)';
    ellipse(ctx, 0, -2.5, 11, 2.6, 0, 0, Math.PI);
    ctx.fill();
    return;
  }

  const hair = look.hair;
  if (hair?.style === 'short') {
    ctx.fillStyle = hair.color;
    ctx.beginPath();
    ctx.moveTo(-12, -1);
    ctx.quadraticCurveTo(-12, -14, 0, -13.5);
    ctx.quadraticCurveTo(12, -14, 12, -1);
    ctx.quadraticCurveTo(6, -7, 1, -5);
    ctx.quadraticCurveTo(-5, -8, -12, -1);
    ctx.fill();
  } else if (hair?.style === 'long') {
    ctx.fillStyle = hair.color;
    ctx.beginPath();
    ctx.moveTo(-12, 2);
    ctx.quadraticCurveTo(-12, -14, 0, -13.5);
    ctx.quadraticCurveTo(12, -14, 12, 2);
    ctx.quadraticCurveTo(7, -6, 0, -5);
    ctx.quadraticCurveTo(-8, -6, -12, 2);
    ctx.fill();
  }
}

function drawScarf(ctx: Ctx2D, s: Skeleton, color: string): void {
  const [bx] = s.joints.neck;
  ctx.fillStyle = color;
  ellipse(ctx, bx, 31.5, 10.5, 3.6);
  ctx.fill();
  limb(
    ctx,
    [
      [bx - 5, 33],
      [bx - 7, 41],
      [bx - 6.5, 48],
    ],
    4.2,
    color,
    null,
  );
}

function drawArms(ctx: Ctx2D, s: Skeleton, look: Look, flat: string | null): void {
  const j = s.joints;
  const sleeve = look.coat.color;
  limb(ctx, [j.shoulderL, j.elbowL, j.handL], 6.4, sleeve, flat);
  limb(ctx, [j.shoulderR, j.elbowR, j.handR], 6.4, sleeve, flat);

  const hand = flat ?? look.gloves?.color ?? look.skin;
  for (const [x, y] of [j.handL, j.handR]) {
    ctx.fillStyle = hand;
    ellipse(ctx, x, y, 3.6, 3.6);
    ctx.fill();
    if (!flat) {
      ctx.fillStyle = tokens.handGlint;
      ellipse(ctx, x - 1, y - 1.2, 1.3, 1.3);
      ctx.fill();
    }
  }
}
