// Renders the PNG icons from public/icons/icon.svg with Playwright's Chromium.
// Usage: pnpm --filter @nobodyfalls/game icons
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';

const dir = fileURLToPath(new URL('../public/icons/', import.meta.url));
const svg = readFileSync(`${dir}icon.svg`, 'utf8');

const targets = [
  { file: 'icon-192.png', size: 192, padding: 0 },
  { file: 'icon-512.png', size: 512, padding: 0 },
  // Maskable icons keep their content inside the central safe zone (80 %).
  { file: 'maskable-512.png', size: 512, padding: 0.1 },
  { file: 'apple-touch-icon.png', size: 180, padding: 0 },
  { file: 'badge-96.png', size: 96, padding: 0 },
];

const browser = await chromium.launch();
const page = await browser.newPage();
for (const { file, size, padding } of targets) {
  const inner = Math.round(size * (1 - padding * 2));
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(
    `<html><body style="margin:0;background:#05070A;display:grid;place-items:center;width:${size}px;height:${size}px">
       <div style="width:${inner}px;height:${inner}px">${svg.replace('<svg ', `<svg width="${inner}" height="${inner}" `)}</div>
     </body></html>`,
  );
  await page.screenshot({ path: `${dir}${file}`, omitBackground: false });
  console.log(`Wrote ${file}`);
}
await browser.close();
