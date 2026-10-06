#!/usr/bin/env node
// PostToolUse after file edits (Claude and Codex). Runs the `edit` gate stage on the edited files:
// format (Biome, CSharpier), front-matter and language for Markdown, and the root layout. Problems go
// back to the agent with exit code 2 so it fixes them in the same turn instead of at commit time.
import { editedFiles, readEvent, repoRoot, runGate, tail } from './lib.mjs';

const event = readEvent();
const root = repoRoot(event);
const files = editedFiles(event, root).filter((file) => !file.startsWith('../'));
const { ok, output } = runGate(root, 'edit', files, 60_000);
if (!ok) {
  console.error(`Gate "edit" failed for ${files.join(', ') || 'the repository'}:\n${tail(output)}`);
  process.exit(2);
}
