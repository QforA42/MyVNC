# Contributing to MyVNC

Thanks for your interest in MyVNC! Bug reports, fixes and improvements are welcome. This document
describes how the project is built, tested, versioned and committed.

For security vulnerabilities, **do not open a public issue** — see [SECURITY.md](SECURITY.md).

## Prerequisites

- Windows 10/11 (the app is WPF and targets `net10.0-windows`)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Optional, for building the installer: [Inno Setup 6](https://jrsoftware.org/isinfo.php)
  (`winget install --id JRSoftware.InnoSetup -e`)

## Building and testing

Always build against `MyVNC.slnx`:

```powershell
dotnet build MyVNC.slnx                      # Debug
dotnet build MyVNC.slnx -c Release           # Release
dotnet test tests\MyVNC.Rfb.Tests            # RFB protocol unit tests
powershell -File tooling\scripts\smoke-test.ps1  # process-level regression check (needs a Release build)
tooling\installer\build-installer.ps1        # self-contained build + Windows installer
node tooling/scripts/gate.mjs pre-push           # format, build -warnaserror, tests, docs checks
```

If a build fails with a locked-file error, a running instance of the app is holding the output
binaries — close MyVNC (or `taskkill /F /IM MyVNC.App.exe`) and build again.

Quality gates are defined once in `tooling/gates.yaml` and run by git hooks (`npx lefthook install`), by
agents and by CI on the self-hosted Gitea runner (`.gitea/workflows/gates.yml`). Please make sure
`node tooling/scripts/gate.mjs pre-push` passes locally before opening a pull request;
`node tooling/scripts/ci-local.mjs` runs the full CI, with every scanner, in a container.

## Project layout

| Path | Contents |
|---|---|
| `src/MyVNC.Rfb` | The RFB (VNC) protocol library — handshake, security types, encodings. No UI dependencies. |
| `src/MyVNC.App` | The WPF application — dashboard, session windows, settings, localization. |
| `tests/MyVNC.Rfb.Tests` | Unit tests for the protocol library. |
| `docs/` | Product documentation (Diátaxis) and ADRs. |
| `project/` | Status, review queue and release notes (`project/releases/`, one per version). |
| `tooling/` | Gates, scripts (including the smoke test), CI image, schemas and the installer (`tooling/installer/`). |
| `.agents/` | Agent roles, skills and hooks; `AGENTS.md` is the entry point for Codex and Claude Code. |

## Versioning

- **Canonical source:** the root `VERSION` file (a single line, e.g. `0.9.2`). Every project in
  the solution reads it automatically via `Directory.Build.props` (the `Version` property →
  Assembly/File/InformationalVersion). **Never edit the version in individual `.csproj` files.**
- **SemVer `MAJOR.MINOR.PATCH`:**
  - PATCH: backward-compatible bug fixes.
  - MINOR: new backward-compatible functionality.
  - MAJOR: a breaking change to the user contract (e.g. the connection profile format, command-line
    arguments).
  - Pre-releases when needed: `0.10.0-rc.1`.
- **The version is only bumped when a release is prepared** — never automatically after each
  commit or feature. Pull requests should not change `VERSION`.
- **When `VERSION` changes, update in the same change:**
  1. `VERSION`
  2. `CHANGELOG.md` (move the `[Unreleased]` content to a new `[X.Y.Z] - DATE` section)
  3. `project/releases/X.Y.Z.md` (a new file with release front matter, following the existing release notes)
  4. Rebuild and verify that the app starts before the release is considered done.
- **Release notes:** give the `Commit range` as `BASE_EXCLUSIVE..RELEASE_INCLUSIVE` (the previous
  release tag and the new one) and list every commit in git order under "Included commits".
- **Tags:** when a version is released and the commit is verified, create an annotated tag
  `vX.Y.Z` on that commit. Tags are immutable — a faulty release is corrected with a new patch
  version, never by moving a published tag.

## Changelog

Add a line under `[Unreleased]` in [CHANGELOG.md](../CHANGELOG.md) for any user-visible change,
following [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) (`Added`, `Changed`, `Fixed`,
`Removed`, `Security`).

## Commits

- Split changes into coherent, independently reviewable slices rather than one large commit.
  Typical slices in this repository: the RFB protocol/library, app UI for a specific feature,
  localization, documentation/versioning, build/tooling. If a change spans several unrelated
  purposes, split it into several commits.
- Use imperative commit messages with a prefix: `feat:`, `fix:`, `docs:`, `refactor:`, `test:`,
  `build:`, `chore:` or `release:`.
- Review `git status` and the staged diff before each commit — do not mix in files that do not
  belong to the slice.
- A version bump (`VERSION` + `CHANGELOG.md` + release note) belongs in its own `release:` commit,
  separate from the feature slice it concludes.
- Do not rewrite published commits or tags. Correct mistakes with a new commit.

## Localization

All user-facing strings live in `src/MyVNC.App/Services/Loc.cs` and exist in six languages
(Swedish, English, Norwegian, Danish, Finnish, Icelandic). A new or changed string should be
provided in all of them; if you can't translate one, add the English text and mention it in the
pull request.

## Privacy in logs and docs

MyVNC's debug log must never contain credentials, keystrokes or clipboard contents. Likewise,
don't include real IP addresses, hostnames or usernames in issues, commit messages, release notes
or logs you attach — use placeholders such as `<hostname>` and `<tailscale-ip>`.

## License

By contributing, you agree that your contributions are licensed under the project's
[MIT License](../LICENSE).
