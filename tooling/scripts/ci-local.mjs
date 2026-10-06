#!/usr/bin/env node
// Runs a gate stage (default: ci) with --strict inside the local CI image, so the full CI runs on your
// own machine with every scanner installed, without GitHub or any hosted runner. The working tree,
// including uncommitted changes, is copied into the container; host node_modules are not used.
// In a git worktree (.git is a file pointing outside the checkout) the container cannot read the
// history, so a temporary clone of HEAD with the working-tree changes on top is mounted instead.
// .NET and npm folders come from the vars in tooling/gates.yaml (dotnetDir, dotnetTarget, npmDirs), and its
// env is passed into the container.
// --docker mounts the host Docker socket so integration tests can start containers (Testcontainers,
// docker compose); they reach them through host.docker.internal. Opt-in: the socket is root on the host.
// Usage: node tooling/scripts/ci-local.mjs [stage] [--rebuild] [--docker]
import { createHash } from 'node:crypto';
import { execFileSync, spawnSync } from 'node:child_process';
import { cpSync, existsSync, mkdtempSync, readFileSync, rmSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, join } from 'node:path';
import { loadGates } from './lib/gates.mjs';
import { root } from './lib/repo.mjs';

const args = process.argv.slice(2);
const stage = args.find((arg) => !arg.startsWith('--')) ?? 'ci';
const { vars, env: gateEnv } = loadGates(root);
const dotnet = ['global.json', 'Directory.Build.props'].some((file) => existsSync(join(root, vars.dotnetDir, file)));
const npmDirs = vars.npmDirs.filter((dir) => existsSync(join(root, dir, 'package-lock.json')));
const withDocker = args.includes('--docker');
const nodeVersion = existsSync(join(root, '.nvmrc')) ? readFileSync(join(root, '.nvmrc'), 'utf8').trim().replace(/^v/, '') : '22';
const context = join(root, 'tooling/ci');
const digest = createHash('sha256').update(readFileSync(join(context, 'Dockerfile'))).update(readFileSync(join(context, 'entrypoint.sh'))).update(String(dotnet)).update(nodeVersion).digest('hex').slice(0, 12);
const image = `local-ci-${basename(root).toLowerCase().replace(/[^a-z0-9-]/g, '')}:${digest}`;
const docker = (dockerArgs) => spawnSync('docker', dockerArgs, { stdio: 'inherit' }).status ?? 1;
const git = (gitArgs, cwd = root) => execFileSync('git', gitArgs, { cwd, encoding: 'utf8' });

/** For a worktree: clone HEAD into a temp dir and overlay the working tree (changed, untracked, deleted). */
function snapshotWorktree() {
  const snapshot = mkdtempSync(join(tmpdir(), 'ci-local-'));
  git(['clone', '--quiet', '--no-hardlinks', '--no-checkout', root, snapshot]);
  git(['checkout', '--quiet', git(['rev-parse', 'HEAD']).trim()], snapshot);
  for (const file of git(['ls-files', '-com', '--exclude-standard', '-z']).split('\0').filter(Boolean)) {
    const from = join(root, file);
    if (existsSync(from) && statSync(from).isFile()) cpSync(from, join(snapshot, file), { force: true });
  }
  for (const file of git(['ls-files', '-d', '-z']).split('\0').filter(Boolean)) rmSync(join(snapshot, file), { force: true });
  return snapshot;
}

const isWorktree = existsSync(join(root, '.git')) && statSync(join(root, '.git')).isFile();
const source = isWorktree ? snapshotWorktree() : root;
if (isWorktree) console.log(`Git worktree: running against a snapshot in ${source}`);
try {
  const exists = spawnSync('docker', ['image', 'inspect', image], { stdio: 'ignore' }).status === 0;
  if (!exists || args.includes('--rebuild')) {
    console.log(`Building ${image} (Node ${nodeVersion}${dotnet ? ', .NET' : ''}) …`);
    if (docker(['build', '-t', image, '--build-arg', `NODE_VERSION=${nodeVersion}`, ...(dotnet ? ['--build-arg', 'WITH_DOTNET=true'] : []), context]) !== 0) process.exit(1);
  }
  const env = ['ZOT_URL', 'ZOT_TOKEN', 'IMAGES', 'REQUIRE_TICKET_REF'].flatMap((name) => (process.env[name] ? ['-e', name] : []));
  for (const [key, value] of Object.entries(gateEnv)) env.push('-e', `${key}=${process.env[key] ?? value}`);
  if (dotnet) env.push('-e', `DOTNET_TARGET=${vars.dotnetTarget}`);
  if (npmDirs.length) env.push('-e', `NPM_DIRS=${npmDirs.join(' ')}`);
  const dockerAccess = withDocker ? ['-v', '/var/run/docker.sock:/var/run/docker.sock', '--add-host', 'host.docker.internal:host-gateway', '-e', 'CI_DOCKER=1', '-e', 'TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal'] : [];
  process.exitCode = docker(['run', '--rm', '-v', `${source}:/src:ro`, ...dockerAccess, ...env, image, stage]);
} finally {
  if (isWorktree) rmSync(source, { recursive: true, force: true });
}

