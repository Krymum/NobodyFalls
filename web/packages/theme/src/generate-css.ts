import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { toCssVariables } from './css.ts';

const target = fileURLToPath(new URL('./tokens.css', import.meta.url));
writeFileSync(target, toCssVariables());
console.log(`Wrote ${target}`);
