import { theme } from './tokens.ts';

/** `backgroundTop` → `background-top`. */
function kebab(name: string): string {
  return name.replace(/[A-Z]/g, (letter) => `-${letter.toLowerCase()}`);
}

/**
 * The tokens the interface needs, as CSS custom properties. Generated into
 * `tokens.css` at development time (no CSS at run time, GAME_DESIGN §13).
 */
export function toCssVariables(): string {
  const lines: string[] = [];
  for (const [name, value] of Object.entries(theme.palette)) {
    lines.push(`  --nf-color-${kebab(name)}: ${value};`);
  }
  for (const [name, value] of Object.entries(theme.typography)) {
    lines.push(`  --nf-font-${kebab(name)}: ${String(value)};`);
  }
  for (const [name, value] of Object.entries(theme.space)) {
    lines.push(`  --nf-space-${kebab(name)}: ${value};`);
  }
  for (const [name, value] of Object.entries(theme.layout)) {
    lines.push(
      `  --nf-layout-${kebab(name)}: ${name === 'panelViewport' ? `${value * 100}vw` : `${value}px`};`,
    );
  }
  lines.push(
    `  --nf-layout-panel: clamp(${theme.layout.panelMin}px, ${theme.layout.panelViewport * 100}vw, ${theme.layout.panelMax}px);`,
  );
  for (const [name, value] of Object.entries(theme.fluid)) {
    lines.push(`  --nf-fluid-${kebab(name)}: ${value};`);
  }

  return [
    '/* Generated from src/tokens.ts by `pnpm --filter @nobodyfalls/theme gen`. Do not edit. */',
    ':root {',
    ...lines,
    '}',
    '',
  ].join('\n');
}
