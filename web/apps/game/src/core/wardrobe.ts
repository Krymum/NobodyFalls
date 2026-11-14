import type { Appearance, SlotChoice } from '@nobodyfalls/render';
import type { Catalog, CatalogItem, Slot } from '../api/types.ts';

/** Slots a player dresses at creation; trousers and boots get the first base item. */
export const EDITABLE_SLOTS = [
  'coat',
  'headwear',
  'hair',
  'scarf',
  'gloves',
] as const satisfies readonly Slot[];

/** Slots that can never be empty (the server checks it too). */
const REQUIRED: readonly Slot[] = ['coat', 'trousers', 'boots'];

export function itemsFor(catalog: Catalog, slot: Slot): CatalogItem[] {
  return catalog.items.filter((item) => item.slot === slot && item.owned);
}

export function isRequired(slot: Slot): boolean {
  return REQUIRED.includes(slot);
}

/** A first outfit: the reference scene's "me" when the catalogue allows it. */
export function defaultAppearance(catalog: Catalog): Appearance {
  const first = (slot: Slot, preferred?: string): SlotChoice | undefined => {
    const items = itemsFor(catalog, slot);
    const item = items.find((i) => i.id === preferred) ?? items[0];
    const color = item?.colors[0];
    return item && color ? { itemId: item.id, color } : undefined;
  };
  const slots: Partial<Record<Slot, SlotChoice>> = {};
  for (const [slot, preferred] of [
    ['coat', 'coat-long'],
    ['trousers', undefined],
    ['boots', undefined],
    ['headwear', 'headwear-beanie'],
    ['scarf', 'scarf-wool'],
  ] as const) {
    const choice = first(slot, preferred);
    if (choice) {
      slots[slot] = choice;
    }
  }
  return { skinTone: catalog.skinTones[0] ?? '#C9C3B9', slots };
}

/** Wears another item (or nothing) in a slot, keeping the colour when the new item offers it. */
export function wear(
  appearance: Appearance,
  catalog: Catalog,
  slot: Slot,
  itemId: string | null,
): Appearance {
  if (itemId === null) {
    if (isRequired(slot)) {
      return appearance;
    }
    const remaining = Object.fromEntries(Object.entries(appearance.slots).filter(([key]) => key !== slot));
    return { ...appearance, slots: remaining };
  }
  const slots = { ...appearance.slots };
  const item = catalog.items.find((i) => i.id === itemId && i.slot === slot && i.owned);
  if (!item) {
    return appearance;
  }
  const current = appearance.slots[slot]?.color;
  const color = current && item.colors.includes(current) ? current : item.colors[0];
  if (color) {
    slots[slot] = { itemId: item.id, color };
  }
  return { ...appearance, slots };
}

export function paint(appearance: Appearance, catalog: Catalog, slot: Slot, color: string): Appearance {
  const choice = appearance.slots[slot];
  const item = choice ? catalog.items.find((i) => i.id === choice.itemId) : undefined;
  if (!choice || !item?.colors.includes(color)) {
    return appearance;
  }
  return { ...appearance, slots: { ...appearance.slots, [slot]: { ...choice, color } } };
}

export function withSkinTone(appearance: Appearance, catalog: Catalog, tone: string): Appearance {
  return catalog.skinTones.includes(tone) ? { ...appearance, skinTone: tone } : appearance;
}
