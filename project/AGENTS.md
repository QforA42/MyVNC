# project/ — how the work goes

- `STATUS.md` is the only file every session reads: at most 80 lines, rewritten, never appended.
- Phases: `phases/phase-NNN-slug/plan.md` from `templates/phase.md`. Files never move when status
  changes; status lives in front-matter. `progress` is derived by tooling from the chunk table.
- A phase keeps its own `protocol.md` and `evidence/` in its folder; dated and release protocols go in
  `protocols/` (`YYYY-MM-DD-slug.md`, `release-X.Y.Z.md`), release notes in `releases/X.Y.Z.md`.
- Names follow ADR 0007 (gate `names`): English kebab-case, dates first, the folder carries the type.
  Rename with `tooling/scripts/rename-docs.mjs`; it fixes links and records `previous_paths`.
- Ideas before a phase go in `proposals/` (template `proposal.md`); dated assessments in `reviews/` (template
  `review.md`); evidence that spans phases or belongs to a release in `evidence/<topic>/`.
- `backlog.md` is generated from the ticket system (`tooling/scripts/render-backlog.mjs`); change tickets, not the file.
- Records of finished work are immutable; correct them with a new dated entry.
- `agent-workflow.md` describes the whole workflow. `review-queue.md` holds open owner decisions;
  `handoffs/` holds work waiting for owner authority (unchecked box = not authorised).
- `metrics/sessions.jsonl` is appended by the session-end hook; never edit it by hand.
- New phase: skill `new-phase`. Release: skill `release` (tag, push and deploy are hard stops).
