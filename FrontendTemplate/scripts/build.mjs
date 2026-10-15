import { cp, mkdir, readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildResourceKit } from './resources.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const dist = path.join(root, 'dist');
await mkdir(dist, { recursive: true });
for (const item of ['index.html', 'src', 'LICENSE', 'NOTICE']) {
  await cp(path.join(root, item), path.join(dist, item), { recursive: true });
}
const html = await readFile(path.join(dist, 'index.html'), 'utf8');
if (!html.includes('flight-taskbar')) throw new Error('The production entry point is missing.');
await buildResourceKit(root, dist);
console.log(`Built FlightTaskbarWeb: ${dist}`);
