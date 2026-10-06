#!/usr/bin/env node
// commit-msg hook: Conventional Commits header and optional ticket trailers.
//   <type>(<scope>)?: <subject>    type: feat fix docs refactor perf test build ci chore revert
//   Refs: github:owner/repo#12 | gitea:owner/repo#12 | ticketeer:KEY-12   (also Closes:)
// feat and fix need a Refs/Closes trailer when REQUIRE_TICKET_REF=1 (set it in CI or lefthook).
import { readFileSync } from 'node:fs';

const file = process.argv[2];
const message = readFileSync(file, 'utf8').split(/\r?\n/).filter((line) => !line.startsWith('#')).join('\n').trim();
const header = message.split('\n')[0];
const errors = [];
const types = ['feat', 'fix', 'docs', 'refactor', 'perf', 'test', 'build', 'ci', 'chore', 'revert'];
const match = header.match(/^(\w+)(\([\w./-]+\))?(!)?: (.+)$/);
if (/^(Merge|Revert|fixup!|squash!)/.test(header)) process.exit(0);
if (!match) errors.push(`header must be "<type>(scope): subject", got: ${header}`);
else {
  if (!types.includes(match[1])) errors.push(`type "${match[1]}" is not one of ${types.join(', ')}`);
  if (header.length > 100) errors.push('header is longer than 100 characters');
}
const ref = /^(github|gitea):[\w.-]+\/[\w.-]+#\d+$|^ticketeer:[A-Za-z0-9][A-Za-z0-9-]*$/;
const trailers = [...message.matchAll(/^(Refs|Closes):\s*(.+)$/gm)];
for (const [, , value] of trailers) {
  for (const item of value.split(/[\s,]+/).filter(Boolean)) if (!ref.test(item)) errors.push(`ticket reference "${item}" must look like github:owner/repo#12, gitea:owner/repo#12 or ticketeer:KEY-12`);
}
if (process.env.REQUIRE_TICKET_REF === '1' && match && ['feat', 'fix'].includes(match[1]) && !trailers.length) errors.push('feat/fix commits need a "Refs:" or "Closes:" trailer');
if (errors.length) {
  console.error('✗ commit message:');
  for (const error of errors) console.error(`  ${error}`);
  process.exit(1);
}
