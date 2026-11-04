# Movie Night VR Roadmap

This roadmap prioritizes reliable Quest releases first, then provider coverage,
performance, and product polish. Each phase should end with evidence in
`docs/ACCEPTANCE.md` or an automated test.

## Phase 1 — Release hardening

- [ ] Run the Quest APK through the repeatable
  [Quest smoke checklist](./QUEST_SMOKE_CHECKLIST.md): install, first launch,
  solo mode, room join, playback, subtitles, audio, and clean exit.
- [ ] Validate two physical Quests on different networks, including WebRTC
  voice and TURN fallback.
- [ ] Record a 30–60 minute playback soak with frame rate, memory, thermal,
  audio drift, reconnect, and host-leave results.
- [ ] Replace remaining Unity 6 build warnings where they affect correctness:
  application entry point, input polling, duplicate permissions, and
  pre-baked collision configuration.
- [ ] Keep the Android build on one supported input system and verify controller
  fallback mappings on-device.

## Phase 2 — Provider reliability

- [ ] Add contract tests for Jellyfin, MediaStorm, Silo, Remux, and Mellon
  authentication, catalogs, stream resolution, subtitles, and progress sync.
- [ ] Verify credential headers are sent only to their intended upstream and
  never enter room state, logs, screenshots, or diagnostics.
- [x] Add bounded timeouts to native catalog and stream requests.
- [x] Validate provider server URLs before authentication requests and clear
  stale sessions after invalid reconfiguration.
- [ ] Add cancellation and retry controls to native catalog and stream
  requests.
- [x] Add user-visible error states to native catalog, search, pagination, and
  stream requests.
- [x] Add user-facing validation and failure feedback to Mellon/Nuvio sync.
- [x] Define and document the supported API response shapes for each provider
  in [PROVIDER_CONTRACTS.md](./PROVIDER_CONTRACTS.md).
- [ ] Add provider fixtures so CI can test success, authentication failure,
  malformed responses, and offline behavior without live services.

## Phase 3 — Quest performance

- [ ] Profile 2D, SBS, OU, HDR, browser, subtitles, avatars, and extra screens
  on the target Quest hardware.
- [ ] Establish budgets for frame time, memory, texture memory, startup time,
  and stream latency; fail release checks when budgets regress.
- [ ] Reduce shader and asset import warnings that translate into runtime cost.
- [ ] Test 4K quality profiles against thermal throttling and network bitrate
  limits; provide a safe automatic fallback.
- [ ] Verify long-session cleanup for posters, browser textures, subtitles,
  provider results, and disconnected rooms.

## Phase 4 — User experience

- [ ] Improve first-run onboarding for server connection, provider setup,
  account pairing, and headset permissions.
- [ ] Add consistent loading, cancellation, retry, and empty-state UI across
  all provider catalog views.
- [ ] Improve virtual keyboard focus, password entry, accessibility labels,
  and controller/desktop parity.
- [ ] Make room reconnect, host departure, and provider disconnect recovery
  explicit and non-destructive.
- [ ] Add a compact in-app diagnostics screen that can copy redacted logs and
  build/version information.

## Phase 5 — Delivery and operations

- [ ] Add reproducible Unity CI builds with the required Unity and Android
  toolchain versions. Server, solo, Rust, and Docker quality gates are
  automated in `.github/workflows/quality-gate.yml`; Unity still requires
  licensed editor secrets and the authorized private-home asset.
- [ ] Publish signed APK artifacts with version metadata and checksums. The
  Unity workflow now publishes SHA-256 checksums; signing remains pending.
- [ ] Add a [release checklist](./RELEASE_CHECKLIST.md) covering Unity, Node,
  Rust, Docker, provider contracts, Quest smoke tests, and rollback notes.
- [ ] Add server health and notification delivery metrics without recording
  credentials or media URLs.
- [ ] Track crash reports and user-facing failures by build version.

## Explicit non-goals

- DRM bypass or redistribution of licensed content.
- Copying third-party Unity projects or private assets without authorization.
- Treating unverified provider behavior as supported production functionality.
