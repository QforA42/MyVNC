---
name: chunk-workflow
description: The loop for implementing one chunk of a phase from start to commit. Use whenever you start work on a chunk, or when a task is about to grow beyond one chunk.
---
# Chunk workflow

Full background: `project/agent-workflow.md`.

1. **Scope.** Find the chunk row in the phase plan and restate its "Done when". More than 5 files,
   more than 3 sub-steps, or "and" between two verbs: split it in the plan first and work on the first part.
2. **Readiness.** Check what the chunk depends on (earlier chunks committed, migrations, owner decisions
   in `project/review-queue.md`). Blocked: say so and stop, do not work around it.
3. **Explore cheaply.** Delegate searches to the `scout` role (T1). Read the `AGENTS.md` of every folder
   you will change.
4. **Implement** (yourself or the `implementer` role, T2). Fix what the edit hook reports immediately.
5. **Verify.** `node tooling/scripts/gate.mjs verify` (or delegate to `test-runner`, T1). A gate that
   fails twice on the same problem: escalate to T3 or use skill `debug-loop`.
6. **Decide or queue.** Any owner decision you could not get: least irreversible option, row in
   `project/review-queue.md`, row in the phase's autonomy record.
7. **Record.** Update the chunk row (status, verification command and result), `project/STATUS.md`
   and any docs whose behaviour changed, in the same commit.
8. **Commit** when asked or when the phase says so: one commit, Conventional Commits, `Refs:` trailer.
   Never commit red; never skip hooks.
9. **Stop.** One chunk per session. Start the next chunk in a new session so it begins with clean context.
