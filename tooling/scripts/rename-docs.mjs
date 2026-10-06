#!/usr/bin/env node
// Renames documents and folders and keeps every reference working (ADR 0007 in repo-blueprint):
//   1. git mv for each entry of the rename map (files or folders);
//   2. rewrites relative links in Markdown and HTML files (in moved files and links to moved files);
//   3. appends the old path to `previous_paths` in the front matter of each moved Markdown file;
//   4. records every file move in a path map (`moves`: old -> new) for files without front matter;
//   5. lists remaining literal mentions of old paths (scripts, config, prose) to fix by hand.
// The rename map is JSON: { "old/path": "new/path", ... }. Without --apply nothing is written.
// Usage: node tooling/scripts/rename-docs.mjs <map.json> [--apply] [--path-map project/path-map-YYYY-MM.json]
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, readFileSync, rmdirSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, posix } from 'node:path';
import { git, listFiles, root } from './lib/repo.mjs';

const args = process.argv.slice(2);
const apply = args.includes('--apply');
const mapFile = args.find((arg) => !arg.startsWith('--') && args[args.indexOf(arg) - 1] !== '--path-map');
if (!mapFile) {
  console.error('usage: rename-docs.mjs <map.json> [--apply] [--path-map project/path-map-YYYY-MM.json]');
  process.exit(2);
}
const pathMapFile = args.includes('--path-map') ? args[args.indexOf('--path-map') + 1] : `project/path-map-${new Date().toISOString().slice(0, 7)}.json`;
const renames = Object.entries(JSON.parse(readFileSync(mapFile, 'utf8'))).map(([from, to]) => [from.replace(/\/$/, ''), to.replace(/\/$/, '')]);

const tracked = listFiles().map((file) => file.replaceAll('\\', '/'));
const trackedSet = new Set(tracked);
const isDirPath = (path) => tracked.some((file) => file.startsWith(`${path}/`));

// File-level map old -> new.
const moves = new Map();
for (const [from, to] of renames) {
  if (trackedSet.has(from)) moves.set(from, to);
  else if (isDirPath(from)) for (const file of tracked.filter((f) => f.startsWith(`${from}/`))) moves.set(file, `${to}${file.slice(from.length)}`);
  else throw new Error(`not tracked: ${from}`);
  if (trackedSet.has(from) && existsSync(join(root, to)) && from.toLowerCase() !== to.toLowerCase()) throw new Error(`target exists: ${to}`);
}
const mapPath = (path) => {
  if (moves.has(path)) return moves.get(path);
  for (const [from, to] of renames) if (path === from || path.startsWith(`${from}/`)) return `${to}${path.slice(from.length)}`;
  return path;
};
const exists = (path) => path === '' || trackedSet.has(path) || isDirPath(path);

const TEXT = /\.(md|html)$/;
const LINK = /(\]\(\s*<?|\b(?:href|src)=["'])([^)"'>\s]+)/g;
/** Rewrites the relative links of one document that lived at oldPath and now lives at newPath. */
export function rewriteLinks(content, oldPath, newPath) {
  let changed = 0;
  const text = content.replace(LINK, (match, lead, href) => {
    if (/^([a-z][a-z0-9+.-]*:|\/|#)/i.test(href)) return match;
    const [target, suffix = ''] = href.split(/(?=[#?])/, 2);
    if (!target) return match;
    let decoded = target;
    try {
      decoded = decodeURI(target);
    } catch {}
    const resolved = posix.normalize(posix.join(posix.dirname(oldPath), decoded)).replace(/\/$/, '');
    if (resolved.startsWith('..') || !exists(resolved)) return match;
    const moved = mapPath(resolved);
    if (moved === resolved && oldPath === newPath) return match;
    let next = posix.relative(posix.dirname(newPath), moved) || '.';
    if (target.endsWith('/')) next += '/';
    if (`${next}${suffix}` === href) return match;
    changed++;
    return `${lead}${encodeURI(next)}${suffix}`;
  });
  return { text, changed };
}

/** Adds oldPath to previous_paths in the front matter (flow list), if the document has front matter. */
export function addPreviousPath(content, oldPath) {
  const match = content.match(/^---\r?\n([\s\S]*?)\r?\n---\r?\n/);
  if (!match) return null;
  const lines = match[1].split(/\r?\n/);
  const index = lines.findIndex((line) => line.startsWith('previous_paths:'));
  if (index >= 0) {
    const items = lines[index].replace(/^previous_paths:\s*\[?|\]\s*$/g, '').split(',').map((item) => item.trim()).filter(Boolean);
    if (!items.includes(oldPath)) items.push(oldPath);
    lines[index] = `previous_paths: [${items.join(', ')}]`;
  } else lines.push(`previous_paths: [${oldPath}]`);
  return content.replace(match[0], `---\n${lines.join('\n')}\n---\n`);
}

// Plan the content changes from the current (old) files.
const documents = tracked.filter((file) => TEXT.test(file));
const writes = new Map();
let linkChanges = 0;
for (const oldPath of documents) {
  const newPath = mapPath(oldPath);
  let content = readFileSync(join(root, oldPath), 'utf8');
  const { text, changed } = rewriteLinks(content, oldPath, newPath);
  linkChanges += changed;
  content = text;
  if (moves.has(oldPath) && oldPath.endsWith('.md')) content = addPreviousPath(content, oldPath) ?? content;
  if (content !== readFileSync(join(root, oldPath), 'utf8')) writes.set(newPath, content);
}

for (const [from, to] of renames) console.log(`${apply ? 'move' : 'would move'} ${from} -> ${to}`);
console.log(`${moves.size} file(s), ${linkChanges} link(s) in ${writes.size} document(s)`);

if (apply) {
  // Move file by file, never a whole folder: git-ignored files (local notes, credentials) stay where
  // their ignore rule covers them instead of becoming trackable at the new path.
  for (const [from, to] of moves) {
    mkdirSync(dirname(join(root, to)), { recursive: true });
    if (from.toLowerCase() === to.toLowerCase() && from !== to) {
      git(['mv', from, `${to}.rename-tmp`]);
      git(['mv', `${to}.rename-tmp`, to]);
    } else git(['mv', from, to]);
  }
  const leftBehind = renames.filter(([from]) => !trackedSet.has(from) && existsSync(join(root, from))).flatMap(([from]) => {
    const found = [];
    const walk = (dir) => {
      for (const name of readdirSync(join(root, dir))) {
        const path = `${dir}/${name}`;
        if (statSync(join(root, path)).isDirectory()) walk(path);
        else found.push(path);
      }
    };
    walk(from);
    return found;
  });
  if (leftBehind.length) console.log(`left in place (not tracked by git, so not moved): ${leftBehind.join(', ')}`);
  // git mv leaves emptied folders on disk; remove them so links to them fail locally as they do in CI.
  for (const from of new Set([...moves.keys()].map((file) => posix.dirname(file)))) {
    for (let dir = from; dir && dir !== '.'; dir = posix.dirname(dir)) {
      try {
        if (readdirSync(join(root, dir)).length) break;
        rmdirSync(join(root, dir));
      } catch {
        break;
      }
    }
  }
  for (const [path, content] of writes) writeFileSync(join(root, path), content);
  const mapTarget = join(root, pathMapFile);
  const pathMap = existsSync(mapTarget) ? JSON.parse(readFileSync(mapTarget, 'utf8')) : { description: 'Old and new paths of renamed files (repo-blueprint ADR 0007). Keys are old paths, values are current paths.' };
  pathMap.moves = { ...(pathMap.moves ?? {}), ...Object.fromEntries(moves) };
  mkdirSync(dirname(mapTarget), { recursive: true });
  writeFileSync(mapTarget, `${JSON.stringify(pathMap, null, 2)}\n`);
  execFileSync('git', ['add', '-A', '--', ...new Set([...writes.keys(), pathMapFile])], { cwd: root });
  console.log(`path map: ${pathMapFile}`);
}

// Literal mentions of old paths outside links: scripts, config and prose that tools read.
const literal = [];
const textFiles = tracked.filter((file) => /\.(md|html|mjs|js|ts|json|ya?ml|toml|sh|ps1)$/.test(file) && file !== pathMapFile && file !== mapFile.replaceAll('\\', '/') &&!/(^|\/)path-map[^/]*\.json$/.test(file));
for (const file of textFiles) {
  const current = apply ? mapPath(file) : file;
  if (!existsSync(join(root, current))) continue;
  const content = readFileSync(join(root, current), 'utf8').replace(/^---\r?\n[\s\S]*?\r?\n---\r?\n/, '');
  for (const [from] of renames) {
    const pattern = new RegExp(`(^|[^\\w./-])${from.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}(?![\\w-])`);
    if (pattern.test(content.replace(LINK, ''))) literal.push(`${current}: ${from}`);
  }
}
if (literal.length) {
  console.log(`\n${literal.length} literal mention(s) of old paths (check by hand; historical records may keep them):`);
  for (const line of literal) console.log(`  ${line}`);
}
if (!apply) console.log('\nDry run. Re-run with --apply to rename.');
