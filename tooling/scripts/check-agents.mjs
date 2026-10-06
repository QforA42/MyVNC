#!/usr/bin/env node
// Keeps agent instructions small and consistent:
// - root AGENTS.md and nested AGENTS.md within byte budgets (procedures belong in .agents/skills);
// - every folder with an AGENTS.md has a CLAUDE.md stub that imports it (@AGENTS.md) and adds nothing;
// - no CLAUDE.md without an AGENTS.md next to it.
// Folders listed in .agents/agents.config.json "exclude" are skipped.
import { existsSync, readFileSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { listFiles, readJson, report, root } from './lib/repo.mjs';

const defaults = { rootAgentsBytes: 8192, nestedAgentsBytes: 3072, claudeStubBytes: 400 };
const config = existsSync(join(root, '.agents/agents.config.json')) ? readJson('.agents/agents.config.json') : {};
const limits = { ...defaults, ...config.limits };
// exclude: folders (for example read-only templates) whose AGENTS.md and CLAUDE.md are not checked.
const excluded = (file) => (config.exclude ?? []).some((dir) => file.startsWith(`${dir.replace(/\/+$/, '')}/`));
const files = listFiles().filter((file) => !excluded(file));
const errors = [];
const dirOf = (file) => (dirname(file) === '.' ? '' : `${dirname(file)}/`);

for (const file of files.filter((path) => path === 'AGENTS.md' || path.endsWith('/AGENTS.md'))) {
  if (file.startsWith('.agents/') || file.startsWith('template/')) continue;
  const size = statSync(join(root, file)).size;
  const limit = file === 'AGENTS.md' ? limits.rootAgentsBytes : limits.nestedAgentsBytes;
  if (size > limit) errors.push(`${file} is ${size} bytes, budget ${limit}; move procedures into .agents/skills`);
  const stub = `${dirOf(file)}CLAUDE.md`;
  if (!existsSync(join(root, stub))) errors.push(`${stub} is missing; it should contain only "@AGENTS.md"`);
}
for (const file of files.filter((path) => path === 'CLAUDE.md' || path.endsWith('/CLAUDE.md'))) {
  if (file.startsWith('template/')) continue;
  const text = readFileSync(join(root, file), 'utf8');
  if (!text.includes('@AGENTS.md')) errors.push(`${file} must import @AGENTS.md`);
  if (Buffer.byteLength(text) > limits.claudeStubBytes) errors.push(`${file} holds rules of its own; keep rules in AGENTS.md (single source)`);
  if (!existsSync(join(root, `${dirOf(file)}AGENTS.md`))) errors.push(`${file} has no AGENTS.md next to it`);
}
// Skills in .agents/skills are read directly by Codex and copied to .claude/skills for Claude Code.
// Both need a SKILL.md whose front matter has a name equal to the folder (lower-case, hyphens,
// at most 64 characters) and a description of at most 1024 characters that says when to use it.
for (const file of files.filter((path) => /^\.agents\/skills\/[^/]+\/SKILL\.md$/.test(path))) {
  const folder = file.split('/')[2];
  const head = readFileSync(join(root, file), 'utf8').match(/^---\r?\n([\s\S]*?)\r?\n---/)?.[1] ?? '';
  const name = head.match(/^name:\s*(.+)$/m)?.[1].trim();
  const description = head.match(/^description:\s*(.+)$/m)?.[1].trim() ?? '';
  if (name !== folder) errors.push(`${file}: front matter name "${name ?? ''}" must equal the folder name "${folder}"`);
  if (!/^[a-z0-9]+(-[a-z0-9]+)*$/.test(folder) || folder.length > 64) errors.push(`${file}: skill folder names are lower-case kebab-case, at most 64 characters`);
  if (!description || description.length > 1024) errors.push(`${file}: description is required and at most 1024 characters (Codex and Claude Code use it to pick the skill)`);
}
report('agent instructions', errors);
