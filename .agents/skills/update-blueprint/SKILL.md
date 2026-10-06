---
name: update-blueprint
description: Updates this repository to a newer repo-blueprint version - dry run, apply, hand-merge of locally changed files, migration steps, regeneration and verification. Use when the session start or blueprint-status reports a newer blueprint and the owner asks for the update.
---
# Update the blueprint

Run only when the owner asked for it; the update is a change to tooling, hooks and CI (a hard stop
otherwise). Work on a branch (`chore/blueprint-<version>`), in a worktree if the main checkout is in use.

1. `node tooling/scripts/blueprint-status.mjs` shows the applied and the available version. Read every
   `CHANGELOG.md` section in between in the blueprint checkout (`source` in `tooling/blueprint.json`),
   especially **Migration** and **Changed**.
2. From the blueprint checkout, dry run:
   `node tooling/apply-blueprint.mjs <this repo> --project <Name> --prefix <PFX> --owner <owner> --overlay <overlays>`
   (values and overlays are in `tooling/blueprint.json`). `new` and `update` are safe; `modified` means
   this repository changed the file and it is never overwritten.
3. Run the same command with `--apply`.
4. For each `modified` file the changelog touches, compare and merge by hand:
   `git diff --no-index <blueprint>/template/base/<file> <file>`. Keep local rules; take the blueprint's
   new checks, fields and paths. Do the changelog's migration steps (renames with `rename-docs.mjs`).
5. Regenerate and check: `node tooling/scripts/sync-agents.mjs`, `node tooling/scripts/render-overview.mjs`,
   `node tooling/scripts/gate.mjs quick`.
6. Verify with `node tooling/scripts/ci-local.mjs`; fix what it reports, never by weakening a gate.
7. Commit as `chore(repo): update repo-blueprint to <version>` with what changed and what was merged by
   hand. Push, pull request and merge only with the owner's go-ahead.
