# Movie Night — multiplayer cinema for Meta Quest

Unity/OpenXR client and Node/WebSocket room server for shared video, avatars, and voice. Native Android APK is the target, with an optional in-world Stremio WebView.

**Status:** native APK built and tested on Quest 3. Stereo display, controller input, video/audio, and microphone delivery have been confirmed. The backend integrates the actual Stremio Rust core. The user confirmed R6 appearance customization and the preview in both eyes; saved profiles were verified after restart. Two-headset acceptance and performance profiling remain open. See [acceptance checklist](docs/ACCEPTANCE.md).

## Quick start: Docker + Quest

You need a computer with **Docker Engine or Docker Desktop and Docker Compose v2**, a Quest with developer mode enabled, and the Movie Night APK. Keep the computer running while you watch. Docker runs the room server and Rust core; it does **not** build/install the Quest app or provide a Stremio torrent streaming service. Node.js and Rust do not need to be installed on the computer when using Docker.

### 1. Get the project and start the server

```sh
git clone https://github.com/JeremyLakeyJr/stremio-vr.git
cd stremio-vr
docker compose version
docker compose up -d --build cinema
docker compose ps
```

The first build downloads dependencies and compiles the Rust core, so allow several minutes. Wait for `cinema` to become **healthy**, then check it from this computer:

```sh
curl http://127.0.0.1:3000/health
```

Expected response: `{"ok":true,"rooms":0}` before anyone creates a room. If startup fails, inspect `docker compose logs --tail=100 cinema`.

### 2. Choose how the Quest connects

**Internet / friends on other networks:** start the included temporary tunnel. No hosting account or router port forwarding is needed.

```sh
docker compose --profile internet up -d internet
docker compose logs -f internet
```

Find the `https://...trycloudflare.com` address in the logs. Enter that exact address in the app's **My room → Server WebSocket URL** field; the app converts it to WSS automatically. Everyone uses the same address. Press Ctrl-C to stop following logs; the containers continue running.

This address is temporary and can change when the tunnel restarts. Keep both containers and the computer running. The tunnel exposes the room server, not your project files. For permanent hosting, see [internet multiplayer](docs/INTERNET-MULTIPLAYER.md).

**USB / one connected Quest:** keep the default local Docker setup and forward its port with Android platform tools:

```sh
adb devices
adb reverse tcp:3000 tcp:3000
```

Accept USB debugging on the headset. Enter `ws://127.0.0.1:3000/ws` in **My room**. Repeat `adb reverse` after an ADB/device restart. This forwards the room server only; locally hosted videos need their own port forwarding.

**Same Wi-Fi / LAN without a tunnel:** the supplied Compose file binds port 3000 to the computer's loopback address, so other devices cannot reach it directly. Change its `ports` entry in `compose.yaml` from `"127.0.0.1:3000:3000"` to `"3000:3000"`, then run:

```sh
docker compose up -d cinema
```

Allow incoming TCP port 3000 through your computer's firewall on the private network. Enter `ws://YOUR_COMPUTER_LAN_IP:3000/ws` on each headset. Use the computer's actual LAN address, such as `192.168.1.50`, and ensure the network does not isolate devices. Restore the loopback binding when you no longer need LAN access. For friends outside your network, use the HTTPS tunnel above.

### 3. Install and open Movie Night

Download an APK from [GitHub Releases](https://github.com/JeremyLakeyJr/stremio-vr/releases). APK binaries are release assets rather than Git-tracked files. Alternatively, follow [Build the Quest APK](#build-the-quest-apk) below. The build output is `unity/Builds/movie-night.apk`.

With Android platform tools installed and USB debugging accepted:

```sh
adb devices
adb install -r unity/Builds/movie-night.apk
```

Open **Movie Night** from the Quest's **Unknown Sources** app list. `-r` updates an existing installation while retaining app data. If `adb devices` shows `offline` or `unauthorized`, wake the headset, reconnect USB, and accept the debugging prompt before installing.

### 4. Create a room and invite friends

1. Open **My room**, enter the server address chosen above, and leave **Room code** empty.
2. Select **Connect / create room**. The room code appears at the top of the menu; you are its host.
3. Friends enter the same server address and your room code, then select **Connect / create room**.
4. The host chooses media and controls playback/world changes. A one-person room works for solo viewing.

Rooms support up to eight participants. Restarting the server removes existing rooms; create a new one afterward.

### Manage Docker

Run these commands from the repository directory:

| Task | Command |
| --- | --- |
| Check service health | `docker compose --profile internet ps` |
| Read server logs | `docker compose logs -f cinema` |
| Find the current public address | `docker compose logs --tail=100 internet` |
| Stop and remove the services | `docker compose --profile internet down` |
| Update the source | `git pull --ff-only` |
| Rebuild/restart after updating | `docker compose --profile internet up -d --build` |

For USB/LAN-only use, rebuild with `docker compose up -d --build cinema` instead. Rebuilding the server does not update the headset APK. If you edited `compose.yaml` for LAN access, preserve that local change when pulling updates.

### Common connection/playback problems

| Symptom | What to check |
| --- | --- |
| Computer health check fails | Docker is running, `cinema` is healthy, and port 3000 is not already occupied. Read the server logs. |
| Health works on the computer, Quest cannot connect | The default binding is computer-only. Use the tunnel, USB forwarding, or the LAN binding instructions above. |
| Public address stopped working | Check the latest tunnel logs; share its new address with everyone. |
| Room not found | Check the server address and code. A server restart discards rooms. |
| Catalogs load but video does not | Cinemeta supplies metadata, not video sources. Choose a playable source and check codec/streaming-service compatibility. |
| Host can play but friends cannot | Every headset must reach the selected video URL. The tunnel forwards the room server, not local media or a separate streaming service. |

## Run the room server without Docker

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

## Use the app

### Controls and comfort

| Control | Action |
| --- | --- |
| Right-controller beam + trigger | Select menu controls or an empty chair |
| Left-controller menu button | Hide/show and recenter the menu |
| Left stick, menu hidden | Walk |
| Right stick, menu hidden | Smooth turn |
| Right stick over the menu/web screen | Scroll |
| B while seated | Stand and return to the aisle |

Open **Settings / worlds** to adjust walk speed, smooth-turn speed, and pointer angle. To watch lying down, get comfortable, look where you want the screen to appear, and select **Calibrate reclined view**. Use the ±5° controls or Reset to refine it. Your calibration does not change other players' worlds.

The host can switch between Cinema and Home, when Home is included in the APK. Select a chair from Settings or point at it and pull the trigger. Chair reservations are exclusive. Settings also controls the mirror, bloom, and ambient video lighting/strength. **Avatar** customizes your R6 character; your hand nubs and the room mirror reflect that appearance. Appearance settings persist on this headset. Your first-person view uses hand nubs; other players and the mirror see your full R6 character.

### Sign in, browse, and watch together

1. Open **Stremio account** and start sign-in. Approve the displayed device-link code on your phone/computer or in the Quest browser, then return to Movie Night.
2. Your account add-ons import automatically. Use the account's sync control to refresh them or **Sign out** to remove the native session.
3. Open **Discover**, select a catalog/poster, and choose a playable source. For a series, choose an episode first. The host selects what the room watches.
4. Open **My room** for play/pause and ±10-second seek controls. You can also paste an HTTP(S) video URL into **Direct video URL** and select **Load video (host)**.
5. Open **Watch history** for imported Stremio history or **This headset** recent selections. Use **Continue Watching** and select an entry to find a source and resume. Account history is read-only; local progress is not written back to Stremio.

To install an add-on manually, use **Discover → Manage add-ons / Stremio account**, enter its manifest URL, and select **Install add-on**. Cinemeta provides metadata/catalogs, not playable sources.

The native player supports compatible direct HTTP(S) video through Android's decoder. Torrents and sources requiring proxy headers need a **separate Stremio streaming service** reachable by every participant; configure its URL in the add-on management page. Docker here supplies the room relay and core bridge, not that streaming service. DRM, external-player handoffs, and unsupported codecs cannot play through the native player.

**The room server shares a video URL and playback timing; it does not rebroadcast the movie.** A host's `localhost` or private LAN media URL will not work for friends on other networks. Use media/streaming-service URLs all participants can reach.

### Use Stremio Web on the cinema screen

1. Create a room so you are the host, then select **My room → Stremio Web screen / native player (host)**.
2. Point the beam at the cinema screen and pull the trigger to interact. Use the right stick to scroll. Walking/turning is suspended while the web screen is active.
3. To type, first select the input on the web page with the beam. Open the menu, go to **My room → Web screen controls**, and use **Type into the selected web input**.
4. Use the same controls page for Back, Reload, or returning to native playback.
5. To watch with everyone, select **Share playing HTTP video with room**. A compatible current video URL and position are handed to the native synchronized player.

Web sign-in is separate from native device-link sign-in. Browser playback and account pages are local to the host until a compatible video is shared; guests do not see the host's browser session. Blob/DRM browser videos cannot be handed off this way. The initial web surface updates at 12 Hz; use native playback for smoother movie viewing.

### Play tabletop games

Update the room server as well as the APK for tabletop support: `git pull --ff-only`, then `docker compose --profile internet up -d --build` (or start only `cinema` for USB/LAN use).

Two physical tables are placed near the spawn point. Select **Games** in the bottom menu bar for status, Join/Leave/Reset, and chess promotion choice. You can also use the JOIN/LEAVE/RESET buttons on each table.

- Both players join the same room and select **Join** for the same game. The first gets White/X and the second Black/O; everyone else can spectate.
- **Chess:** select one of your pieces with the beam/trigger, then a highlighted legal destination. Castling, en passant, check/checkmate, stalemate, repetition, and promotion are handled by the server. Promotion defaults to queen; choose rook, bishop, or knight in Games before making that move.
- **Tic-tac-toe:** select an empty square on your turn. The server announces a win or draw.
- Players or the room host can reset a board. Leaving frees a game seat; a replacement can continue the position. Brief reconnects preserve seats and the board. Restarting the server clears games along with rooms.

### Movie audio and protected sources

**My room → Movie audio / track** shows the selected track, reported language/channel count, volume, and a Next audio track control. Audio goes directly to Android, with an explicitly enabled track and playback held at normal speed. Track selection is local to your headset. If the source still has no sound, try another track or an AAC source; the current player does not add missing AC-3/DTS decoders or automatically transcode audio.

Stremio add-ons can return direct streams, torrents, or external provider pages; Stremio is not itself a universal DRM license provider. Compatible Stremio streams already use room synchronization. **Protected DRM playback is not supported by this Unity texture player.** It requires a provider-authorized player, license acquisition for each viewer, and a protected output surface; the screen-mirroring/readback WebView cannot provide that path. No DRM keys, account sessions, or decrypted protected video are shared with peers.

### Talk with friends

Select **Toggle mic**, allow Android microphone permission, and speak. Select it again to mute. **Toggle audio** mutes incoming room voice. Use headphones: the current PCM voice relay has no echo cancellation. Microphone capture is opt-in.

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
- Chess rules: [chess.js](https://github.com/jhlywa/chess.js), [BSD-2-Clause notice](docs/licenses/chess-js-BSD-2-Clause.txt).
- Open Concept Home: [TwilightStudio / KittyFangs.exe](https://jinxxy.com/TwilightStudio/9cvqn), included in the supplied APK. The author states: “There is no copyright terms to this project anymore. So do whatever you want with it.” See [the recorded update](docs/licenses/home-author-update.md).
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

These launch extras are development-build only. The fixture accepts `play`, `pause`, `seek 10`, `avatar`, `tone`, `mute`, and `leave`. `leave` transfers hosting to the headset while keeping the media server available. In Discover, the **Device test** catalog exercises add-on → stream → Rust core → native playback. USB forwarding must be recreated after a device/ADB restart. Stop both servers with Ctrl-C when finished.

## Worldwide multiplayer

See [internet hosting](docs/INTERNET-MULTIPLAYER.md) for the tested Docker server, a no-account temporary Cloudflare tunnel, and a Render deployment template. Every participant needs the same public WSS server and a media URL reachable from their network. The project does not yet have a permanent hosted server.

## Worlds and screen options

Home restores the supplied baked lightmap, emissive materials, normal maps, and underwater sky, with a toggleable bloom pass for emissive lights. Textures are capped at 4K with ASTC compression on Quest. Video edge colors are sampled and smoothed on the GPU each frame, without CPU readback, and add per-pixel bounce to room surfaces. All Home emissive light materials follow the same smoothed colors while ambient lighting is active; their original colors return when it is inactive. Home includes the main screen plus two smaller mirrored displays, all using the same texture and decoder. The reference appearance still requires final visual comparison on the headset; desktop showcase effects and Quest rendering are not assumed identical.

Get the free Home package from [the author](https://jinxxy.com/TwilightStudio/9cvqn). Their current listing removes the old bundled restrictions; see [the recorded update](docs/licenses/home-author-update.md). `scripts/import-private-home.py` imports visual dependencies only, then the Unity build regenerates the Home prefab. Bakery/VRChat editor scripts are not required at runtime.

Your own view shows small hand nubs, with the R6 arms, chest, and legs hidden from your camera. Other players and the stereo room mirror still see your full R6 avatar; toggle it in Settings. The mirror updates at 24 Hz while visible and near the viewer, at 1024 pixels per eye.

**My room → Stremio Web screen** opens the official web app in a hardware-accelerated Android WebView on the cinema screen. The room host uses the beam/trigger and right-stick scroll. Use Web screen controls to enter text into a selected web field, go back, reload, or return to native playback. Web sign-in is separate from native device-link sign-in; credentials/cookies stay on the device. Browser playback is local to the host. **Share playing HTTP video with room** hands a compatible URL and timeline to the synchronized native player. Blob/DRM media and browser account pages are not streamed to guests. The initial web surface updates at 12 Hz; native video remains the high-frame-rate viewing path.

The menu uses smooth panel fades, page transitions, hover/press animation, poster cards, and eased scrolling. **Watch history** switches between imported Stremio history and this headset's recent selections, with Continue Watching and resume. **Settings** exposes walk speed, smooth-turn speed, pointer angle, reclined view, mirror, and video lighting.

Network deployments expose `/source` with the AGPL source URL. If you deploy a modified fork, set `SOURCE_URL` to its corresponding source, including your changes. Third-party dependencies/art retain their own notices.
