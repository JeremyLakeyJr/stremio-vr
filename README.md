# Movie Night — multiplayer cinema for Meta Quest

Unity/OpenXR client and Node/WebSocket room server for shared video, avatars, and voice. Native Android APK is the target, with an optional in-world Stremio WebView.

**Status:** native APK built and tested on Quest 3. Stereo display, controller input, video/audio, and microphone delivery have been confirmed. The backend integrates the actual Stremio Rust core. The user confirmed R6 appearance customization and the preview in both eyes; saved profiles were verified after restart. Two-headset acceptance and performance profiling remain open. See [acceptance checklist](docs/ACCEPTANCE.md).

## Run the room server

Requires Node.js 22 or later and Rust/Cargo. The first core build fetches the pinned Stremio dependency.

```sh
npm ci
npm run build:core
npm run test:core
npm test
npm start
```

The server listens on port 3000. Health check: `GET /health`. Connect the headset to `ws://YOUR_COMPUTER_LAN_IP:3000/ws`; `localhost` on a headset refers to the headset itself.

Configuration:

| Variable | Default | Purpose |
| --- | --- | --- |
| `PORT` | `3000` | Listen port |
| `CORE_BINARY` | `core/target/release/stremio-vr-core` | Stremio core bridge executable |
| `HOST` | `0.0.0.0` | Listen address |
| `TLS_CERT`, `TLS_KEY` | unset | PEM certificate/key paths for native HTTPS/WSS |
| `PUBLIC_ORIGIN` | server origin | Allowed browser Origin if a reverse proxy is used |

Use a trusted TLS certificate and `wss://` for use outside a trusted development LAN. The server supports eight participants per room, 500 simultaneous rooms, 15-second reconnect grace, bounded messages, and message-rate limits. Rooms are in memory: restarting the server ends sessions. Room codes grant access; there are no accounts, persistent rooms, or moderation tools.

## Build the Quest APK

1. Install Unity **6000.0.66f2** with Android Build Support, Android SDK/NDK Tools, and OpenJDK. Activate Unity Personal in Unity Hub using your own account.
2. Open `unity/` as the Unity project and let packages import.
3. Select **Movie Night → Configure Quest project**. Restart the editor if input handling requests it. The command generates the cinema scene and enables OpenXR, Meta Quest Support, and Oculus Touch controllers.
4. In Android XR Plug-in Management, resolve any reported OpenXR project-validation errors.
5. Select **Movie Night → Build Quest APK**. Output: `unity/Builds/movie-night.apk` (development build).
6. Enable developer mode on the Quest, connect USB, approve USB debugging, then install:

```sh
adb install -r unity/Builds/movie-night.apk
```

After initial configuration/import, the command-line build is:

```sh
/path/to/Unity -batchmode -quit -projectPath "$PWD/unity" \
  -buildTarget Android -executeMethod StremioVR.Editor.QuestBuild.Build \
  -logFile "$PWD/unity/Builds/build.log"
```

The build targets ARM64/IL2CPP and OpenGLES3. Cleartext networking is enabled for LAN development; use HTTPS media and WSS for internet sessions. Store signing and store submission are not configured.

## Use the cinema

- **World & comfort** controls walking/turn speeds, pointer pitch (45° down by default), reclined calibration, and chair selection. Hide the menu to walk with the left stick and turn smoothly with the right stick. Point at a chair and pull the trigger to sit, or choose a chair in the menu. B stands up and returns to the aisle. Chair reservations are exclusive in a room.
- To watch lying down, get comfortable, open World & comfort, look where you want the screen to appear, and select **Calibrate reclined view**. Adjust ±5° or Reset as needed. Calibration levels your current gaze and rotates tracked hands consistently; it does not change other players’ worlds.
- **Stremio account** creates an official device-link code. Approve it on your phone/computer or in the Quest browser, then return to the app. Your configured account add-ons import automatically and refresh on launch; manual Sync and Sign out controls are available.
- Point the right controller at a control and press the trigger. Right stick scrolls. The left menu button hides/shows and recenters the panel. Desktop mouse input is available for editor checks.
- Set the server URL in **Room**. Leave the code empty to create a room or enter a friend's code to join. A one-person room supports solo playback.
- The host loads a direct video URL or chooses a source in **Library**, then uses play/pause or ±10-second seek controls. Guests follow the authoritative room timeline. Late joins and reconnects resynchronize automatically.
- In **Library**, install Stremio add-ons by manifest URL. Catalogs, series episodes, and stream lists are supported; you can also request streams by video ID. Cinemeta supplies metadata, not playable sources.
- Direct HTTP(S) media uses Unity's native video decoder. Torrents require a separately running Stremio streaming service reachable by every participant. Set its URL in Library. The Stremio core routes streams with proxy headers through that service. External-player handoffs, DRM, and unsupported codecs are not supported by the native player.
- **Avatar** opens an R6 character studio with a rotatable live preview. The supplied rig supplies the six body meshes and face texture; options change head/body proportions, skin, hair, outfit/color, and accessories. Configuration is stored as JSON at Unity's `Application.persistentDataPath/profile.json` in the Android app sandbox and sent only to the current room. Profiles are per app installation, not cloud accounts.
- **Mic** explicitly enables capture after microphone permission. Press it again to mute. **Audio** mutes incoming room voice. Use headphones: the initial PCM voice transport has no echo cancellation.

## Architecture

- `unity/`: native Unity application, OpenXR tracking, world-space UI/keyboard, movement and smooth turning, seated/reclined comfort, video synchronization, avatars, voice, account/add-on client, and Android build configuration.
- `server/`: room ownership, authoritative playback timeline, reconnects, avatar/pose propagation, audio relay, and bounded `/api/core` requests.
- `core/`: Rust bridge using pinned official `stremio-core` for manifest validation and stream conversion.
- `unity/Assets/Editor/R6Import.cs`: prepares six rigid avatar meshes from the user-supplied FBX, preserving geometry and UVs. The APK build refreshes the generated prefab.
- `src/network/sync.ts`: documented TypeScript wire contract; the original fake synchronous `joinRoom` implementation is removed.
- `test/`: room logic and actual WebSocket integration tests.

The add-on client fetches catalogs and streams over HTTP. The actual Rust `stremio-core` runs in the backend for manifest validation and stream routing, including torrents and proxy headers. It is not embedded in the APK; the streaming service is a separate process. Stremio device-link sign-in and automatic account add-on import are implemented; account watch history is imported read-only, with local recent playback and resume controls. The auth key stays on the headset in Android Keystore-encrypted app-private storage and is never sent to the room server. Voice is an initial 16 kHz mono PCM relay over WebSocket, not WebRTC/Opus: it is suitable for testing small rooms but still needs latency/bandwidth and echo-handling work before release.

## References and credits

- Creator: Jeremy Lakey.
- Cinema world: [Cinema Multiplex and Foyer](https://3dassets.dev/packs/cinema-multiplex-and-foyer), CC0; [source and conversion notes](assets/worlds/cinema/SOURCE.txt).
- R6 geometry and face texture: user-supplied `roblox-r6-rigged.zip`; its archive contains no author or license metadata. Keep source attribution/license information with this asset when available.
- Menu layout inspired by the supplied `ingame menu example.webp`.
- Stremio core license: [MIT notice](docs/licenses/stremio-core-MIT.txt).
- [Stremio add-on protocol](https://stremio.github.io/stremio-addon-sdk/protocol.html).
- [Stremio core stream routing source](https://stremio.github.io/stremio-core/src/stremio_core/types/resource/stream.rs.html).
- [Meta's Unity/OpenXR setup](https://developers.meta.com/vr/documentation/unity/unity-xr-plugin/).
- [Unity Hub command line](https://docs.unity.com/en-us/hub/use-hub-cli).

AGPLv3 (`AGPL-3.0-only`) for original project code; see [LICENSE](LICENSE). Stremio, Unity, and Meta dependencies retain their own licenses. This is an independent project.

## Repeatable USB device check

With the room server running and a local H.264/AAC MP4 available:

```sh
node scripts/device-smoke.mjs /absolute/path/to/demo.mp4
adb reverse tcp:3000 tcp:3000
adb reverse tcp:3001 tcp:3001
adb shell am start -n com.jeremylakey.stremiovr/com.unity3d.player.UnityPlayerActivity \
  --es stremio.server ws://127.0.0.1:3000/ws \
  --es stremio.room ROOM_CODE_FROM_FIXTURE \
  --es stremio.addon http://127.0.0.1:3001/manifest.json \
  --es stremio.page avatar
```

These launch extras are development-build only. The fixture accepts `play`, `pause`, `seek 10`, `avatar`, `tone`, `mute`, and `leave`. `leave` transfers hosting to the headset while keeping the media server available. In Library, the **Device test** catalog exercises add-on → stream → Rust core → native playback. USB forwarding must be recreated after a device/ADB restart. Stop both servers with Ctrl-C when finished.

## Worldwide multiplayer

See [internet hosting](docs/INTERNET-MULTIPLAYER.md) for the tested Docker server, a no-account temporary Cloudflare tunnel, and a Render deployment template. Every participant needs the same public WSS server and a media URL reachable from their network. The project does not yet have a permanent hosted server.

## Worlds and screen options

Home restores the supplied baked lightmap, emissive materials, normal maps, and underwater sky, with a toggleable bloom pass for emissive lights. Textures are capped at 4K with ASTC compression on Quest. Video edge colors add per-pixel bounce to room surfaces. The reference appearance still requires final visual comparison on the headset; desktop showcase effects and Quest rendering are not assumed identical.

Get the free Home package from [the author](https://jinxxy.com/TwilightStudio/9cvqn). Their current listing removes the old bundled restrictions; see [the recorded update](docs/licenses/home-author-update.md). `scripts/import-private-home.py` imports visual dependencies only, then the Unity build regenerates the Home prefab. Bakery/VRChat editor scripts are not required at runtime.

Your R6 arms/body follow your tracked head and controllers. A stereo room mirror shows your full avatar; toggle it in Settings. The mirror updates at 24 Hz while visible and near the viewer, at 1024 pixels per eye.

**My room → Stremio Web screen** opens the official web app in a hardware-accelerated Android WebView on the cinema screen. The room host uses the beam/trigger and right-stick scroll. Use Web screen controls to enter text into a selected web field, go back, reload, or return to native playback. Web sign-in is separate from native device-link sign-in; credentials/cookies stay on the device. Browser playback is local to the host. **Share playing HTTP video with room** hands a compatible URL and timeline to the synchronized native player. Blob/DRM media and browser account pages are not streamed to guests. The initial web surface updates at 12 Hz; native video remains the high-frame-rate viewing path.

The menu uses smooth panel fades, page transitions, hover/press animation, poster cards, and eased scrolling. **Watch history** switches between imported Stremio history and this headset's recent selections, with Continue Watching and resume. **Settings** exposes walk speed, smooth-turn speed, pointer angle, reclined view, mirror, and video lighting.

Network deployments expose `/source` with the AGPL source URL. If you deploy a modified fork, set `SOURCE_URL` to its corresponding source, including your changes. Third-party dependencies/art retain their own notices.
