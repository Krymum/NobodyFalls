import { describe, expect, it } from 'vitest';
import { frame, MAX_SCALE, MIN_SCALE, project, sceneInsets } from './camera.ts';
import { SCENE_HEIGHT, SCENE_WIDTH } from './neighbourhood.ts';

const none = { left: 0, right: 0, top: 0, bottom: 0 };

describe('camera', () => {
  it('shows the reference phone exactly as composed', () => {
    expect(frame(390, 844, none, 'full')).toEqual({ scale: 1, offsetX: 0, offsetY: 0 });
  });

  it('sizes characters on the height in landscape, and centres them with room on the sides', () => {
    const camera = frame(1366, 768, none, 'full');
    expect(camera.scale).toBeCloseTo(768 / SCENE_HEIGHT);
    const [left] = project(camera, 0, 0);
    const [right] = project(camera, SCENE_WIDTH, 0);
    expect(left).toBeGreaterThan(400);
    expect(1366 - right).toBeCloseTo(left);
  });

  it('never lets the composition overflow a narrow window', () => {
    const camera = frame(360, 900, none, 'full');
    expect(SCENE_WIDTH * camera.scale).toBeLessThanOrEqual(360);
    expect(camera.offsetX).toBeGreaterThanOrEqual(0);
  });

  it('keeps characters between the minimum and maximum sizes', () => {
    expect(frame(320, 480, none, 'full').scale).toBe(MIN_SCALE);
    expect(frame(3840, 2160, none, 'content').scale).toBe(MAX_SCALE);
  });

  it('frames the free area between the rail and the side panel on desktop', () => {
    const { insets, focus } = sceneInsets('desktop', 'full', 1440);
    expect(insets).toEqual({ left: 88, right: 420, top: 0, bottom: 0 });
    const camera = frame(1440, 900, insets, focus);
    const [left] = project(camera, 0, 0);
    const [right] = project(camera, SCENE_WIDTH, 0);
    expect(left - 88).toBeCloseTo(1440 - 420 - right);
    expect(left).toBeGreaterThan(88);
    expect(right).toBeLessThan(1440 - 420);
  });

  it('on desktop, characters grow with the height (the text has moved aside)', () => {
    const { insets, focus } = sceneInsets('desktop', 'full', 1440);
    const desktop = frame(1440, 900, insets, focus);
    const phone = frame(390, 844, none, 'full');
    expect(desktop.scale).toBeGreaterThan(phone.scale * 1.3);
    // The people (composition rows 30–600) are vertically centred.
    const [, top] = project(desktop, 0, 30);
    const [, bottom] = project(desktop, 0, 600);
    expect(top).toBeCloseTo(900 - bottom);
  });

  it('phones and tablets keep the bottom of the composition for the text panel', () => {
    expect(sceneInsets('mobile', 'full', 390)).toEqual({ insets: none, focus: 'full' });
    expect(sceneInsets('tablet', 'dimmed', 820)).toEqual({ insets: none, focus: 'full' });
  });

  it('is uniform: a square stays a square', () => {
    const camera = frame(1180, 820, none, 'full');
    const [x0, y0] = project(camera, 100, 100);
    const [x1, y1] = project(camera, 200, 200);
    expect(x1 - x0).toBeCloseTo(y1 - y0);
  });
});
