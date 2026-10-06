#!/usr/bin/env node
// Fails when the repository root holds an entry that is not in tooling/config/root-allowlist.json,
// or when more visible (non-dot) entries exist than maxVisible. Ignored files are not counted.
// An allow entry may use * as a wildcard (for example "*.sln").
import { listFiles, readJson, report } from './lib/repo.mjs';

const { allow, maxVisible } = readJson('tooling/config/root-allowlist.json');
const escape = (text) => text.replace(/[.+?^${}()|[\]\\]/g, '\\$&');
const patterns = allow.map((entry) => new RegExp(`^${entry.split('*').map(escape).join('[^/]*')}$`));
const entries = [...new Set(listFiles().map((file) => file.split('/')[0]))].sort();
const errors = entries.filter((entry) => !patterns.some((pattern) => pattern.test(entry))).map((entry) => `"${entry}" is not allowed in the root; move it (see docs) or add it to tooling/config/root-allowlist.json deliberately`);
const visible = entries.filter((entry) => !entry.startsWith('.'));
if (maxVisible && visible.length > maxVisible) errors.push(`${visible.length} visible root entries, budget is ${maxVisible}: ${visible.join(', ')}`);
report('layout', errors, { okMessage: `${visible.length}/${maxVisible} visible root entries` });
