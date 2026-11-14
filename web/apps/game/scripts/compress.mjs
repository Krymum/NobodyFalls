// Writes a brotli (.br) and a gzip (.gz) copy next to each text file of the build,
// once, at the strongest settings. The API serves them by Accept-Encoding
// (src/NobodyFalls.Api/Hosting/PrecompressedFiles.cs). index.html is left alone:
// the API serves it itself, and it is tiny.
import { readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { brotliCompressSync, constants, gzipSync } from 'node:zlib';

const root = fileURLToPath(new URL('../../../../src/NobodyFalls.Api/wwwroot', import.meta.url));
const COMPRESSIBLE = /\.(js|css|svg|webmanifest|json)$/;

function* walk(dir) {
  for (const name of readdirSync(dir)) {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) {
      yield* walk(path);
    } else {
      yield path;
    }
  }
}

let count = 0;
for (const path of walk(root)) {
  if (!COMPRESSIBLE.test(path)) {
    continue;
  }
  const content = readFileSync(path);
  const variants = {
    br: brotliCompressSync(content, {
      params: {
        [constants.BROTLI_PARAM_QUALITY]: constants.BROTLI_MAX_QUALITY,
        [constants.BROTLI_PARAM_SIZE_HINT]: content.length,
      },
    }),
    gz: gzipSync(content, { level: 9 }),
  };
  for (const [suffix, compressed] of Object.entries(variants)) {
    // A variant only helps if it is smaller.
    if (compressed.length < content.length) {
      writeFileSync(`${path}.${suffix}`, compressed);
    }
  }
  count++;
}
console.log(`compressed ${count} files in ${relative(process.cwd(), root) || '.'}`);
