#!/usr/bin/env node
// Converts working-tree files that git stores with LF but that are checked out with CRLF (a Windows
// checkout with core.autocrlf=true from before .gitattributes said eol=lf). Only line endings change,
// so git shows no diff afterwards. Run once after adopting the blueprint on Windows.
// Usage: node tooling/scripts/normalize-eol.mjs [--apply]   (default: list what would change)
import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { root } from './lib/repo.mjs';

const apply = process.argv.includes('--apply');
const lines = execFileSync('git', ['ls-files', '--eol'], { cwd: root, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 }).split('\n');
const targets = lines
  .map((line) => line.match(/^i\/lf\s+w\/crlf\s+\S*\s*\t(.+)$/)?.[1] ?? line.match(/^i\/lf\s+w\/crlf.*\t(.+)$/)?.[1])
  .filter(Boolean);
for (const file of targets) {
  if (apply) writeFileSync(join(root, file), readFileSync(join(root, file), 'utf8').replace(/\r\n/g, '\n'));
}
console.log(`${apply ? 'normalized' : 'would normalize'} ${targets.length} file(s) from CRLF to LF${apply ? '' : ' (run with --apply)'}`);
