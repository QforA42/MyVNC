#!/usr/bin/env node
// Stop (Claude and Codex): before the agent ends its turn, run the `turn-end` gate on every file changed
// in the working tree. On failure the turn continues (exit code 2) so the agent fixes the problem.
// stop_hook_active prevents loops: a turn already continued once by this hook is allowed to end, and
// the failure is left for the git hooks to catch.
import { changedFiles, readEvent, repoRoot, runGate, tail } from './lib.mjs';

const event = readEvent();
if (event.stop_hook_active) process.exit(0);
const root = repoRoot(event);
const files = changedFiles(root);
if (!files.length) process.exit(0);
const { ok, output } = runGate(root, 'turn-end', files, 300_000);
if (!ok) {
  console.error(`Before you finish: gate "turn-end" failed on the files you changed. Fix it, or say why it cannot pass.\n${tail(output)}`);
  process.exit(2);
}
