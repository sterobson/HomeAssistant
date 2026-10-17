// Shows exactly how the local copy differs from the production snapshot, key by key.
// Expected and benign: the new code adds fields (room displayName/group, settings.heating).
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const snap = join(dirname(fileURLToPath(import.meta.url)), '.work', 'snapshot');
const LOCAL = 'http://localhost:7159/api';
const house = process.argv[2] ?? 'LOCAL-COPY';

const walk = (a, b, path, out) => {
  if (a && b && typeof a === 'object' && typeof b === 'object') {
    for (const k of new Set([...Object.keys(a), ...Object.keys(b)])) {
      const p = Array.isArray(a) ? `${path}[${k}]` : `${path}.${k}`;
      if (!(k in a)) out.push(`+ added in local   ${p} = ${JSON.stringify(b[k])}`);
      else if (!(k in b)) out.push(`- missing locally ${p} = ${JSON.stringify(a[k])}`);
      else walk(a[k], b[k], p, out);
    }
  } else if (JSON.stringify(a) !== JSON.stringify(b)) {
    out.push(`~ changed          ${path}: ${JSON.stringify(a)} -> ${JSON.stringify(b)}`);
  }
};

for (const ep of ['schedules', 'device-settings']) {
  const prod = JSON.parse(readFileSync(join(snap, `${ep}.json`), 'utf8'));
  const local = await (await fetch(`${LOCAL}/${ep}?houseId=${house}`)).json();
  const out = [];
  walk(prod, local, ep, out);
  const summary = {};
  for (const line of out) { const k = line.replace(/\[\d+\]/g, '[]').replace(/ = .*| : .*|:.*/, ''); summary[k] = (summary[k] ?? 0) + 1; }
  console.log(`\n${ep}: ${out.length} difference(s)`);
  for (const [k, n] of Object.entries(summary)) console.log(`   ${String(n).padStart(3)}x  ${k}`);
  const unexpected = out.filter((l) => !(l.startsWith('+') && /\.(displayName|group|heating)\b/.test(l)));
  console.log(unexpected.length ? `   UNEXPECTED:\n${unexpected.map((l) => '     ' + l).join('\n')}` : '   all differences are the new fields only');
}
