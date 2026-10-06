#!/usr/bin/env node
// Keeps osv-scanner exceptions temporary: every [[IgnoredVulns]] entry in tooling/config/osv-scanner.toml
// needs an id, a reason and an ignoreUntil date (YYYY-MM-DD) at most 90 days ahead. An expired entry is
// reported as a note: osv-scanner already reports the finding again, and a fixed one should be removed.
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { report, root, today } from './lib/repo.mjs';

const MAX_DAYS = 90;
const file = 'tooling/config/osv-scanner.toml';
const errors = [];
const notes = [];
const text = existsSync(join(root, file)) ? readFileSync(join(root, file), 'utf8') : '';
const lines = text.split(/\r?\n/).map((line) => line.replace(/\s+#.*$/, '').trim());
const entries = [];
for (const line of lines) {
  if (line.startsWith('#') || !line) continue;
  if (/^\[\[\s*IgnoredVulns\s*\]\]$/.test(line)) entries.push({});
  else if (/^\[/.test(line)) entries.push(null);
  else if (entries.at(-1)) {
    const [, key, value] = line.match(/^(\w+)\s*=\s*(.+)$/) ?? [];
    if (key) entries.at(-1)[key] = value.replace(/^["']|["']$/g, '').trim();
  }
}
const limit = new Date(Date.now() + MAX_DAYS * 86400000).toISOString().slice(0, 10);
for (const entry of entries.filter(Boolean)) {
  const id = entry.id ?? '(no id)';
  if (!entry.id) errors.push(`${file}: an IgnoredVulns entry has no id`);
  if (!entry.reason) errors.push(`${file}: ${id} needs a reason (why it cannot be fixed now, and what it waits for)`);
  const until = entry.ignoreUntil?.slice(0, 10);
  if (!until || !/^\d{4}-\d{2}-\d{2}$/.test(until)) errors.push(`${file}: ${id} needs ignoreUntil = YYYY-MM-DD (at most ${MAX_DAYS} days ahead)`);
  else if (until > limit) errors.push(`${file}: ${id} ignoreUntil ${until} is more than ${MAX_DAYS} days ahead (latest ${limit})`);
  else if (until < today()) notes.push(`${id} expired on ${until}: remove it if the upgrade has landed`);
}
for (const note of notes) console.log(`  note: ${note}`);
report('osv exceptions', errors, { okMessage: `${entries.filter(Boolean).length} exception(s)` });
