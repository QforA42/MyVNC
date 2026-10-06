#!/usr/bin/env node
// Documentation is English (ADR 0004 in repo-blueprint). Swedish text must be saved as a translation
// next to its source: <name>.sv.md. This check flags Markdown files that read as Swedish but are not
// named *.sv.md. It is a heuristic over prose only (code blocks, inline code, links and tables of
// identifiers are ignored), tuned so that a few Swedish names or quotes in English text pass.
//
// Ratchet for repositories that adopt the blueprint with existing Swedish pages:
// tooling/config/language-baseline.json { "files": [...] } lists pages that may stay Swedish until
// translated. A new Swedish page fails; a listed page that no longer reads as Swedish is reported so
// it can be removed from the list (--update-baseline rewrites the list from the current state, only
// ever removing entries).
// Usage: node tooling/scripts/check-language.mjs [--files a.md b.md] [--update-baseline] [--init-baseline]
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { listFiles, report, root } from './lib/repo.mjs';

const SWEDISH = new Set(('och att det är som för inte med på av till den ska eller har kan vi du jag ett en om när men så också från vid alla efter utan under mellan finns måste genom vara blir eftersom detta dessa denna sedan redan bara här där hur vad vilka vilken varför nu nästa ingen inga mycket mer mest samma')
  .split(' '));
const args = process.argv.slice(2);
const filesIndex = args.indexOf('--files');
const explicit = filesIndex >= 0 ? args.slice(filesIndex + 1).filter((arg) => !arg.startsWith('--')) : null;
const baselinePath = join(root, 'tooling/config/language-baseline.json');
const baselineData = existsSync(baselinePath) ? JSON.parse(readFileSync(baselinePath, 'utf8')) : {};
const baseline = new Set(baselineData.files ?? []);
// "paths": folders that stay Swedish as a whole until an owner-approved change (for example a generated
// reference whose checks require Swedish headings). New pages there are allowed too.
const baselinePaths = baselineData.paths ?? [];

/** Returns { words, swedish, ratio } for the prose of a Markdown document. */
export function swedishScore(markdown) {
  const prose = markdown
    .replace(/^---\r?\n[\s\S]*?\r?\n---\r?\n/, '')
    .replace(/```[\s\S]*?```/g, ' ')
    .replace(/`[^`\n]*`/g, ' ')
    .replace(/\]\([^)]*\)/g, ']')
    .replace(/<[^>]+>/g, ' ')
    .toLowerCase();
  const words = prose.match(/[a-zåäöéü]+/g) ?? [];
  const swedish = words.filter((word) => SWEDISH.has(word) || /[åäö]/.test(word)).length;
  return { words: words.length, swedish, ratio: words.length ? swedish / words.length : 0 };
}

const looksSwedish = ({ words, swedish, ratio }) => words >= 20 && swedish >= 8 && ratio >= 0.08;

const files = (explicit ?? listFiles())
  .map((file) => file.replaceAll('\\', '/'))
  .filter((file) => file.endsWith('.md') && !/\.sv\.md$/.test(file))
  .filter((file) => !file.startsWith('node_modules/') && existsSync(join(root, file)));

const errors = [];
const swedishFiles = [];
for (const file of files) {
  const score = swedishScore(readFileSync(join(root, file), 'utf8'));
  if (!looksSwedish(score)) continue;
  swedishFiles.push(file);
  if (baseline.has(file) || baselinePaths.some((prefix) => file.startsWith(prefix)) || args.includes('--init-baseline')) continue;
  const target = file.replace(/\.md$/, '.sv.md');
  errors.push(`${file} reads as Swedish (${Math.round(score.ratio * 100)}% Swedish words). Documentation is English: write ${file} in English and save the Swedish text as ${target} (lang: sv, translation_of: <source id>)`);
}

if (args.includes('--init-baseline') || args.includes('--update-baseline')) {
  const keep = args.includes('--init-baseline') ? swedishFiles : [...baseline].filter((file) => swedishFiles.includes(file));
  writeFileSync(baselinePath, `${JSON.stringify({ $comment: 'Pages that may stay Swedish until translated (repo-blueprint ADR 0004). The list only shrinks: check-language.mjs --update-baseline removes translated pages.', files: keep.sort() }, null, 2)}\n`);
  console.log(`language baseline: ${keep.length} file(s)`);
}
if (!explicit) {
  const translated = [...baseline].filter((file) => existsSync(join(root, file)) && !swedishFiles.includes(file));
  if (translated.length) console.log(`○ language: ${translated.length} baseline page(s) now read as English; run --update-baseline to shrink the list`);
}
report('language', errors, { okMessage: `${files.length} Markdown file(s) checked${baseline.size ? `, ${baseline.size} Swedish page(s) in the baseline` : ''}` });
