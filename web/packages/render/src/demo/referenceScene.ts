import type { CharacterInstance } from '../CharacterRenderer.ts';
import type { Look } from '../look.ts';
import type { Posture } from '../rig/skeleton.ts';

/**
 * The validated reference scene (docs/reference/scene-demo.dc.html) as data: same
 * people, clothes and positions on a 390 × 844 screen. Used by the `/dev/scene` page
 * and by tests that check the renderers reproduce it.
 */
const skin = '#C9C3B9';
const trousers = '#262A31';
const boots = '#16191E';

function look(coat: string, extra: Partial<Omit<Look, 'skin' | 'coat' | 'trousers' | 'boots'>>): Look {
  return { skin, coat: { style: 'long', color: coat }, trousers, boots, ...extra };
}

export type CastId = 'lea' | 'kenji' | 'me' | 'tom' | 'ines' | 'yuki';

export interface CastMember extends Omit<CharacterInstance, 'posture'> {
  readonly id: CastId;
  readonly name: string;
  readonly posture: Posture;
}

export const REFERENCE_CAST: readonly CastMember[] = [
  {
    id: 'lea',
    name: 'LÉA_M',
    x: 106,
    y: 142,
    scale: 0.8,
    posture: 'held',
    phase: 0.3,
    amplitude: 0.025,
    look: look('#6A6744', { hair: { style: 'long', color: '#2A1E19' } }),
  },
  {
    id: 'kenji',
    name: 'KENJI',
    x: 296,
    y: 120,
    scale: 0.8,
    posture: 'held',
    phase: 2.1,
    amplitude: 0.022,
    look: look('#2E4749', {
      headwear: { style: 'hood', color: '#2E4749' },
      gloves: { style: 'wool', color: '#3B3F45' },
    }),
  },
  {
    id: 'me',
    name: 'TOI',
    x: 195,
    y: 282,
    scale: 1.15,
    posture: 'held',
    phase: 1.1,
    amplitude: 0.018,
    look: look('#4A5463', {
      headwear: { style: 'beanie', color: '#7A3931' },
      scarf: { style: 'wool', color: '#B5AD9C' },
      gloves: { style: 'wool', color: '#2E3238' },
    }),
  },
  {
    id: 'tom',
    name: 'TOM.B',
    x: 100,
    y: 505,
    scale: 0.85,
    posture: 'held',
    phase: 3.3,
    amplitude: 0.02,
    look: look('#4E5A3C', { headwear: { style: 'beanie', color: '#585C63' } }),
  },
  {
    id: 'ines',
    name: 'INES',
    x: 200,
    y: 540,
    scale: 0.85,
    posture: 'attention',
    phase: 0.9,
    amplitude: 0.035,
    look: look('#5A2E39', {
      hair: { style: 'short', color: '#CFC7BA' },
      scarf: { style: 'wool', color: '#6F757E' },
    }),
  },
  {
    id: 'yuki',
    name: 'YUKI',
    x: 318,
    y: 560,
    scale: 0.8,
    posture: 'danger',
    phase: 2.5,
    amplitude: 0.06,
    look: look('#857A65', { headwear: { style: 'brim', color: '#34343A' } }),
  },
];
