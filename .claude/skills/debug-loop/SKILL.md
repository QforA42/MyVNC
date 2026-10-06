---
name: debug-loop
description: Bounded debugging procedure. Use when a bug, failing test or broken gate is not fixed by the first obvious change.
---
# Debug loop

1. Copy `project/templates/debug.md` to `.local/debug-<issue>.md` (ignored by git). Write the symptom
   and the exact command that reproduces it.
2. Reproduce before changing anything. No reproduction, no fix: report what you tried.
3. Write one hypothesis at a time with the observation that would refute it, then test it.
4. **Ceilings:** at most 3 hypotheses and 20 turns. When either is reached, stop, write the state
   to the debug file and hand over (or escalate to a T3 agent) with a clean session.
5. Fix with a test that fails before and passes after. Run `node tooling/scripts/gate.mjs pre-push`.
6. If the cause was non-obvious, record it as a reusable finding (ctxMem) and, when it is a trap
   specific to one folder, as one line in that folder's AGENTS.md.
