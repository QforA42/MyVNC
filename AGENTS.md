# MyVNC — agent instructions

Canonical instructions for every agent (Codex, Claude Code, others). `CLAUDE.md` files only import
this file. Keep it under 8 KB: procedures live in `.agents/skills/`, folder rules in the folder's own
`AGENTS.md`, current state in `project/STATUS.md`. `node tooling/scripts/check-agents.mjs` enforces it.

## Start of session

Confirm the git root, branch and status; never discard changes you did not make. The session-start
hook shows the branch, uncommitted changes, open owner decisions and `project/STATUS.md`; without
hooks, read `project/STATUS.md` yourself. Read the active phase plan only if your
task belongs to it, and a folder's `AGENTS.md` before editing in it. The whole flow is in
`project/agent-workflow.md`; the chunk loop is skill `chunk-workflow`.

## Repository map

| Path | Purpose | Own AGENTS.md |
|---|---|---|
| `src/MyVNC.Rfb/` | RFB (VNC) protocol library: handshake, security types, encodings; no UI dependencies | — |
| `src/MyVNC.App/` | WPF app (`net10.0-windows`): dashboard, session windows, settings, localization (`Services/Loc.cs`) | — |
| `tests/MyVNC.Rfb.Tests/` | xUnit tests for the protocol library | — |
| `docs/` | Product truth for readers (Diátaxis) and ADRs | yes |
| `project/` | Plans, status, protocols, release notes (`project/releases/`), evidence | yes |
| `tooling/` | Gates, scripts (smoke test), CI image, schemas, security config, `installer/` (Inno Setup) | — |
| `.github/` | CONTRIBUTING (versioning, commits, localization, privacy), SECURITY, Dependabot | — |
| `.agents/` | Source for skills and agent roles (generated into `.claude/`, `.codex/`) | — |

`VERSION` is the only version source (`Directory.Build.props` reads it); `.github/CONTRIBUTING.md` has the
release rules. Build with `MyVNC.slnx`; a running `MyVNC.App.exe` locks the output (`taskkill /F /IM MyVNC.App.exe`).
User-facing strings exist in six languages in `src/MyVNC.App/Services/Loc.cs`. Never log credentials,
keystrokes or clipboard contents, and use placeholders for hosts and addresses in docs and commits.

## Commands

| Need | Command |
|---|---|
| Fast check while editing | `node tooling/scripts/gate.mjs quick` |
| Before commit (hooks run it) | `node tooling/scripts/gate.mjs pre-commit --staged` |
| Before push | `node tooling/scripts/gate.mjs pre-push` |
| Is the chunk done? | `node tooling/scripts/gate.mjs verify` |
| App-level smoke test (Release build, Windows) | `powershell -File tooling\scripts\smoke-test.ps1` |
| Full CI locally, all scanners | `node tooling/scripts/ci-local.mjs` |
| Everything CI runs | `node tooling/scripts/gate.mjs ci` |
| List stages and checks | `node tooling/scripts/gate.mjs --list` |
| Regenerate agent files | `node tooling/scripts/sync-agents.mjs` |

Formatting, lint, types, layering, front-matter, secrets and CVEs are checked by gates. Do not
memorise those rules; run the gate and fix what it reports.

## Source of truth (highest first)

1. Accepted ADRs (`docs/adr/`) and specifications (`docs/reference/`)
2. Architecture and explanation docs (`docs/explanation/`)
3. The active phase plan (`project/phases/*/plan.md`)
4. `project/STATUS.md`, roadmap and backlog
5. Code comments and older records

Report a conflict between sources instead of resolving it silently.

## Hard stops (ask the owner, record the stop, continue with something else)

- `git push`, force push, rebase of shared branches, tags, `--no-verify`
- Version bumps and releases; deploys
- New or upgraded dependencies, CI, hooks, registries, base images (installing from the existing lockfiles
  when the owner asks is fine: skill `setup-machine`)
- Breaking API or contract changes, auth/permission/crypto changes, migrations
- Deleting files or data you did not create; network calls to non-local services
- Changing an accepted ADR (write a superseding ADR instead)
- Secrets: never print, commit or write them; use Infisical where configured

## Working rules

- One chunk = one session = one commit; at most 5 files. Split larger work.
- Owner decisions you cannot get: take the least irreversible option, add a row to `project/review-queue.md`
  and to the phase's autonomy record. Work that needs owner authority goes into a handoff
  (`project/templates/handoff.md`); an unchecked box there means you may not do that step.
- Hooks (Claude and Codex, `.agents/hooks/`) check every shell call, every edit and the end of every
  turn, and record session metrics. Fix what they report; never work around them.
- All Markdown is English. Swedish text is saved as `<name>.sv.md` next to its English source
  (gate `language`). Docs in `docs/` and `project/` need valid front-matter; tooling sets `updated` and derived fields.
- Use subagents by tier (`.agents/roles/`): T1 scout and test-runner for search and test runs, T2
  implementer and doc-writer for chunks, T3 architect and reviewer for contracts, security and stuck
  debugging. Escalate on evidence: a gate failing twice, a package boundary crossed, a trust
  boundary touched, two failed debugging hypotheses. Anything a script can do is T0: write the script.
- Code quality (blueprint ADR 0009): search before writing (CodeGraph or grep) and reuse, or move shared code into a
  package, instead of copying; one responsibility per function and file; names say what, comments say why.
  Complexity, function and file length, parameter count and copied code are gates with a baseline that only
  shrinks: split the code, never raise the baseline.
- Commit messages: Conventional Commits with `Refs: github:owner/repo#N`, `gitea:owner/repo#N` or `ticketeer:KEY`.

## Definition of done

`node tooling/scripts/gate.mjs verify` passes (format, build with warnings as errors, unit tests), user-visible
changes have a `[Unreleased]` line in `CHANGELOG.md`, docs and front-matter are updated in the same change, and the result (command
and outcome) is recorded in the chunk row and `project/STATUS.md`.
