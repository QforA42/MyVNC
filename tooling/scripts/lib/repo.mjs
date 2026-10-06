// Shared helpers: repository root, file listing that respects .gitignore, small output helpers.
import { execFileSync } from 'node:child_process';
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';

export const root = process.cwd();

export const toPosix = (path) => path.split(sep).join('/');

export function readJson(path) {
  return JSON.parse(readFileSync(join(root, path), 'utf8'));
}

export function git(args, options = {}) {
  return execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'], ...options });
}

export function isGitRepo() {
  try {
    git(['rev-parse', '--is-inside-work-tree']);
    return true;
  } catch {
    return false;
  }
}

/** Tracked and untracked-but-not-ignored files, as POSIX paths. Falls back to a directory walk outside git. */
export function listFiles() {
  if (isGitRepo()) {
    return git(['ls-files', '--cached', '--others', '--exclude-standard', '-z'])
      .split('\0')
      .filter(Boolean)
      .filter((file) => existsSync(join(root, file)));
  }
  const files = [];
  const walk = (dir) => {
    for (const name of readdirSync(dir)) {
      if (name === '.git' || name === 'node_modules' || name === '.local') continue;
      const full = join(dir, name);
      if (statSync(full).isDirectory()) walk(full);
      else files.push(toPosix(relative(root, full)));
    }
  };
  walk(root);
  return files;
}

export function stagedFiles() {
  if (!isGitRepo()) return [];
  return git(['diff', '--cached', '--name-only', '--diff-filter=ACMR', '-z']).split('\0').filter(Boolean);
}

export function report(name, errors, { okMessage } = {}) {
  if (errors.length) {
    console.error(`✗ ${name}: ${errors.length} problem(s)`);
    for (const error of errors) console.error(`  ${error}`);
    process.exitCode = 1;
  } else {
    console.log(`✓ ${name}${okMessage ? `: ${okMessage}` : ''}`);
  }
}

export const today = () => new Date().toISOString().slice(0, 10);
