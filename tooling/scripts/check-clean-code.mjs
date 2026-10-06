#!/usr/bin/env node
// Pragmatic clean-code limits (repo-blueprint ADR 0009): cognitive complexity, lines per function and
// parameters per function (Biome rules in tooling/config/clean-code.biome.jsonc) plus lines per file
// (maxFileLines in tooling/config/clean-code.json). Existing code is ratcheted, not rewritten:
// tooling/config/clean-code-baseline.json holds the number of findings per file when the rules were
// adopted, and a file may never get more. New files start at zero.
// Usage: node tooling/scripts/check-clean-code.mjs [--files a.ts b.ts] [--init-baseline] [--update-baseline]
import { spawnSync } from 'node:child_process';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { listFiles, readJson, report, root } from './lib/repo.mjs';

const args = process.argv.slice(2);
const filesIndex = args.indexOf('--files');
const explicit = filesIndex >= 0 ? args.slice(filesIndex + 1).filter((arg) => !arg.startsWith('--')) : null;
const settings = { roots: ['apps', 'packages', 'src'], extensions: ['ts', 'tsx', 'js', 'jsx', 'mjs', 'cjs'], maxFileLines: 500, biomeConfig: 'tooling/config/clean-code.biome.jsonc', ...readJson('tooling/config/clean-code.json') };
const baselinePath = join(root, 'tooling/config/clean-code-baseline.json');
const baseline = existsSync(baselinePath) ? JSON.parse(readFileSync(baselinePath, 'utf8')).files ?? {} : {};
const sourcePattern = new RegExp(`\\.(${settings.extensions.join('|')})$`);
const inRoots = (file) => settings.roots.some((dir) => file === dir || file.startsWith(`${dir}/`));

const targets = (explicit ?? listFiles())
  .map((file) => file.replaceAll('\\', '/'))
  .filter((file) => sourcePattern.test(file) && !file.endsWith('.d.ts') && existsSync(join(root, file)) && (explicit || inRoots(file)));

// Biome from the repository's own dependencies (no pnpm or npx on PATH needed).
const biomeBin = join(root, 'node_modules/@biomejs/biome/bin/biome');
// Without installed dependencies (Docker-first repositories on the host) the check is skipped like a missing
// tool; in CI (CI=true, set in the local CI image) it fails.
if (targets.length && !existsSync(biomeBin)) {
  if (process.env.CI === 'true' || args.includes('--strict')) {
    console.error('✗ clean-code: node_modules/@biomejs/biome is missing; install the dependencies first');
    process.exit(1);
  }
  console.log('○ clean-code: skipped, node_modules/@biomejs/biome is not installed here (ci-local.mjs runs it)');
  process.exit(0);
}

if (args.includes('--init-baseline') && existsSync(baselinePath) && !args.includes('--force')) {
  console.error('✗ clean-code: a baseline exists; --update-baseline lowers it, --init-baseline --force replaces it (owner decision)');
  process.exit(1);
}

/** findings per file: [{ rule, line, message }] */
const findings = new Map();
const add = (file, finding) => findings.set(file, [...(findings.get(file) ?? []), finding]);

// Biome on batches of files (Windows command-line limit), one JSON report per batch.
const batches = [[]];
for (const file of targets) {
  if (batches.at(-1).join(' ').length > 6000) batches.push([]);
  batches.at(-1).push(file);
}
for (const batch of batches.filter((group) => group.length)) {
  const run = spawnSync(process.execPath, [biomeBin, 'lint', '--config-path', settings.biomeConfig, '--reporter=json', '--max-diagnostics=none', '--no-errors-on-unmatched', ...batch], { cwd: root, encoding: 'utf8', maxBuffer: 1 << 28 });
  const start = run.stdout.indexOf('{');
  if (start < 0) {
    console.error(`✗ clean-code: biome produced no report\n${run.stderr}`);
    process.exit(1);
  }
  for (const diagnostic of JSON.parse(run.stdout.slice(start)).diagnostics ?? []) {
    if (!diagnostic.category?.startsWith('lint/complexity/')) continue;
    add(diagnostic.location.path.replaceAll('\\', '/'), { rule: diagnostic.category.replace('lint/complexity/', ''), line: diagnostic.location.start?.line ?? 0, message: diagnostic.message });
  }
}
for (const file of targets) {
  const lines = readFileSync(join(root, file), 'utf8').split('\n').length;
  if (lines > settings.maxFileLines) add(file, { rule: 'maxFileLines', line: 1, message: `File has ${lines} lines (max ${settings.maxFileLines}); split it by responsibility.` });
}

const errors = [];
for (const file of targets) {
  const list = findings.get(file) ?? [];
  const allowed = baseline[file] ?? 0;
  if (list.length > allowed && !args.includes('--init-baseline')) {
    errors.push(`${file}: ${list.length} finding(s), baseline ${allowed}. Split long or complex functions, group parameters into an object:\n${list.map((f) => `      line ${f.line}: ${f.rule}: ${f.message}`).join('\n')}`);
  }
}

if (args.includes('--init-baseline') || args.includes('--update-baseline')) {
  const next = {};
  if (args.includes('--init-baseline')) for (const [file, list] of findings) next[file] = list.length;
  else for (const [file, count] of Object.entries(baseline)) if (findings.get(file)?.length) next[file] = Math.min(count, findings.get(file).length);
  const sorted = Object.fromEntries(Object.entries(next).sort(([a], [b]) => a.localeCompare(b)));
  writeFileSync(baselinePath, `${JSON.stringify({ $comment: 'Clean-code findings per file when the limits were adopted (repo-blueprint ADR 0009). The numbers only go down: check-clean-code.mjs --update-baseline lowers them after a cleanup.', files: sorted }, null, 2)}\n`);
  console.log(`clean-code baseline: ${Object.keys(sorted).length} file(s), ${Object.values(sorted).reduce((a, b) => a + b, 0)} finding(s)`);
}
if (!explicit && !args.includes('--init-baseline')) {
  const improved = Object.entries(baseline).filter(([file, count]) => (findings.get(file)?.length ?? 0) < count).length;
  if (improved) console.log(`○ clean-code: ${improved} file(s) improved below the baseline; run --update-baseline to lock it in`);
}
report('clean-code', errors, { okMessage: `${targets.length} file(s) checked, no new findings${Object.keys(baseline).length ? ` (${Object.keys(baseline).length} file(s) in the baseline)` : ''}` });
