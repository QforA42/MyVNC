---
name: setup-machine
description: Installs the tools, dependencies and git hooks this repository's local gates need on the owner's machine. Use only when the owner asks to install dependencies or set up the machine, or answers yes to the doctor's suggestion.
---
# Set up the machine

Installing is a hard stop unless the owner asked for it in this conversation; their request is the
authorisation for the steps below, nothing more.

1. `node tooling/scripts/doctor.mjs` lists what is missing and the fix for each.
2. Repository-local fixes (Corepack shim, `pnpm install`/`npm ci` from the lockfile, `lefthook install`):
   `node tooling/scripts/doctor.mjs --fix`. They only use the repository's own lockfiles and configuration.
3. System tools (for example the .NET SDK or gitleaks through winget): name each one with its package id
   and ask the owner before `node tooling/scripts/doctor.mjs --fix --system`. Items marked "install it
   yourself" (Node.js, Docker) stay with the owner.
4. Run `node tooling/scripts/doctor.mjs` again and `node tooling/scripts/gate.mjs quick`; report what was
   installed, what failed and whether a new shell is needed. Never change lockfiles, versions or gates to
   make an install pass.
