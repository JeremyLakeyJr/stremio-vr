# Release checklist

Use this checklist for a tagged or distributed build. Do not publish until
every required item has an owner, evidence, or an explicit documented
exception.

## Source and dependencies

- [ ] Confirm the intended commit is on `master`.
- [ ] Confirm `npm ci` succeeds with the committed lockfile.
- [ ] Confirm no secrets, private asset archives, generated `Library/` content,
      APKs, or local configuration files are tracked.
- [ ] `npm run check:repository` passes.
- [ ] Record Node, Rust, Unity, Android SDK/NDK, and OpenXR versions.

## Automated gates

- [ ] `npm test` passes.
- [ ] `npm run check` passes.
- [ ] `npm run check:solo` passes.
- [ ] `npm run test:core` passes with `--locked`.
- [ ] `npm run docker-smoke` passes.
- [ ] GitHub Actions quality gate is green for the release commit.

## Quest build and device evidence

- [ ] Authorized Open Concept Home assets are imported locally or available to
      the Unity build runner.
- [ ] Unity Configure Quest project completes without errors.
- [ ] Development or release ARM64 APK builds successfully.
- [ ] Record APK size, SHA-256 checksum, Unity version, and build commit.
- [ ] Follow [QUEST_SMOKE_CHECKLIST.md](./QUEST_SMOKE_CHECKLIST.md).
- [ ] Record two-headset, different-network, voice, TURN, and soak results, or
      explicitly mark them as not tested in [ACCEPTANCE.md](./ACCEPTANCE.md).

## Provider and security checks

- [ ] Provider health and contract tests pass for every advertised backend.
- [ ] Credentials are masked in dashboard/API output and absent from room
      state, logs, diagnostics, and screenshots.
- [ ] Test an unavailable provider, invalid credentials, no-stream result,
      subtitle failure, and reconnect path.
- [ ] Confirm webhook delivery behavior does not block room operations.

## Packaging and publishing

- [ ] Update version metadata and release notes.
- [ ] Attach the APK and checksum file to the release.
- [ ] Configure signing only through protected CI secrets.
- [ ] Verify the published checksum after download.
- [ ] Keep the previous known-good APK and rollback commit available.
- [ ] Confirm the release workflow and Docker image point to the intended
      commit.

## Post-release

- [ ] Record first-install and first-launch results.
- [ ] Review crash, server, provider, and notification diagnostics.
- [ ] Capture regressions as roadmap items with reproduction evidence.
