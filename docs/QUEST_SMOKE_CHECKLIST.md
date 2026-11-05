# Quest release smoke checklist

Use this checklist for every candidate APK. Record the build commit, Unity
version, headset model, and date with the results. A check is complete only
when the observed result and any log/screenshot path are recorded.

## Build identity

| Field | Value |
| --- | --- |
| Commit | |
| Unity version | |
| APK path / checksum | |
| Headset model and OS | |
| Test date | |

## Install and first launch

- [ ] APK installs cleanly over the previous build.
- [ ] App launches from Unknown Sources without a crash.
- [ ] Both eyes render the apartment and the initial camera is comfortable.
- [ ] Head and controller tracking work.
- [ ] Menu opens with the primary menu control and fallback input.
- [ ] No new red Unity/player log errors appear during launch.

## Solo path

- [ ] Solo mode starts without a room server.
- [ ] Home environment, seating, locomotion, turning, and calibration work.
- [ ] A local test video plays with audio.
- [ ] Pause, seek, resume, and subtitles work.
- [ ] Chess and tic-tac-toe remain usable.
- [ ] Layout, profile, and resume state survive an app restart.
- [ ] App exits and relaunches without stale state or a crash.

## Room and playback path

- [ ] Host creates a room and a second client joins using the displayed code.
- [ ] Host playback, pause, and seek are reflected on the second client.
- [ ] A late joiner catches up to active playback.
- [ ] Host departure produces a clean handoff or clear room termination.
- [ ] A short network interruption reconnects without duplicating the player.
- [ ] Room state does not expose credentials, profile keys, or private browser
      state.

## Providers and browser

- [ ] Stremio catalog → title → stream works with a reachable test add-on.
- [ ] Jellyfin, MediaStorm, Silo, and Remux are tested when configured.
- [ ] Invalid credentials and an offline provider show a recoverable error.
- [ ] A no-stream result does not leave a stuck loading state.
- [ ] Browser tabs, text entry, password masking, and host-only control work.

## Voice and performance

- [ ] Two clients can join voice with explicit microphone permission.
- [ ] Mute and deafen stop outgoing/incoming audio as expected.
- [ ] A denied permission leaves capture disabled and recoverable.
- [ ] A 30–60 minute playback soak records frame rate, memory, thermal state,
      audio drift, reconnect behavior, and host-leave behavior.
- [ ] Two-headset testing on different networks records WebRTC and TURN
      results separately.

## Evidence

Attach or link:

- Unity build log and APK checksum.
- `adb logcat` excerpt covering launch and playback.
- Server log excerpt covering room creation and playback.
- Before/after screenshots for any failure.
- Measured playback drift, frame time, memory, and soak duration.

Do not mark the corresponding row in [ACCEPTANCE.md](./ACCEPTANCE.md) complete
from source inspection alone.

