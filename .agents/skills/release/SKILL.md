---
name: release
description: Prepares and verifies a release - version, changelog, release note, test protocol, image scan in Zot and DocSaga sync. Use only when the owner asks for a release; tagging and pushing are hard stops.
---
# Release

1. Confirm the owner asked for this release and which version (SemVer by contract impact).
2. Update `VERSION` and run the version sync; move `CHANGELOG.md` "Unreleased" into the version.
3. Create `project/releases/<version>.md` from the release note template in `project/templates/` and the release test
   protocol from `project/templates/test-protocol.md`.
4. Build and push images to Zot, then record each image as `name@sha256:<digest>` in the release
   note's `images:` front-matter.
5. Run `node tooling/scripts/gate.mjs release`. It checks governance, front-matter and queries Zot
   for every listed digest; HIGH/CRITICAL findings not in `tooling/security/accepted-risks.json`
   block the release.
6. **Hard stop:** tag, push and deploy only after explicit owner approval. Deploy by digest.
7. After release: run the DocSaga sync and set the release note `status: released`.
