/**
 * A character is data (GAME_DESIGN §5): an appearance referencing catalogue items.
 * The renderer resolves it into a `Look`: what to draw for each layer, by style.
 * Styles come from the catalogue's `renderKey`, so a new item that reuses an
 * existing style needs no code; a new style needs a part (code today, sprite later).
 */

export type Slot = 'hair' | 'headwear' | 'coat' | 'scarf' | 'gloves' | 'trousers' | 'boots';

export interface SlotChoice {
  readonly itemId: string;
  readonly color: string;
}

/** The appearance as the API sends it. */
export interface Appearance {
  readonly skinTone: string;
  readonly slots: Partial<Readonly<Record<Slot, SlotChoice>>>;
}

export type CoatStyle = 'long' | 'hooded' | 'oilskin';
export type HairStyle = 'short' | 'long';
export type HeadwearStyle = 'beanie' | 'hood' | 'brim' | 'wide-brim';
export type ScarfStyle = 'wool' | 'long';
export type GloveStyle = 'wool' | 'fingerless';

export interface Look {
  readonly skin: string;
  readonly coat: { readonly style: CoatStyle; readonly color: string };
  readonly trousers: string;
  readonly boots: string;
  readonly hair?: { readonly style: HairStyle; readonly color: string };
  readonly headwear?: { readonly style: HeadwearStyle; readonly color: string };
  readonly scarf?: { readonly style: ScarfStyle; readonly color: string };
  readonly gloves?: { readonly style: GloveStyle; readonly color: string };
}

/** itemId → renderKey (`coat.long`, `headwear.beanie`…), from the skin catalogue. */
export type RenderKeys = ReadonlyMap<string, string>;

const DEFAULTS = { skin: '#C9C3B9', coat: '#4A5463', trousers: '#262A31', boots: '#16191E' } as const;

function styleOf<T extends string>(
  keys: RenderKeys,
  choice: SlotChoice | undefined,
  allowed: readonly T[],
  fallback: T,
): T {
  const key = choice ? keys.get(choice.itemId) : undefined;
  const style = key?.split('.')[1];
  return allowed.find((s) => s === style) ?? fallback;
}

/** Turns an appearance into what the parts draw. Unknown items fall back to plain styles. */
export function resolveLook(appearance: Appearance, keys: RenderKeys): Look {
  const { slots } = appearance;
  const look: {
    -readonly [K in keyof Look]: Look[K];
  } = {
    skin: appearance.skinTone,
    coat: {
      style: styleOf(keys, slots.coat, ['long', 'hooded', 'oilskin'] as const, 'long'),
      color: slots.coat?.color ?? DEFAULTS.coat,
    },
    trousers: slots.trousers?.color ?? DEFAULTS.trousers,
    boots: slots.boots?.color ?? DEFAULTS.boots,
  };

  if (slots.hair) {
    look.hair = {
      style: styleOf(keys, slots.hair, ['short', 'long'] as const, 'short'),
      color: slots.hair.color,
    };
  }
  if (slots.headwear) {
    look.headwear = {
      style: styleOf(keys, slots.headwear, ['beanie', 'hood', 'brim', 'wide-brim'] as const, 'beanie'),
      color: slots.headwear.color,
    };
  }
  if (slots.scarf) {
    look.scarf = {
      style: styleOf(keys, slots.scarf, ['wool', 'long'] as const, 'wool'),
      color: slots.scarf.color,
    };
  }
  if (slots.gloves) {
    look.gloves = {
      style: styleOf(keys, slots.gloves, ['wool', 'fingerless'] as const, 'wool'),
      color: slots.gloves.color,
    };
  }

  return look;
}

export const DEFAULT_LOOK: Look = {
  skin: DEFAULTS.skin,
  coat: { style: 'long', color: DEFAULTS.coat },
  trousers: DEFAULTS.trousers,
  boots: DEFAULTS.boots,
};
