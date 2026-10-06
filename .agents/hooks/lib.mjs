// Shared helpers for agent hooks. One set of hook programs serves Claude Code and Codex: both send a
// JSON event on stdin (hook_event_name, cwd, session_id, transcript_path, tool_name, tool_input, …),
// and both read exit code 2 as "block, and show stderr to the agent".
import { execFileSync, spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { isAbsolute, join, relative, resolve } from 'node:path';

export function readEvent() {
  try {
    return JSON.parse(readFileSync(0, 'utf8') || '{}');
  } catch {
    return {};
  }
}

/** Repository root, resolved from the event cwd so hooks work when the agent runs in a subfolder. */
export function repoRoot(event) {
  const cwd = event.cwd ?? process.env.CLAUDE_PROJECT_DIR ?? process.cwd();
  try {
    return execFileSync('git', ['rev-parse', '--show-toplevel'], { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
  } catch {
    return resolve(cwd);
  }
}

/** Which agent sent the event: the launcher passes --agent, otherwise guess from the event shape. */
export function agentName(event) {
  const flag = process.argv.find((arg) => arg.startsWith('--agent='));
  if (flag) return flag.slice(8);
  // Claude Code sets CLAUDE_PROJECT_DIR for hook commands; Codex does not.
  return process.env.CLAUDE_PROJECT_DIR ? 'claude' : 'codex';
}

/** Shell command of a Bash tool call (Claude: command, Codex: command or cmd). */
export function shellCommand(event) {
  const input = event.tool_input ?? {};
  const value = input.command ?? input.cmd ?? '';
  return Array.isArray(value) ? value.join(' ') : String(value);
}

/** Repository-relative POSIX paths a tool call writes (Edit/Write/MultiEdit file_path, apply_patch headers). */
export function editedFiles(event, root) {
  const input = event.tool_input ?? {};
  const cwd = event.cwd ?? root;
  const paths = [];
  if (input.file_path) paths.push(input.file_path);
  for (const edit of input.edits ?? []) if (edit.file_path) paths.push(edit.file_path);
  const patch = typeof input === 'string' ? input : input.command ?? input.input ?? input.patch ?? '';
  for (const match of String(Array.isArray(patch) ? patch.join('\n') : patch).matchAll(/^\*\*\* (?:Add File|Update File|Delete File|Move to): (.+)$/gm)) paths.push(match[1].trim());
  return [...new Set(paths.map((path) => relative(root, isAbsolute(path) ? path : resolve(cwd, path)).replaceAll('\\', '/')))];
}

/** Changed, staged and untracked files in the working tree, as repository-relative paths. */
export function changedFiles(root) {
  try {
    return execFileSync('git', ['status', '--porcelain', '-z', '--untracked-files=all'], { cwd: root, encoding: 'utf8' })
      .split('\0')
      .filter(Boolean)
      .map((line) => line.slice(3))
      .filter((path) => path && !path.startsWith('.local/'));
  } catch {
    return [];
  }
}

/** Runs a gate stage on the given files; returns { ok, output }. */
export function runGate(root, stage, files = [], timeout = 120_000) {
  const args = [join(root, 'tooling/scripts/gate.mjs'), stage];
  if (files.length) args.push('--files', ...files);
  const result = spawnSync(process.execPath, args, { cwd: root, encoding: 'utf8', timeout });
  const output = `${result.stdout ?? ''}${result.stderr ?? ''}${result.error ? `\n${result.error.message}` : ''}`;
  return { ok: result.status === 0, output };
}

/** Keeps only the useful tail of gate output for the agent's context. */
export const tail = (text, lines = 40) => text.trim().split('\n').slice(-lines).join('\n');
