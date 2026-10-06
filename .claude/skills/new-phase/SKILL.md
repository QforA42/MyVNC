---
name: new-phase
description: Plans a new delivery phase from a ticket or a proposal in project/proposals/. Use when the owner promotes work into a phase or asks for a phase plan.
---
# New phase

1. Read `project/STATUS.md`, the source ticket or proposal and the ADRs and specs the work touches.
2. If the source is a proposal, set its `status: promoted` and link the new phase from it.
3. Create `project/phases/phase-NNN-slug/plan.md` from `project/templates/phase.md`. The next number is
   one higher than the highest folder in `project/phases/`.
4. Fill the readiness check with facts you verified (file paths, migration numbers, current
   behaviour), not assumptions.
5. Split the work into chunks: one session, one commit, at most 5 files, one verb. Each chunk gets a
   "Done when" that names a command and its expected result.
6. Architecture-significant choices get an ADR with status `proposed` (role: architect, T3).
7. List open owner decisions with a proposed default for each.
8. Run `node tooling/scripts/gate.mjs pre-commit`. Phase files never move; status lives in
   front-matter only.
