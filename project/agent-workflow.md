---
id: VNC-REC-agent-workflow
type: runbook
title: Agent workflow
status: current
owner: magnus
updated: 2026-01-01
lang: en
wiki:
  slug: agent-workflow
  page_type: runbook
  sync: auto
---
# Agent workflow

How agents (Claude Code, Codex) and the owner work in this repository, from session start to release.
`AGENTS.md` holds the rules in short form; this document explains the whole flow. Skills hold the
step-by-step procedures. Hooks and gates enforce what can be enforced, so this text describes intent
and judgement, not things a script already checks.

## 1. Units of work

| Unit | What it is | Where it lives |
|---|---|---|
| Ticket | A need or problem, owned by the tracker | GitHub, Gitea or Ticketeer; `project/backlog.md` is a generated view |
| Proposal | An idea or design being explored before it is a phase; promoted, deferred or rejected by the owner | `project/proposals/YYYY-MM-DD-slug.md` |
| Phase | A governed delivery with a goal, readiness check, chunks and a test protocol | `project/phases/phase-NNN-slug/plan.md` |
| Chunk | One verifiable sub-goal: one session, one commit, at most 5 files, one verb | A row in the phase's chunk table |
| Review | A dated assessment (security, CVE, architecture, structure); each finding becomes a ticket, review-queue row or ADR | `project/reviews/YYYY-MM-DD-slug.md` |
| Session | One agent conversation; working memory only | Recorded in `project/metrics/sessions.jsonl` |

Anything that must survive a session is written to a file. A session that ends without writing its
result down did not happen.

## 2. Session lifecycle

1. **Start (hook `session-start`).** The agent receives the branch, uncommitted changes it must
   preserve, the number of open owner decisions and `project/STATUS.md`. It reads the active phase plan
   only if the task belongs to it, and a folder's `AGENTS.md` when it starts working there.
2. **Plan the chunk.** Restate the chunk's "Done when". If the task is larger than a chunk, split it
   in the phase plan first (skill `new-phase` for a new phase, `chunk-workflow` for the loop).
3. **Work.** Every shell call passes `pre-tool` (no gate bypass, no force push, no edits to generated
   or secret files). Every edit passes `post-edit` (format, front-matter, language, layout on the
   edited files); problems come back immediately and are fixed in the same step.
4. **Delegate by tier.** Search and test runs go to T1 subagents, implementation of a well-specified
   chunk to T2, contracts, security, review and stuck debugging to T3. Anything a script can do is T0.
5. **Finish the turn (hook `stop`).** Before the agent ends a turn, the `turn-end` gate runs on every
   changed file. If it fails, the agent continues and fixes it, or explains why it cannot pass.
6. **Commit.** Only when the owner asked for commits or the phase says so. Conventional Commits with a
   `Refs:` trailer; lefthook runs `pre-commit` and `commit-msg`. Never commit red.
7. **End (hook `session-end`).** One line of metrics is appended: agent, models, minutes, exact tokens
   or "unavailable", commits, tickets, files changed and the last gate result.

## 3. Definition of done for a chunk

- `node tooling/scripts/gate.mjs verify` passes: tests, types, typed lint, layering, and the Compose
  stack builds, becomes healthy and answers the smoke URLs (`tooling/config/verify.json`).
- Docs and front-matter changed in the same commit as the code.
- The chunk row has status, commit and the verification command with its result.
- `project/STATUS.md` reflects the new state (rewritten, at most 80 lines).

Before pushing a branch that changes CI-relevant files, run the full CI locally:
`node tooling/scripts/ci-local.mjs`.

## 4. Autonomy and decisions

- **Hard stops** (listed in `AGENTS.md`) are never passed without the owner: push, release, new
  dependencies, CI/hooks, contracts and security, migrations, deleting what you did not create,
  network calls to non-local services, changing accepted ADRs.
- **Owner decisions that are not available now:** take the least irreversible option, continue, and
  add a row to `project/review-queue.md` with the options and what changing course would cost.
  Decisions inside a phase are also recorded in the phase's autonomy record.
- **Handoffs:** when work must wait for the owner (credentials, production steps, history rewrites),
  write `project/handoffs/<topic>.md` from `project/templates/handoff.md`. Each step has a checkbox;
  an unchecked box means the agent is not authorised to do it.
- **Conflicts between sources** (code, ADR, spec, plan) are reported, never resolved silently. The
  source-of-truth order is in `AGENTS.md`.

## 5. Debugging

Use skill `debug-loop`: reproduce first, one hypothesis at a time, at most 3 hypotheses and 20 turns,
then stop, write the state to `.local/debug-<issue>.md` and escalate or hand over with a clean session.

## 6. Phases, releases and evidence

- A phase starts from a ticket or backlog item promoted by the owner (skill `new-phase`), passes a
  readiness check, and closes with a test protocol and an explicit decision.
- A release (skill `release`) moves `CHANGELOG.md`, writes the release note with image digests, runs
  `gate.mjs release` (front-matter, governance, Zot CVE on the digests) and stops before tag, push
  and deploy for the owner.
- Evidence (gate output, Zot reports, DocSaga receipts) goes to `project/evidence/`.

## 7. Where checks run

Everything runs locally or on locally hosted services; a hosted CI is optional.

| When | What | Where |
|---|---|---|
| Every edit | `edit` gate via agent hook | Agent session |
| End of every agent turn | `turn-end` gate via agent hook | Agent session |
| Commit | `pre-commit`, `commit-msg` via lefthook | Developer machine |
| Push | `pre-push` via lefthook | Developer machine |
| Chunk done | `verify` (includes Compose smoke test) | Developer machine |
| Before merge, nightly | `ci --strict` in the local CI image (`ci-local.mjs`) or on a self-hosted Gitea runner | Developer machine or LAN |
| Images | `build` gate after pushing to Zot | LAN (Zot) |

## 8. Metrics

`project/metrics/sessions.jsonl` grows by one line per session, written by the `session-end` hook and
committed with the next commit. Tokens are exact counts from the agent's transcript, or
"unavailable"; never estimated. `node tooling/scripts/session-report.mjs --write` summarises them per
agent and model in `project/metrics/report.md`, which the DocSaga sync can publish.
