#!/usr/bin/env node
// Lists what this clone is missing for fast local feedback: tools the active gates need, dependencies
// that are not installed and git hooks that are not set up, each with the command that fixes it.
// Only tools used before CI (edit, turn-end, pre-commit, pre-push, verify) are listed; CI-only
// scanners run in the local CI container (ci-local.mjs) and need no local install.
// Usage: node tooling/scripts/doctor.mjs [--once] [--quiet] [--fix] [--system]
//   --once    print at most once per clone (marker in .local/), used by the session-start hook
//   --quiet   print nothing when nothing is missing
//   --fix     run the repository-local fixes (Corepack shim, dependencies, git hooks)
//   --system  with --fix: also install system tools with the platform package manager (winget)
//   --dry-run with --fix: print the commands without running them
// An agent runs --fix only when the owner asks for the installs (skill setup-machine).
import { execFileSync, spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { delimiter, join } from 'node:path';
import { loadGates } from './lib/gates.mjs';
import { listFiles, root } from './lib/repo.mjs';

const args = process.argv.slice(2);
const marker = join(root, '.local/doctor-shown');
if (args.includes('--once') && existsSync(marker)) process.exit(0);

const { config, vars } = loadGates(root);
const LOCAL_STAGES = ['edit', 'turn-end', 'pre-commit', 'pre-push', 'verify'];
const windows = process.platform === 'win32';
const onPath = (tool) => {
  const exts = windows ? ['', ...(process.env.PATHEXT ?? '.EXE;.CMD;.BAT').split(';')] : [''];
  return (process.env.PATH ?? '').split(delimiter).some((dir) => dir && exts.some((ext) => existsSync(join(dir, tool + ext)) || existsSync(join(dir, tool + ext.toLowerCase()))));
};
let fileList;
const met = (requirement) => {
  if (requirement.startsWith('glob:')) {
    fileList ??= listFiles();
    return fileList.some((file) => new RegExp(requirement.slice(5)).test(file));
  }
  return requirement.split('|').some((path) => existsSync(join(root, path)));
};

// The .NET SDK major version from global.json, if any.
const globalJson = join(root, vars.dotnetDir, 'global.json');
const sdkMajor = existsSync(globalJson) ? (JSON.parse(readFileSync(globalJson, 'utf8')).sdk?.version ?? '9').split('.')[0] : '9';
const winget = (id) => ({ scope: 'system', command: ['winget', 'install', '--id', id, '-e', '--accept-source-agreements', '--accept-package-agreements'] });

/** tool -> { scope: repo|system|manual, command?: string[], text } */
const FIX = {
  // On Windows the Node folder needs admin rights; the user's npm folder (on PATH with Node) does not.
  pnpm: { scope: 'repo', command: windows && process.env.APPDATA ? ['corepack', 'enable', '--install-directory', join(process.env.APPDATA, 'npm')] : ['corepack', 'enable'], text: 'corepack enable (adds the pnpm shim; Corepack ships with Node)' },
  npx: { scope: 'manual', text: 'install Node.js LTS (includes npm and npx)' },
  npm: { scope: 'manual', text: 'install Node.js LTS (includes npm)' },
  dotnet: windows ? { ...winget(`Microsoft.DotNet.SDK.${sdkMajor}`), text: `winget install Microsoft.DotNet.SDK.${sdkMajor} (the SDK in global.json)` } : { scope: 'manual', text: 'install the .NET SDK from global.json (https://dot.net)' },
  gitleaks: windows ? { ...winget('Gitleaks.Gitleaks'), text: 'winget install Gitleaks.Gitleaks' } : { scope: 'manual', text: 'brew install gitleaks (or your package manager)' },
  docker: { scope: 'manual', text: windows ? 'install Docker Desktop' : 'install Docker Engine with the compose plugin' },
  jscpd: { scope: 'manual', text: 'not needed locally: the duplication gate runs in ci-local.mjs' }
};

const missing = new Map();
const used = new Set(LOCAL_STAGES.flatMap((stage) => config.stages?.[stage] ?? []));
for (const id of used) {
  const check = config.checks?.[id];
  if (!check || !(check.requires ?? []).every(met)) continue;
  for (const tool of check.tools ?? []) {
    if (onPath(tool)) continue;
    const entry = missing.get(tool) ?? { ...(FIX[tool] ?? { scope: 'manual', text: `install ${tool}` }), checks: [] };
    entry.checks.push(id);
    missing.set(tool, entry);
  }
}
if (existsSync(join(root, 'pnpm-lock.yaml')) && !existsSync(join(root, 'node_modules/.modules.yaml'))) {
  missing.set('node_modules', { scope: 'repo', command: ['corepack', 'pnpm', 'install', '--frozen-lockfile'], text: 'corepack pnpm install', checks: ['Biome, typecheck, clean-code, tests'] });
}
for (const dir of vars.npmDirs) {
  if (!existsSync(join(root, dir, 'package-lock.json')) || existsSync(join(root, dir, 'node_modules/.package-lock.json'))) continue;
  missing.set(`${dir}/node_modules`, { scope: 'repo', command: ['npm', 'ci', '--prefix', dir], text: `npm ci --prefix ${dir}`, checks: ['Prettier, ESLint, npm tests'] });
}
// Hooks live in the common git directory, which a worktree shares with its main checkout.
let hook = '';
try {
  hook = execFileSync('git', ['rev-parse', '--path-format=absolute', '--git-path', 'hooks/pre-commit'], { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
} catch {
  hook = '';
}
if (hook && existsSync(join(root, 'lefthook.yml')) && !(existsSync(hook) && readFileSync(hook, 'utf8').includes('lefthook'))) {
  missing.set('git hooks', { scope: 'repo', command: ['npx', '--yes', 'lefthook', 'install'], text: 'npx lefthook install', checks: ['pre-commit, commit-msg, pre-push'] });
}

if (!missing.size) {
  if (!args.includes('--quiet') && !args.includes('--once')) console.log('✓ doctor: everything the local gates need is installed');
} else if (args.includes('--fix')) {
  let failed = false;
  for (const [what, entry] of missing) {
    const runnable = entry.command && (entry.scope === 'repo' || (entry.scope === 'system' && args.includes('--system')));
    if (!runnable) {
      console.log(`○ ${what}: ${entry.text}${entry.scope === 'system' ? ' (add --system to install it)' : ' (install it yourself)'}`);
      continue;
    }
    const line = entry.command.map((part) => (/\s/.test(part) ?`"${part}"` : part)).join(' ');
    console.log(`▶ ${what}: ${line}`);
    if (args.includes('--dry-run')) continue;
    const result = spawnSync(line, { cwd: root, stdio: 'inherit', shell: true });
    if (result.status === 0) console.log(`✓ ${what}`);
    else {
      console.error(`✗ ${what}: exit ${result.status}`);
      failed = true;
    }
  }
  if (failed) process.exitCode = 1;
  console.log('Re-run node tooling/scripts/doctor.mjs to confirm; a new shell may be needed for new PATH entries.');
} else {
  console.log('Missing locally (gates skip these checks until installed; CI still runs them):');
  for (const [what, { text, checks }] of missing) console.log(`  - ${what}: ${text}  [${[...new Set(checks)].join(', ')}]`);
  console.log('Install the repository-local ones with: node tooling/scripts/doctor.mjs --fix (add --system for system tools),');
  console.log('or ask the agent to install them. Without installing: node tooling/scripts/ci-local.mjs <stage> runs everything in a container.');
}
if (args.includes('--once')) {
  mkdirSync(join(root, '.local'), { recursive: true });
  writeFileSync(marker, `${new Date().toISOString()}\n`);
}
