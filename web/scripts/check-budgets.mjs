// Checks the performance budgets of GAME_DESIGN §13 on the production build:
// initial JavaScript ≤ 150 KB and CSS ≤ 30 KB, gzip. "Initial" means what the
// first page loads: the entry module, its modulepreloads and its stylesheets.
// Lazy chunks (SignalR, the dev scene) are listed but not counted.
// Usage: node scripts/check-budgets.mjs [build dir]
import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'node:zlib';

const BUDGETS = { js: 150 * 1024, css: 30 * 1024 };

const here = fileURLToPath(new URL('.', import.meta.url));
const root = resolve(process.argv[2] ?? join(here, '../../src/NobodyFalls.Api/wwwroot'));
const html = readFileSync(join(root, 'index.html'), 'utf8');

const attr = (tag, name) => new RegExp(`${name}="([^"]+)"`).exec(tag)?.[1];
const tags = html.match(/<(script|link)\b[^>]*>/g) ?? [];
const initial = { js: new Set(), css: new Set() };
for (const tag of tags) {
  if (tag.startsWith('<script') && attr(tag, 'type') === 'module') {
    initial.js.add(attr(tag, 'src'));
  } else if (attr(tag, 'rel') === 'modulepreload') {
    initial.js.add(attr(tag, 'href'));
  } else if (attr(tag, 'rel') === 'stylesheet') {
    initial.css.add(attr(tag, 'href'));
  }
}

const gzipSize = (path) => gzipSync(readFileSync(join(root, path)), { level: 9 }).length;
const kb = (bytes) => `${(bytes / 1024).toFixed(1)} KB`;

let failed = false;
for (const kind of /** @type {const} */ (['js', 'css'])) {
  const files = [...initial[kind]].filter(Boolean);
  if (files.length === 0) {
    console.error(`No initial ${kind} found in ${join(root, 'index.html')}: is the build there?`);
    process.exit(1);
  }
  let total = 0;
  for (const file of files) {
    const size = gzipSize(file);
    total += size;
    console.log(`  ${kind}  ${kb(size).padStart(9)}  ${file}`);
  }
  const ok = total <= BUDGETS[kind];
  failed ||= !ok;
  console.log(`${ok ? 'ok  ' : 'FAIL'} initial ${kind}: ${kb(total)} / ${kb(BUDGETS[kind])} gzip\n`);
}

const counted = new Set([...initial.js, ...initial.css].map((p) => p.replace(/^\//, '')));
const lazy = readdirSync(join(root, 'assets'))
  .map((name) => `assets/${name}`)
  .filter((p) => /\.(js|css)$/.test(p) && !counted.has(p));
for (const file of lazy) {
  console.log(`  lazy ${kb(gzipSize(file)).padStart(9)}  /${file}`);
}

process.exit(failed ? 1 : 0);
