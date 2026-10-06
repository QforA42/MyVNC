#!/usr/bin/env node
// PreToolUse (Claude and Codex). Blocks, with exit code 2 and a reason the agent can act on:
// - shell commands that bypass gates or rewrite shared history (--no-verify, commit -n, LEFTHOOK=0, force push);
// - edits to generated agent files (edit .agents/ and run sync-agents instead);
// - edits to secret files (.env, keys); secrets live in Infisical.
import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { editedFiles, readEvent, repoRoot, shellCommand } from './lib.mjs';

const event = readEvent();
const root = repoRoot(event);
const block = (reason) => {
  console.error(`Blocked by .agents/hooks/pre-tool.mjs: ${reason}`);
  process.exit(2);
};

const SHELL_RULES = [
  [/\bgit\b[^\n;&|]*\s--no-verify\b/, 'git --no-verify skips the gates. Fix the failing check instead.'],
  [/\bgit\s+commit\b(?:\s+-[a-zA-Z]+)*\s+-[a-mo-zA-Z]*n[a-zA-Z]*(?=\s|$)/, 'git commit -n skips the gates. Fix the failing check instead.'],
  [/\bgit\s+push\b[^\n;&|]*\s(--force(?!-with-lease)\b|-f\b)/, 'Force push is a hard stop. Ask the owner.'],
  [/\b(LEFTHOOK|HUSKY)=0\b/, 'Disabling git hooks skips the gates.'],
  [/\bgit\s+config\b[^\n;&|]*core\.hooksPath/, 'Changing core.hooksPath disables the gates.']
];

const tool = event.tool_name ?? '';
// Every shell tool, including PowerShell on Windows hosts.
if (['Bash', 'PowerShell', 'shell', 'exec_command'].includes(tool)) {
  const command = shellCommand(event);
  for (const [pattern, reason] of SHELL_RULES) if (pattern.test(command)) block(reason);
  process.exit(0);
}

const files = editedFiles(event, root);
if (!files.length) process.exit(0);
const configPath = join(root, '.agents/agents.config.json');
const skillTargets = existsSync(configPath) ? JSON.parse(readFileSync(configPath, 'utf8')).skillTargets ?? [] : [];
const generatedDirs = ['.claude/agents/', '.codex/agents/', '.codex/roles/', ...skillTargets.map((dir) => `${dir}/`)];
const generatedFiles = ['.codex/config.toml', '.codex/hooks.json'];
for (const file of files) {
  if (file.startsWith('../')) block(`${file} is outside the repository.`);
  if (generatedFiles.includes(file) || generatedDirs.some((dir) => file.startsWith(dir))) {
    block(`${file} is generated. Edit .agents/ and run node tooling/scripts/sync-agents.mjs.`);
  }
  const name = file.split('/').pop();
  if (/^\.env(\..+)?$/.test(name) && name !== '.env.example') block(`${file} holds secrets. Use Infisical; never write secret values to files.`);
  if (/\.(pem|key|p12|pfx)$/.test(file)) block(`${file} looks like a private key. Keys never go into the repository.`);
}
