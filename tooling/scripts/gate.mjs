#!/usr/bin/env node
// One entry point for every quality gate. Git hooks, CI, Claude hooks and agents all call this,
// so "passes locally, fails in CI" cannot happen. Checks and stages are defined in tooling/gates.yaml.
//
// Usage: node tooling/scripts/gate.mjs <stage> [--staged] [--strict] [--fail-fast] [--list]
//   --staged     pass staged files to checks that declare `staged:` (pre-commit)
//   --strict     a missing tool fails instead of being skipped (CI=true sets this too)
//   --fail-fast  stop at the first failing check
//   --list       print stages and checks
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { delimiter, join } from 'node:path';
import { loadGates } from './lib/gates.mjs';
import { listFiles, root, stagedFiles } from './lib/repo.mjs';

const args = process.argv.slice(2);
const stage = args.find((arg) => !arg.startsWith('--'));
const flag = (name) => args.includes(`--${name}`);
const strict = flag('strict') || process.env.CI === 'true';
// ${name} variables and env from gates.yaml are applied here (lib/gates.mjs).
const { config, env: gateEnv } = loadGates(root);
const runEnv = { ...gateEnv, ...process.env };

if (flag('list') || !stage) {
  for (const [name, checks] of Object.entries(config.stages)) console.log(`${name.padEnd(11)} ${checks.join(', ')}`);
  process.exit(stage || flag('list') ? 0 : 2);
}
const checks = config.stages[stage];
if (!checks) {
  console.error(`Unknown stage "${stage}". Stages: ${Object.keys(config.stages).join(', ')}`);
  process.exit(2);
}

const onPath = (tool) => {
  const exts = process.platform === 'win32' ? ['', ...(process.env.PATHEXT ?? '.EXE;.CMD;.BAT').split(';')] : [''];
  return (process.env.PATH ?? '').split(delimiter).some((dir) => dir && exts.some((ext) => existsSync(join(dir, tool + ext)) || existsSync(join(dir, tool + ext.toLowerCase()))));
};
let fileList;
const requirementMet = (requirement) => {
  if (requirement.startsWith('glob:')) {
    const pattern = new RegExp(requirement.slice(5));
    fileList ??= listFiles();
    return fileList.some((file) => pattern.test(file));
  }
  // "a|b|c": any one of the paths is enough (for example eslint.config.js or eslint.config.mjs).
  return requirement.split('|').some((path) => existsSync(join(root, path)));
};
const quote = (value) => (/[\s"'&|<>^]/.test(value) ? `"${value.replaceAll('"', '\\"')}"` : value);

// --files a b c works like --staged with an explicit list (agent hooks pass the edited files).
const filesIndex = args.indexOf('--files');
const explicitFiles = filesIndex >= 0 ? args.slice(filesIndex + 1).filter((arg) => !arg.startsWith('--')) : null;
const staged = explicitFiles ?? (flag('staged') ? stagedFiles() : null);
const results = [];
for (const id of checks) {
  const check = config.checks[id];
  if (!check) {
    results.push({ id, status: 'fail', note: 'not defined in tooling/gates.yaml' });
    continue;
  }
  const missingRequirement = (check.requires ?? []).find((requirement) => !requirementMet(requirement));
  if (missingRequirement) {
    results.push({ id, status: 'n/a', note: `needs ${missingRequirement}` });
    continue;
  }
  const missingTool = (check.tools ?? []).find((tool) => !onPath(tool));
  if (missingTool) {
    results.push({ id, status: strict ? 'fail' : 'skip', note: `${missingTool} not installed` });
    if (strict && flag('fail-fast')) break;
    continue;
  }
  const command = check.run;
  let commands = null;
  if (check.staged !== undefined) {
    if (!staged) {
      results.push({ id, status: 'n/a', note: 'runs only with --staged' });
      continue;
    }
    const pattern = check.match ? new RegExp(check.match) : null;
    // A check with cwd gets the staged files below that folder, relative to it.
    const prefix = check.cwd && check.cwd !== '.' ? `${check.cwd.replace(/\/$/, '')}/` : '';
    const files = staged.filter((file) => (!pattern || pattern.test(file)) && file.startsWith(prefix)).map((file) => file.slice(prefix.length));
    if (!files.length) {
      results.push({ id, status: 'n/a', note: 'no staged files match' });
      continue;
    }
    // Windows limits a command line to about 32 KB: a large change (a rename of hundreds of files)
    // runs the check in batches instead of failing to start.
    const batches = [[]];
    for (const file of files.map(quote)) {
      const current = batches.at(-1);
      if (current.length && [command, check.staged, ...current, file].join(' ').length > 7000) batches.push([]);
      batches.at(-1).push(file);
    }
    commands = batches.map((group) => [command, check.staged ?? '', ...group].filter(Boolean).join(' '));
  }
  const started = Date.now();
  let status = 0;
  for (const line of commands ?? [command]) {
    console.log(`\n▶ ${id}: ${line.length > 400 ? `${line.slice(0, 400)} …` : line}`);
    const run = spawnSync(line, { cwd: check.cwd ? join(root, check.cwd) : root, shell: true, stdio: 'inherit', env: runEnv });
    if (run.status !== 0) status = run.status ?? 1;
  }
  const seconds = ((Date.now() - started) / 1000).toFixed(1);
  results.push({ id, status: status === 0 ? 'pass' : 'fail', note: `${seconds}s` });
  if (status !== 0 && flag('fail-fast')) break;
}

// The last gate result is kept locally for the session metrics (session-end hook).
try {
  mkdirSync(join(root, '.local'), { recursive: true });
  writeFileSync(join(root, '.local/gate-last.json'), JSON.stringify({ stage, ok: !results.some((result) => result.status === 'fail'), at: new Date().toISOString() }));
} catch {
  // Metrics are best effort; never fail a gate because of them.
}

const icon = { pass: '✓', fail: '✗', skip: '○', 'n/a': '·' };
console.log(`\nGate "${stage}"${strict ? ' (strict)' : ''}:`);
for (const result of results) console.log(`  ${icon[result.status]} ${result.id.padEnd(20)} ${result.status.padEnd(5)} ${result.note ?? ''}`);
const failed = results.filter((result) => result.status === 'fail');
if (failed.length) {
  console.error(`\n${failed.length} check(s) failed: ${failed.map((result) => result.id).join(', ')}`);
  process.exitCode = 1;
}
