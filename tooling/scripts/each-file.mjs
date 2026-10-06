#!/usr/bin/env node
// Runs a command once with every tracked file whose path matches a regex appended.
// Usage: node tooling/scripts/each-file.mjs "<regex>" <command> [args…]   e.g. "Dockerfile$" hadolint
import { spawnSync } from 'node:child_process';
import { listFiles, root } from './lib/repo.mjs';

const [pattern, command, ...rest] = process.argv.slice(2);
const files = listFiles().filter((file) => new RegExp(pattern).test(file));
if (!files.length) {
  console.log(`○ no files match ${pattern}`);
} else {
  const run = spawnSync(command, [...rest, ...files], { cwd: root, stdio: 'inherit', shell: process.platform === 'win32' });
  process.exitCode = run.status ?? 1;
}
