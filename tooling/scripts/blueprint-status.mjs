#!/usr/bin/env node
// Compares the repo-blueprint version this repository applied (tooling/blueprint.json) with the version
// of the blueprint checkout next to it (`source` in tooling/blueprint.json, default ../repo-blueprint)
// and lists the versions in between so the owner sees what an update brings.
// Usage: node tooling/scripts/blueprint-status.mjs [--strict] [--quiet]
//   --strict  exit 1 when the repository is behind (nightly stage)
//   --quiet   print only when behind (session-start hook)
import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { root } from './lib/repo.mjs';

const args = process.argv.slice(2);
const say = (text) => {
  if (!args.includes('--quiet')) console.log(text);
};
const parse = (version) => version.split('.').map(Number);
const newer = (a, b) => {
  const [x, y] = [parse(a), parse(b)];
  for (let i = 0; i < 3; i++) if (x[i] !== y[i]) return x[i] > y[i];
  return false;
};

const manifestPath = join(root, 'tooling/blueprint.json');
if (!existsSync(manifestPath)) {
  say('○ blueprint: no tooling/blueprint.json; the blueprint is not applied');
} else {
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
  const source = resolve(root, manifest.source ?? '../repo-blueprint');
  if (!existsSync(join(source, 'VERSION'))) {
    say(`○ blueprint: ${manifest.version}; no blueprint checkout at ${source} to compare with`);
  } else {
    const latest = readFileSync(join(source, 'VERSION'), 'utf8').trim();
    if (!newer(latest, manifest.version)) {
      say(`✓ blueprint: repo-blueprint ${manifest.version} is current`);
    } else {
      const changelog = existsSync(join(source, 'CHANGELOG.md')) ? readFileSync(join(source, 'CHANGELOG.md'), 'utf8') : '';
      const versions = [...changelog.matchAll(/^## \[(\d+\.\d+\.\d+)\]/gm)].map((match) => match[1]).filter((version) => newer(version, manifest.version));
      console.log(`repo-blueprint ${latest} is available; this repository has ${manifest.version} (new: ${versions.join(', ') || latest}). Update with skill update-blueprint.`);
      if (args.includes('--strict')) process.exitCode = 1;
    }
  }
}
