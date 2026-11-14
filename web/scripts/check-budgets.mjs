// Checks the performance budgets on the production build (GAME_DESIGN §13, ADR 0010, 0017):
// - game: initial JavaScript ≤ 150 KB and CSS ≤ 30 KB gzip, and the initial JavaScript may
//   not grow by more than 1 KB over the baseline recorded in budgets.json (phase 2 must not
//   make the game heavier: the admin is a separate bundle);
// - admin: initial JavaScript ≤ 200 KB and CSS ≤ 30 KB gzip.
// "Initial" means what the first page loads: the entry module, its modulepreloads and its
// stylesheets. Lazy chunks are listed but not counted.
// Usage: node scripts/check-budgets.mjs [build dir]
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { gzipSync } from 'node:zlib';

const here = fileURLToPath(new URL('.', import.meta.url));
const root = resolve(process.argv[2] ?? join(here, '../../src/NobodyFalls.Api/wwwroot'));
const config = JSON.parse(readFileSync(join(here, '../budgets.json'), 'utf8'));

const attr = (tag, name) => new RegExp(`${name}="([^"]+)"`).exec(tag)?.[1];
const gzipSize = (path) => gzipSync(readFileSync(join(root, path)), { level: 9 }).length;
const kb = (bytes) => `${(bytes / 1024).toFixed(1)} KB`;

function initialFiles(indexPath) {
  const html = readFileSync(indexPath, 'utf8');
  const initial = { js: new Set(), css: new Set() };
  for (const tag of html.match(/<(script|link)\b[^>]*>/g) ?? []) {
    if (tag.startsWith('<script') && attr(tag, 'type') === 'module') {
      initial.js.add(attr(tag, 'src'));
    } else if (attr(tag, 'rel') === 'modulepreload') {
      initial.js.add(attr(tag, 'href'));
    } else if (attr(tag, 'rel') === 'stylesheet') {
      initial.css.add(attr(tag, 'href'));
    }
  }
  return initial;
}

let failed = false;
const fail = (message) => {
  failed = true;
  console.log(`FAIL ${message}`);
};

for (const [name, app] of Object.entries(config)) {
  const indexPath = join(root, app.index);
  console.log(`\n${name} (${app.index})`);
  if (!existsSync(indexPath)) {
    fail(`${indexPath} is missing: build the front end first (pnpm build).`);
    continue;
  }

  const initial = initialFiles(indexPath);
  const totals = {};
  for (const kind of ['js', 'css']) {
    const files = [...initial[kind]].filter(Boolean);
    totals[kind] = 0;
    for (const file of files) {
      const size = gzipSize(file);
      totals[kind] += size;
      console.log(`  ${kind.padEnd(4)} ${kb(size).padStart(9)}  ${file}`);
    }
    const limit = app[kind];
    const ok = totals[kind] <= limit;
    if (ok) {
      console.log(`ok   initial ${kind}: ${kb(totals[kind])} / ${kb(limit)} gzip`);
    } else {
      fail(`initial ${kind}: ${kb(totals[kind])} / ${kb(limit)} gzip`);
    }
  }

  if (app.jsBaseline !== undefined) {
    const growth = totals.js - app.jsBaseline;
    const message = `growth over the baseline: ${growth >= 0 ? '+' : ''}${(growth / 1024).toFixed(2)} KB (allowed +${kb(app.jsGrowthAllowance)})`;
    if (growth > app.jsGrowthAllowance) {
      fail(message);
    } else {
      console.log(`ok   ${message}`);
    }
  }

  const counted = new Set([...initial.js, ...initial.css].map((p) => p.replace(/^\//, '')));
  const assets = join(root, app.assets);
  for (const file of existsSync(assets) ? readdirSync(assets) : []) {
    const path = `${app.assets}/${file}`;
    if (/\.(js|css)$/.test(file) && !counted.has(path)) {
      console.log(`  lazy ${kb(gzipSize(path)).padStart(9)}  /${path}`);
    }
  }
}

process.exit(failed ? 1 : 0);
