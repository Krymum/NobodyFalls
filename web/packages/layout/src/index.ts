import { breakpoints } from '@nobodyfalls/theme';

/** The three layouts of ART_DIRECTION, "Responsive". */
export type LayoutKind = 'mobile' | 'tablet' | 'desktop';

/** The layout for a real width in CSS pixels (never the user agent). */
export function layoutOf(width: number): LayoutKind {
  if (width >= breakpoints.desktop) {
    return 'desktop';
  }
  return width >= breakpoints.tablet ? 'tablet' : 'mobile';
}

/** The media queries matching each layout, for `matchMedia` and the tests. */
export const MEDIA: Record<LayoutKind, string> = {
  mobile: `(max-width: ${breakpoints.tablet - 1}px)`,
  tablet: `(min-width: ${breakpoints.tablet}px) and (max-width: ${breakpoints.desktop - 1}px)`,
  desktop: `(min-width: ${breakpoints.desktop}px)`,
};

/** The current layout of this window. */
export function currentLayout(): LayoutKind {
  return typeof window === 'undefined' ? 'mobile' : layoutOf(window.innerWidth);
}

/**
 * Calls `onChange` whenever the layout changes (crossing a breakpoint), not on every
 * pixel of a resize. Framework-free: apps wrap it (e.g. `useSyncExternalStore`).
 * Returns a function that stops listening.
 */
export function watchLayout(onChange: () => void): () => void {
  if (typeof matchMedia !== 'function') {
    return () => undefined;
  }
  const queries = Object.values(MEDIA).map((query) => matchMedia(query));
  for (const query of queries) {
    query.addEventListener('change', onChange);
  }
  return () => {
    for (const query of queries) {
      query.removeEventListener('change', onChange);
    }
  };
}

/**
 * Keys that should not trigger shortcuts: typing in a field, or with a modifier held
 * (the browser's and the system's own shortcuts win).
 */
export function isTypingOrModified(event: KeyboardEvent): boolean {
  if (event.ctrlKey || event.metaKey || event.altKey) {
    return true;
  }
  const target = event.target;
  if (!(target instanceof HTMLElement)) {
    return false;
  }
  return target.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName);
}
