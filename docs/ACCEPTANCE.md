# Goal completion and release acceptance

“Implemented” below describes source code, not validated headset behavior. Do not mark native goals complete until the APK and device checks pass.

| Goal | Implementation | Validation and remaining acceptance |
| --- | --- | --- |
| Foundation | Unity 6, native Android ARM64, OpenXR | Editor installed/licensed; packages imported and OpenXR configured |
| Working Quest app | Runtime cinema scene and Android build command | APK built/installed on Quest 3; stereo/tracking/controller UI confirmed by user |
| Stremio media | Add-on manifests, catalogs, episode metadata, streams; streaming-service torrent URLs | Actual Rust core integrated; six routing/validation tests pass; real external torrent service still needs a device check |
| Solo playback | One-person room, native video/audio, pause and seek | H.264/AAC test pattern plays on Quest; pause/seek confirmed; broader codec/error cases remain |
| Rooms | Create/join/leave, capacity, host transfer, reconnect | Server tests pass; test two native clients |
| Playback sync | Server timeline, clock-offset sampling, catch-up and bounded speed correction | Two-headset drift/buffering tests |
| Avatar presence | Supplied R6 meshes, head/arm poses, interpolation, room-only visibility | Verify tracking origin, seating, orientation, and frame cost |
| Customization | R6 live preview, head/body proportions, skin, hair, outfits/colors, three accessory types | R6 model/face renders on Quest; all-combination inspection remains |
| Saved profiles | Local JSON and room-state synchronization | Saved avatar profile matched the advertised room profile after final APK update/relaunch |
| Voice | Native WebRTC/Opus, opt-in microphone, mute/deafen, room-scoped signaling, TURN configuration | Android build and two-headset audio/echo/NAT acceptance required; old PCM checks do not validate WebRTC |
| VR interface | Controller ray, scroll, in-world keyboard, room/media/avatar pages | User confirmed both-eye R6 preview, appearance controls, and preview rotation on Quest |
| MVP release | Server checks and build instructions | Native build, two-headset acceptance, performance and packaging |

## Automated checks

`npm test`: 24 tests covering room isolation, host authority, timeline progression, invalid playback, reconnect identity, session replacement, host handoff, room cleanup/capacity, profile validation, full-range rotations, voice validation/isolation/throttling, signaling isolation, real WebSocket playback, invalid JSON recovery, Origin checks, and flood closure.

`npm run check`: JavaScript syntax validation. `npm run test:core`: six Rust tests. Twenty-two Unity EditMode checks pass, including mirror reflection geometry, covering URL safety and R6 asset/pose integrity. The APK is built with the installed Unity editor.

Quest 3 test evidence: user confirmed both eyes and controls, video and room tone; the peer received hundreds of microphone frames. Playback logs recorded approximately 0–30 ms timeline drift during the local test and a paused seek settled at 10.00 seconds. This is one physical headset with a Node peer, not two-headset validation. USB interruption disconnected the client and stopped capture; debugging was subsequently restored.

## Native build gate

- Unity imports all packages and compiles without errors.
- Configure runs successfully; OpenXR project validation passes for Android.
- Development ARM64 APK builds and installs on a Quest 2/3/Pro.
- Launch from Unknown Sources: both eyes render, controllers track, UI responds.
- Avatar and screen shaders are included in the APK and video audio plays.

## Two-headset scenario

1. Start a room server; connect both headsets to a reachable server address.
2. A creates a room; B joins the displayed code. Both see correct host and participant names.
3. A selects a known compatible MP4, plays, pauses, and seeks. B follows. Target measured drift below 750 ms after settling; record the measurement rather than assuming the threshold proves it.
4. B joins during playback and catches up. Interrupt B's network for less than 15 seconds; confirm identity restoration and playback catch-up. After longer interruption, confirm clean rejoin or a clear room-not-found message.
5. Disconnect A beyond the grace period; B becomes host and controls playback. Explicit leave should hand off immediately.
6. Change every avatar option on A. B sees the changes; a third client in a separate room receives no profile, pose, or audio updates.
7. Enable voice on both devices with headphones. Test mute, deafen, permission denial, app backgrounding, disconnection, and reconnect. Confirm capture stays off until explicitly re-enabled after disconnection/backgrounding.
8. Restart the client; profile, add-on URLs, and server settings persist. Room membership is intentionally not persisted across app restarts.
9. Browse catalog → movie source and catalog → series episode → source. Test an unavailable add-on, no-stream result, unsupported stream type, unreachable service, and unsupported codec.
10. Profile at two and eight participants: target 72 FPS (13.9 ms frame budget), bounded voice buffers, stable memory over 30 minutes. Measure before marking this complete.

## Remaining product work

- Broader stream compatibility (headers/transcoding/DRM/subtitles) is outside the initial player.
- Validate WebRTC echo cancellation, codec quality, jitter handling, and TURN on separate networks.
- Add durable accounts/rooms, moderation, deployment operations and production signing if public distribution is intended.

## R6/menu device pass

The updated APK installed successfully on Quest 3. `unity/Builds/quest-r6-menu.png` shows the supplied R6 mesh/face in both eyes and the charcoal navigation layout. `unity/Builds/quest-r6.log` records successful native `/api/core` manifest calls and test-video preparation without app exceptions. The user confirmed the preview looks right in both eyes and customization works. A final scrolling fix was installed successfully. A room observer verified that the restarted headset advertises the saved profile. Two physical headsets have not been tested.

## Cinema, comfort, accounts, and internet hosting

- Imported a CC0 premade cinema/foyer, combined into 12 material batches; 32 interactable chairs.
- Left-stick walking with collision/gravity, right-stick smooth turn, exclusive room seating, tracked head/hand movement, 45-degree default ray correction, and reclined calibration implemented. The user confirmed walking, smooth turning, seating, and pointer comfort.
- Stremio device-link schema verified live; official HTTPS link filtering, account add-on import and Android Keystore credential storage implemented. The user confirmed actual sign-in and account add-on import.
- Docker image built and passed HTTP/core/two-client WebSocket, seating, playback, pose, and host-transfer checks on its published port. Render and no-account Quick Tunnel deployment files are supplied; permanent hosting is not provisioned.

## Movie Night feature pass (2026-10-28)

Implemented after user feedback: per-pixel video bounce and edge glow; 4K Home textures and original baked lighting/emission/sky; account/local watch history and resume; explicit walk/turn speed settings; local R6 arms/body; stereo mirror; Android hardware WebView cinema surface with host-only pointer, scroll, text entry, and HTTP-video handoff; Movie Night naming and AGPLv3 source link.

Automated server tests pass (24). The Home conversion restores baked UV coordinates on 67 active renderers. The APK builds and installs on Quest 3. Hardware web frames and the Stremio login page were verified in both-eye screenshots; 139 account watch-history entries imported successfully. Public HTTPS/WSS health/core/two-client seating, playback, pose, and host-transfer checks passed. Mirror stereo comfort, browser login/video/beam operation, new lighting balance, memory/frame budget, and the history UI still require headset acceptance. The author's showcase appearance is a reference target, not an already verified pixel-identical result. No two-physical-headset test has been performed.

## Alpha 2 follow-up

Replaces periodic CPU video-edge readback with an 8-pixel GPU smoothing pass; links Home emissive materials to that result; adds two mirrored Home screens using one decoder; hides the local R6 body from first-person view and substitutes hand nubs while retaining the full mirror/peer avatar. Native movie audio now explicitly controls its selected track, uses direct Android output, and stays at 1× playback speed. Local track and volume controls are exposed.

Adds server-authoritative chess and tic-tac-toe with in-world tables, player seats, legal moves, game outcomes, reset, room isolation, and reconnect continuity. Chess uses chess.js 1.4.0 under BSD-2-Clause.

The specific silent Stremio source has not been supplied or retested; unsupported audio codecs may still require another source or transcoding. Protected DRM playback has not been implemented. New lighting smoothness, tabletop placement/control comfort, mirrored-screen placement, and movie audio need headset acceptance.

Alpha 2 automated validation: 31 Node tests passed, including real WebSocket board updates; 23 Unity EditMode tests passed; Android APK built successfully. Home conversion retained 67 baked renderers and enabled video response on seven emissive material batches. No new headset installation or live visual/audio acceptance was performed, per the user's request to install later.

The updated Docker image also built successfully and passed health/core, two-client playback/seating/pose/host-transfer checks, plus chess and tic-tac-toe moves through its published port.

## 0.1.2 recovery checks (pending headset validation)

- [ ] Cold-launch Home: neither eye is white/frozen; no right-eye tunneling.
- [ ] Move and turn with ambient lighting on/off; both eyes remain aligned.
- [ ] Discover: play through the updated Docker server and hear AAC audio.
- [ ] Two headsets: same video, pause/resume/seek and late join through public server.
- [ ] Resume watch history after a seek: original movie time is preserved.
- [ ] Nubs, sofa seats, mirrored screens, light bars, chess and tic-tac-toe remain usable.

Mirror, runtime reflection probe, and bloom are disabled in this recovery build.
Automated checks cover render-target restoration, source validation, AC3-to-AAC
transcoding, HLS ingestion/output, shared room URLs, host authority, and seek replacement.

## 0.1.3 placement, mirror and voice acceptance

- [x] User confirmed the 0.1.2 stereo recovery on Quest.
- [ ] Mirror restored without white-eye/tunneling regression.
- [ ] Host can place/move/remove screens and both table types with comfortable controls.
- [ ] Second headset sees the same layout and cannot edit as a guest.
- [ ] WebRTC voice connects, mutes, deafens and reconnects across NAT/TURN.
- [ ] Web sign-in persists across closing the screen and creating a new room.
- [ ] Share page shows the current Cloudflare URL and copied invite joins the correct server.

Two real Chromium peers exchanged encrypted WebRTC audio through the room server on 2026-10-29. This validates signaling and browser-to-browser media; it does not establish Quest microphone or cross-network TURN acceptance. The Cloudflare share helper produced a real public URL and the dashboard returned it.

## Alpha 4 final build checks (0.1.3)

- Unity EditMode: **27/27 passed**, including ambient framebuffer restoration, mirror geometry, downstairs texture/emission, and original chess mesh bounds.
- Docker server: **41/41 passed**, including room placement authority/revisions, WebRTC signaling, TURN credentials, real WebSocket games, and FFmpeg AC3-to-AAC transcoding.
- Android ARM64 APK: built successfully with the native WebRTC library and tabbed WebView.
- Before/after in-engine capture reproduced the white downstairs floor and verified the corrected patterned floor.
- Original Jacob G chess models imported without triangle reduction; author/source/CC BY-ND attribution included in Games and assets/chess/README.md.
- [x] User confirmed browser tab controls work on Quest.
- [x] User confirmed placement and chess grip controls work on Quest (detailed illegal-drop/other-hand cases remain part of extended testing).
- [ ] On Quest: assess performance with a full chess board, mirror, video and voice together.

Browser pages remain local to the host. Guests receive synchronized native video after a supported HTTP-video handoff. The chess grab preview is local; committed moves are server-authoritative. Two-headset voice/TURN and protected DRM playback are not claimed as verified.

User acceptance (2026-10-29): after installing Alpha 4 and asking about both-eye startup, browser tabs, table placement and chess gripping, the user replied **“Looks good; controls work.”** The final APK includes the same runtime code plus the corrected original pawn asset and bundled third-party license notices.
