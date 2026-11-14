import { describe, expect, it } from 'vitest';
import type { Catalog, CatalogItem } from '../api/types.ts';
import { defaultAppearance, itemsFor, paint, wear, withSkinTone } from './wardrobe.ts';

const item = (id: string, slot: CatalogItem['slot'], colors: string[], owned = true): CatalogItem => ({
  id,
  slot,
  rarity: owned ? 'base' : 'unlockable',
  renderKey: `${slot}.${id}`,
  colors,
  unlock: null,
  owned,
});

const catalog: Catalog = {
  items: [
    item('coat-long', 'coat', ['#4A5463', '#6A6744']),
    item('coat-hooded', 'coat', ['#6A6744', '#2E4749']),
    item('coat-storm', 'coat', ['#3A4A4F'], false),
    item('trousers-plain', 'trousers', ['#262A31']),
    item('boots-plain', 'boots', ['#16191E']),
    item('headwear-beanie', 'headwear', ['#7A3931']),
    item('scarf-wool', 'scarf', ['#B5AD9C']),
  ],
  skinTones: ['#C9C3B9', '#8D6E5A'],
};

describe('wardrobe', () => {
  it('starts dressed with base items: coat, trousers, boots and a little more', () => {
    const appearance = defaultAppearance(catalog);
    expect(appearance.slots.coat).toEqual({ itemId: 'coat-long', color: '#4A5463' });
    expect(appearance.slots.trousers?.itemId).toBe('trousers-plain');
    expect(appearance.slots.boots?.itemId).toBe('boots-plain');
    expect(appearance.slots.headwear?.itemId).toBe('headwear-beanie');
    expect(appearance.skinTone).toBe('#C9C3B9');
  });

  it('only offers what the player owns', () => {
    expect(itemsFor(catalog, 'coat').map((i) => i.id)).toEqual(['coat-long', 'coat-hooded']);
    const before = defaultAppearance(catalog);
    expect(wear(before, catalog, 'coat', 'coat-storm')).toBe(before);
  });

  it('keeps the colour when the new item has it, otherwise takes its first', () => {
    const olive = paint(defaultAppearance(catalog), catalog, 'coat', '#6A6744');
    expect(wear(olive, catalog, 'coat', 'coat-hooded').slots.coat).toEqual({
      itemId: 'coat-hooded',
      color: '#6A6744',
    });
    const grey = defaultAppearance(catalog);
    expect(wear(grey, catalog, 'coat', 'coat-hooded').slots.coat?.color).toBe('#6A6744');
  });

  it('optional slots can be emptied, required ones cannot', () => {
    const appearance = defaultAppearance(catalog);
    expect(wear(appearance, catalog, 'headwear', null).slots.headwear).toBeUndefined();
    expect(wear(appearance, catalog, 'coat', null).slots.coat).toBeDefined();
  });

  it('refuses colours and skin tones the catalogue does not offer', () => {
    const appearance = defaultAppearance(catalog);
    expect(paint(appearance, catalog, 'coat', '#D0645F')).toBe(appearance);
    expect(withSkinTone(appearance, catalog, '#000000')).toBe(appearance);
    expect(withSkinTone(appearance, catalog, '#8D6E5A').skinTone).toBe('#8D6E5A');
  });
});
