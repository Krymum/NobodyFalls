import { currentLayout, watchLayout, type LayoutKind } from '@nobodyfalls/layout';
import { useSyncExternalStore } from 'react';

/** The current layout (phone, tablet, desktop); re-renders only when a breakpoint is crossed. */
export function useLayout(): LayoutKind {
  return useSyncExternalStore(watchLayout, currentLayout, () => 'mobile');
}
