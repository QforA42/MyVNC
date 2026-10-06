#!/usr/bin/env node
// Naming standard for documents and folders under docs/ and project/ (ADR 0007 in repo-blueprint):
// English lower-case kebab-case names (well-known upper-case files and version numbers excepted), dates
// first (YYYY-MM-DD-slug), the folder carries the type, ADRs in docs/adr/NNNN-slug.md, one folder per
// phase (project/phases/phase-NNN-slug/), templates only in project/templates/.
//
// Ratchet: tooling/config/names-baseline.json { "files": [...], "paths": [...] } lists existing names
// that may stay until they are renamed (tooling/scripts/rename-docs.mjs). "files" are exact paths (a
// file or a folder), "paths" are prefixes whose whole subtree is exempt (for example a generated
// reference whose names are registry types). --init-baseline writes every current violation,
// --update-baseline only removes entries that are fixed.
// Usage: node tooling/scripts/check-names.mjs [--files a.md b.md] [--init-baseline] [--update-baseline]
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { listFiles, report, root } from './lib/repo.mjs';

const ROOTS = ['docs/', 'project/'];
const UPPER = new Set(['README', 'AGENTS', 'CLAUDE', 'STATUS', 'CHANGELOG', 'LICENSE', 'SECURITY', 'CONTRIBUTING']);
const DOC = /\.(md|html)$/;
const DATE = /\d{4}-\d{2}-\d{2}/;
const VERSION = /\d+\.\d+\.\d+(?:-[a-z0-9]+(?:\.[a-z0-9]+)*)?/g;
// Folders whose name says the document type; a file in them must not repeat it ("reviews/review-x.md").
const TYPED = { reviews: 'review', revisions: 'revision', proposals: 'proposal', protocols: 'protocol', releases: 'release', handoffs: 'handoff', plans: 'plan', reports: 'report' };

const args = process.argv.slice(2);
const filesIndex = args.indexOf('--files');
const explicit = filesIndex >= 0 ? args.slice(filesIndex + 1).filter((arg) => !arg.startsWith('--')) : null;
const baselinePath = join(root, 'tooling/config/names-baseline.json');
const baselineData = existsSync(baselinePath) ? JSON.parse(readFileSync(baselinePath, 'utf8')) : {};
const baseline = new Set(baselineData.files ?? []);
const baselinePaths = baselineData.paths ?? [];

export const stem = (name) => name.replace(/\.[^.]+$/, '').replace(/\.[a-z]{2}$/, '');
export const isKebab = (text) => /^[a-z0-9]+(-[a-z0-9]+)*$/.test(text.replace(VERSION, '0'));

/** Problems for one path segment (a folder when isDir, else a document) at `path`. */
export function segmentProblems(path, isDir) {
  const parts = path.split('/');
  const name = parts.at(-1);
  const parent = parts.at(-2) ?? '';
  const base = isDir ? name : stem(name);
  const problems = [];
  if (!(UPPER.has(base) && !isDir) && !isKebab(base)) problems.push('name is not English lower-case kebab-case (a-z, 0-9, hyphens; versions like 1.2.3 allowed)');
  const date = base.match(DATE);
  if (date && date.index !== 0) problems.push(`the date goes first: ${date[0]}-${base.replace(date[0], '').replace(/--+/g, '-').replace(/^-|-$/g, '')}`);
  const typed = TYPED[parent];
  if (typed && !isDir && base.startsWith(`${typed}-`) && !(parent === 'protocols' && base.startsWith('release-'))) problems.push(`the folder "${parent}" already says the type: drop "${typed}-"`);
  if (!isDir && /(^|-)template$|^template-/.test(base)) problems.push(path.startsWith('project/templates/') ? 'the folder already says it is a template: name it <type>.md' : 'templates live in project/templates/<type>.md');
  if (isDir && path === 'docs/decisions') problems.push('ADRs live in docs/adr/');
  if (!isDir && parent === 'adr' && path.startsWith('docs/adr/') && parts.length === 3 && base !== 'README' && !/^\d{4}-[a-z0-9-]+$/.test(base)) problems.push('ADR files are named NNNN-slug.md');
  if (parts.length === 3 && path.startsWith('project/phases/')) {
    if (!isDir && base !== 'README') problems.push('a phase is a folder: project/phases/phase-NNN-slug/plan.md');
    if (isDir && !/^phase-\d{3}-[a-z0-9-]+$/.test(name)) problems.push('phase folders are named phase-NNN-slug (three digits)');
  }
  return problems;
}

const files = (explicit ?? listFiles())
  .map((file) => file.replaceAll('\\', '/'))
  .filter((file) => ROOTS.some((prefix) => file.startsWith(prefix)));

const findings = new Map();
for (const file of files) {
  const parts = file.split('/');
  for (let i = 2; i <= parts.length; i++) {
    const isDir = i < parts.length;
    const path = parts.slice(0, i).join('/');
    if (findings.has(path) || parts[i - 1].startsWith('.')) continue;
    if (!isDir && !DOC.test(path)) continue;
    const problems = segmentProblems(path, isDir);
    if (problems.length) findings.set(path, problems);
  }
}

const exempt = (path) => baseline.has(path) || baselinePaths.some((prefix) => path.startsWith(prefix));
const errors = [...findings].filter(([path]) => !exempt(path) && !args.includes('--init-baseline')).map(([path, problems]) => `${path}: ${problems.join('; ')}`);

if (args.includes('--init-baseline') || args.includes('--update-baseline')) {
  const keep = args.includes('--init-baseline') ? [...findings.keys()] : [...baseline].filter((path) => findings.has(path));
  writeFileSync(baselinePath, `${JSON.stringify({ $comment: 'Names that may stay until renamed (repo-blueprint ADR 0007). The list only shrinks: check-names.mjs --update-baseline removes renamed entries.', files: keep.sort(), paths: baselinePaths }, null, 2)}\n`);
  console.log(`names baseline: ${keep.length} entr${keep.length === 1 ? 'y' : 'ies'}`);
}
if (!explicit) {
  const fixed = [...baseline].filter((path) => !findings.has(path));
  if (fixed.length) console.log(`○ names: ${fixed.length} baseline entr${fixed.length === 1 ? 'y is' : 'ies are'} fixed or gone; run --update-baseline to shrink the list`);
}
report('names', errors, { okMessage: `${files.length} path(s) under docs/ and project/ checked${baseline.size ? `, ${baseline.size} name(s) in the baseline` : ''}` });
