# Movie Night

**Your own VR apartment. A movie. Your friends.**

Hang out in Open Concept Home on Meta Quest, stream your favorite movies and shows across multiple media providers (Stremio, Jellyfin, MediaStorm, Silo, Nuvio/Mellon), and enjoy synchronized playback together in 1080p / 4K. Pull up a sofa seat, customize your character, or make space for a game of chess or tic-tac-toe.

![Open Concept Home lounge in Movie Night](docs/screenshots/apartment-lounge.png)

| Make yourself at home | A room for your friends |
| --- | --- |
| ![Apartment interior](docs/screenshots/apartment-screen.png) | ![Apartment overview](docs/screenshots/apartment-overview.png) |

*In-engine captures of the included apartment. Open Concept Home by TwilightStudio.*

- **Watch together:** one host picks the video; playback and timeline are synchronized in real-time across the room.
- **Universal media integration:** stream from **Stremio Add-ons**, **Jellyfin / Remux**, **MediaStorm** (Debrid/Usenet), **Silo**, or direct HTTP(S) sources.
- **Hardware GPU transcoding & Smart Remux:** auto-detected NVENC, QSV, VAAPI, VideoToolbox, and AMF acceleration. Compatible H.264 streams bypass transcoding via Smart Remux for 0% CPU/GPU overhead.
- **Cloudflare Tunnels (Free & Named):** zero-configuration TryCloudflare quick tunnels out of the box, with full support for paid/named Cloudflare Zero Trust / Argo tunnels (`TUNNEL_TOKEN`).
- **Subtitles & audio sync:** automatic SRT to WebVTT conversion, in-memory caching, and on-the-fly timing offset adjustments.
- **Scrobbling & watch history:** native progress synchronization to Jellyfin sessions and Trakt / Simkl scrobbling.
- **Make it yours:** place extra movie screens and game tables where you want them.
- **Get comfortable:** sit down, adjust movement speed, or calibrate the view while reclining.
- **Talk and play:** WebRTC voice with echo cancellation, customizable R6 avatars, and shared tabletop games.

[Download the Quest alpha](https://github.com/oOflyeyesOo/Media-VR/releases) · [What still needs testing](docs/ACCEPTANCE.md)

**Playing alone?** Install the Quest app and open it: **Solo mode starts automatically**, with no Docker, room code, or room server. Your apartment layout, boards, and continue watching queue save on the headset.

**Playing with friends?** One person runs the Docker server on a computer. Everyone installs the Quest app. The person hosting opens **http://localhost:3000**, scans the QR code or clicks **Copy invite**, and sends the link plus the headset's room code to friends. Keep the computer running while you hang out.

---

## Goal Checklist

Checked items are implemented; separate acceptance tasks below remain open.

- [x] Native Quest app with OpenXR controls and a Stremio-inspired animated menu.
- [x] Start automatically in server-free solo mode with saved layout and game progress.
- [x] Play chess/tic-tac-toe against local computer opponents or practice both sides.
- [x] Fix Alpha 2's stereo startup bug — stereorecovery verified.
- [x] Include Open Concept Home, high-resolution textures, sofas, and ambient video lighting.
- [x] Customize R6 avatars; show hand nubs in first person and full characters to peers.
- [x] Restore the mirror with render-target state preservation and an On/Off setting.
- [x] Let the host add, move, rotate, and remove extra screens and game tables.
- [x] Synchronize layout for room members and late joiners without resetting games.
- [x] Support movement speed, smooth turning, seating, and reclined viewing controls.
- [x] **Multi-Provider Architecture:** unified client and registry for Stremio, Jellyfin/Remux, MediaStorm, Silo, and Nuvio/Mellon.
- [x] **GPU Hardware Transcoding:** auto-detection for NVIDIA NVENC, Intel QuickSync (QSV), AMD/Intel VAAPI, Apple VideoToolbox, and AMD AMF.
- [x] **Smart Remux:** direct stream passthrough (`-c:v copy`) for H.264 video to eliminate transcoding load.
- [x] **Cloudflare Zero Trust / Argo Tunnels:** support for persistent named domains via `TUNNEL_TOKEN`, with automatic fallback to free TryCloudflare tunnels.
- [x] **Subtitles Pipeline:** on-the-fly SRT to WebVTT conversion, timing offset adjustments (`?offset=±seconds`), and multi-viewer caching.
- [x] **Watch Progress & Scrobbling:** Jellyfin session reporting (`/Sessions/Playing`), Trakt scrobbling (`/scrobble`), and local resume caching.
- [x] **Webhooks & Room Notifications:** rich Discord embeds, Slack blocks, and Apprise/generic webhook alerts.
- [x] **Sharing Dashboard:** QR code rendering on the host web page for fast headset and mobile pairing.
- [x] **Universal Stremio Add-on Protocol:** dynamic manifest installation and stream resolution for Cinemeta, Torrentio, CyberFlix, and anime add-ons.
- [x] Browse HTTP/HTTPS sites in six host-controlled tabs, managed with the menu button.
- [x] Use Jacob G’s original chess set with attribution and grip-to-move controls.
- [x] Remember the in-world web screen's sign-in with encrypted local session backup.
- [x] Replace PCM voice relay with WebRTC/Opus and optional TURN support.
- [ ] Verify shared-browser quality and audio on two physical Quests across different networks.
- [ ] Verify WebRTC audio with two headsets on different networks, including TURN.
- [ ] Validate prolonged movie sessions, audio synchronization, and Quest performance under heavy loads.
- [ ] Add provider-authorized DRM playback; current player does not support it.

---

## Multi-Platform Media Providers

Movie Night VR features a unified provider framework located in `unity/Assets/Scripts/Providers/` and backend media proxy in `server/media.js`.

```mermaid
flowchart TD
    subgraph Unity ["Meta Quest / Unity Client"]
        Reg["MediaProviderRegistry"]
        JF["JellyfinProvider"]
        MS["MediaStormProvider"]
        SI["SiloProvider"]
        ST["StremioAddonProvider"]
        NV["NuvioMellonSync"]
        WP["WatchProgressSync (Trakt / Jellyfin)"]
        Playback["CinemaPlayback"]

        Reg --> JF
        Reg --> MS
        Reg --> SI
        Reg --> ST
        Reg --> NV
        Playback --> WP
    end

    subgraph Server ["Movie Night Docker Server"]
        Router["Server WebSocket / Router"]
        Sanitize["CRLF / Null-Byte Sanitizer"]
        HW["hwaccel.js (GPU Detect & Smart Remux)"]
        FFmpeg["FFmpeg HLS Transcoder"]
        Subs["subtitles.js (SRT to VTT + Offset)"]
        Notif["notifications.js (Discord / Slack)"]

        Router --> Sanitize
        Sanitize --> HW
        HW --> FFmpeg
    end

    JF -.->|"X-Emby-Token"| Router
    MS -.->|"X-MediaStorm-Key"| Router
    SI -.->|"Bearer Token"| Router
    ST -.->|"Direct / Debrid Stream"| Router
    FFmpeg -->|"Shared HLS Stream"| Playback
```

### 1. Jellyfin & Remux (`lostb1t/remux`)
- **Native API Client:** `JellyfinProvider.cs` authenticates via `/Users/AuthenticateByName` or session tokens.
- **Catalogs & Views:** maps movie and TV libraries into the VR menu.
- **Playback Reporting:** `WatchProgressSync.cs` reports live progress ticks to `/Sessions/Playing/Progress` and `/Sessions/Playing/Stopped`.
- **Configuration:** Set `JELLYFIN_URL` in `.env` (e.g. `http://192.168.1.50:8096`).

### 2. MediaStorm (Debrid & Usenet Streaming)
- **Adapter Client:** `MediaStormProvider.cs` maps MediaStorm's catalog search, debrid stream resolution, and watch progress endpoints.
- **Debrid Streaming:** passes authenticated stream URLs with sanitized `X-MediaStorm-Key` upstream headers, supporting Real-Debrid, AllDebrid, Premiumize, and Usenet indexers.
- **Enriched Stream Details:** parses debrid provider tags (e.g. `[MediaStorm: RD+]`), quality tags (4K, 1080p), file sizes, audio track indices, and remote subtitle URLs.
- **Playback Sync:** live watch progress is automatically synchronized with MediaStorm's `/api/v1/user/progress` endpoint.
- **In-VR Setup:** configure your server URL and API key directly in the headset via "Media backends".
- **Configuration:** Set `MEDIASTORM_URL` and `MEDIASTORM_KEY` in `.env`.

### 3. Silo (Go High-Performance Media Server)
- **High-Throughput Streaming:** `SiloProvider.cs` connects to Silo media servers, supporting library browsing and stream resolution.
- **Configuration:** Set `SILO_URL` in `.env`.

### 4. Stremio & Universal Add-on Protocol
- **Built-in Stremio Core:** pinned Rust `stremio-core` bridge resolves manifests, torrents, and proxy headers.
- **Universal Addon Provider (`StremioAddonProvider.cs`):** install *any* standard Stremio add-on manifest URL (Cinemeta, Torrentio, CyberFlix, OpenSubtitles v3, Anime Kitsu) directly into the VR client without running a desktop Stremio client.
- **Device-Link Sign-in:** approve on your phone or PC with a simple device code; add-ons import securely.

### 5. Nuvio & Mellon Add-on Manifest Sync
- **NuvioMellonSync.cs:** imports Mellon and Nuvio add-on bundles and manifests directly into the VR media library.

---

## Hardware Transcoding & Smart Remux

Movie Night automatically detects host GPU capabilities upon startup:

| Accelerator | Hardware Encoder | Target Systems | Compose Setup |
| --- | --- | --- | --- |
| **NVIDIA NVENC** | `h264_nvenc` | GeForce / Quadro / RTX (Linux & Windows) | `compose.gpu.yaml` (nvidia driver) |
| **Intel QuickSync** | `h264_qsv` | Intel Core CPUs with integrated UHD/Iris Graphics | `compose.gpu.yaml` (`/dev/dri`) |
| **AMD / Intel VAAPI** | `h264_vaapi` | Linux AMD Radeon & Intel Graphics | `compose.gpu.yaml` (`/dev/dri`) |
| **Apple VideoToolbox** | `h264_videotoolbox` | macOS Apple Silicon (M1/M2/M3/M4) | Native Node execution |
| **AMD AMF** | `h264_amf` | Windows AMD Radeon GPUs | Native Node execution |
| **CPU Fallback** | `libx264` | Any x86_64 or ARM64 host | Default `compose.yaml` (capped at 720p/30fps) |

### Smart Remux (Zero CPU/GPU Load)
When an upstream source is already encoded in H.264 video with AAC audio (such as standard Web-DL, MP4, or Debrid streams), the transcoder automatically engages **Smart Remux** (`-c:v copy`). This forwards the video stream directly into the shared HLS pipeline without re-encoding, preserving native visual fidelity and consuming near-zero CPU or GPU resources.

To disable remuxing and force re-encoding (e.g., for bandwidth-constrained hosts), set `MEDIA_DISABLE_REMUX=true` in `.env`.

### GPU Docker Deployment
To enable GPU acceleration in Docker:

```sh
# For NVIDIA GPUs:
docker compose -f compose.yaml -f compose.gpu.yaml up -d --build

# For Intel / AMD VAAPI devices on Linux:
docker compose -f compose.yaml -f compose.gpu.yaml up -d --build
```

---

## Cloudflare Tunnels: Free vs. Named / Paid

Movie Night includes automated Cloudflare tunnel integration so friends can connect from anywhere without opening ports or configuring router port forwarding.

### 1. Free Quick Tunnels (Zero Setup, No Account Needed)
- **Default Mode:** if no token is configured, the server starts a temporary TryCloudflare quick tunnel (`https://<random-id>.trycloudflare.com`).
- **Dashboard & QR Code:** Open `http://localhost:3000` to view the active tunnel URL and a **QR Code** that you can scan with your phone or Meta Quest browser to immediately pair your headset.

### 2. Named / Paid Cloudflare Zero Trust Tunnels (Persistent Domain)
- **Stable Hostname:** If you have a Cloudflare domain, you can assign a permanent address (e.g. `https://cinema.yourdomain.com`).
- **Setup:**
  1. In the Cloudflare Zero Trust Dashboard, create a Cloudflare Tunnel pointing to `http://cinema:3000`.
  2. Copy the tunnel token.
  3. In `.env`, add:
     ```env
     TUNNEL_TOKEN=eyJhIjoiY...
     TUNNEL_URL=https://cinema.yourdomain.com
     ```
  4. Start Movie Night: `docker compose up -d`. The tunnel will run under your custom domain automatically!

---

## Subtitles & Timing Offset

Movie Night includes a dedicated subtitle extraction, conversion, and caching engine:
- **SRT to WebVTT:** automatically converts SubRip `.srt` tracks into valid WebVTT on the fly via `/api/subtitles?url=<url>`.
- **Timing Offset Adjustment:** if a subtitle track is slightly ahead or behind the audio in VR, append `&offset=1.5` (or negative, e.g. `&offset=-0.5`). Timestamps are shifted and clamped at `00:00:00.000` so subtitles never display negative timing.
- **In-Memory TTL Cache:** parsed subtitles are cached in memory so when 8 viewers in a room load the same movie, upstream subtitle servers are not repeatedly hit.

---

## Scrobbling & Watch Progress Sync

- **Trakt / Simkl Scrobbling:** Configure `TraktClientId` and `TraktAccessToken` in the client. When playback starts, pauses, or finishes, `WatchProgressSync.cs` sends scrobble events with exact percentage completion.
- **Jellyfin Playback Reporting:** Updates Jellyfin's server-side session ticker so your media library's *Continue Watching* list remains synchronized between your TV, phone, and VR headset.
- **Local Fallback:** Last known resume positions are automatically saved to Quest local storage (`PlayerPrefs`) for serverless Solo playback.

---

## Webhooks & Room Notifications

Get notified in Discord, Slack, or smart home automations when a movie night starts:
- **Discord:** sends rich embeds displaying the movie title, active viewer count (`3/8`), room code, and one-click join link.
- **Slack:** sends formatted mrkdwn blocks.
- **Apprise / Generic:** dispatches clean JSON notifications.
- **Triggers:** fires on `room_created`, `participant_joined`, `playback` start, and `playback_stop`.

Configure your webhook in `.env`:
```env
DISCORD_WEBHOOK_URL=https://discord.com/api/webhooks/...
# or SLACK_WEBHOOK_URL / APPRISE_URL / NOTIFICATION_WEBHOOK_URL
```

---

## System & Diagnostics CLI

To verify your GPU hardware acceleration, media providers, Cloudflare tunnel status, and webhooks in a single command, run:

```sh
npm run diagnose
```

Output:
```
╔═══════════════════════════════════════════════════════════╗
║       Movie Night VR • System & Provider Diagnostics      ║
╚═══════════════════════════════════════════════════════════╝

[1/4] Hardware Transcoding & Smart Remux:
  ✔ GPU Acceleration Active: NVENC (h264_nvenc)
    Preset: max 1080p @ 60fps | Bitrate: 15M
  ✔ Smart Remux Active: H.264 streams bypass transcoding (-c:v copy).

[2/4] Network & Cloudflare Tunnel:
  ✔ Named / Paid Cloudflare Zero Trust Tunnel: Token configured
    Published Domain: https://cinema.mydomain.com

[3/4] Media Backends & Providers:
  ✔ Stremio Streaming Service: Online at http://stremio:11470 (HTTP 200)
  ✔ Jellyfin / Remux: Online at http://192.168.1.50:8096 (HTTP 200)
  ○ MediaStorm (Debrid/Usenet): Not configured
  ○ Silo (Go Backend): Not configured

[4/4] Webhooks & Room Notifications:
  ✔ Discord Webhook: Configured

Diagnostic scan complete.
```

---

## Quick Start: Docker + Quest (Multiplayer)

You need a computer with **Docker Engine or Docker Desktop and Docker Compose v2**, a Quest with developer mode enabled, and the Movie Night APK. Keep the computer running while you watch.

### 1. Start the Server

```sh
# Copy environment configuration
cp .env.example .env

# Edit .env with any optional provider URLs or tunnel tokens
nano .env

# Start Movie Night (with GPU passthrough if available)
docker compose -f compose.yaml -f compose.gpu.yaml up -d --build
```

Wait for services to become healthy, then check status:

```sh
curl http://127.0.0.1:3000/health
npm run diagnose
```

### 2. Connect Your Headset

1. Open **http://localhost:3000** in your web browser.
2. If using the Cloudflare tunnel, copy the invitation link or scan the displayed **QR Code**.
3. In your Quest headset, launch Movie Night and open **My room**.
4. Enter the server WebSocket URL (or the tunnel URL; the app automatically converts `https://` to `wss://`).
5. Click **Connect / create room**. Share the 6-character room code with your friends!

---

## Complete Configuration Reference (`.env`)

| Variable | Default | Purpose |
| --- | --- | --- |
| **Networking & Tunnels** | | |
| `PORT` | `3000` | Host listen port for HTTP/WebSocket traffic |
| `TUNNEL_TOKEN` | *unset* | Cloudflare Zero Trust / Argo tunnel token (uses TryCloudflare if unset) |
| `TUNNEL_URL` | *unset* | Custom domain URL when using a named tunnel (e.g. `https://cinema.mydomain.com`) |
| `TUNNEL_HOSTNAME` | *unset* | Optional alias for `TUNNEL_URL` |
| `ALLOW_PRIVATE_NETWORKS` | `true` | Set `true` to allow streaming from private LAN IPs (192.168.x.x, 10.x.x.x) |
| `MEDIA_ALLOWED_HOSTS` | `stremio:11470` | Comma-separated list of trusted upstream host:port authorities |
| **Media Providers** | | |
| `STREAMING_SERVICE_URL` | `http://stremio:11470` | Internal Stremio engine URL |
| `JELLYFIN_URL` | *unset* | Jellyfin server URL (e.g. `http://192.168.1.50:8096`) |
| `REMUX_URL` | *unset* | Standalone Remux (`lostb1t/remux`) server URL |
| `MEDIASTORM_URL` | *unset* | MediaStorm Debrid/Usenet streaming server URL |
| `MEDIASTORM_KEY` | *unset* | Optional MediaStorm API Key for backend health authentication |
| `SILO_URL` | *unset* | Silo Go streaming backend URL |
| **Hardware Transcoding** | | |
| `MEDIA_DISABLE_REMUX` | `false` | Set `true` to force transcoding even on compatible H.264 streams |
| `MEDIA_MAX_HEIGHT` | `1080` | Maximum transcoded vertical resolution (e.g. `720`, `1080`) |
| `MEDIA_MAX_FPS` | `60` | Maximum transcoded framerate |
| `MEDIA_VIDEO_BITRATE` | `15M` | Video bitrate target for hardware transcoder |
| `MEDIA_MAX_JOBS` | `4` | Maximum simultaneous room transcode jobs |
| `MEDIA_MAX_BYTES` | `25769803776` | Maximum transcode cache size on disk (24 GiB default) |
| `VAAPI_DEVICE` | `/dev/dri/renderD128` | Linux VAAPI DRM rendering device |
| `FFMPEG_BINARY` | `ffmpeg` | Path to FFmpeg executable |
| **Webhooks & Subtitles** | | |
| `DISCORD_WEBHOOK_URL` | *unset* | Discord webhook URL for room and playback alerts |
| `SLACK_WEBHOOK_URL` | *unset* | Slack webhook URL |
| `APPRISE_URL` | *unset* | Apprise / Home Assistant notification webhook URL |
| `NOTIFICATION_WEBHOOK_URL` | *unset* | Generic webhook notification URL |
| `OPENSUBTITLES_API_KEY` | *unset* | Optional OpenSubtitles REST API key for subtitle search |

---

## Build the Quest APK

1. Install Unity **6000.0.66f2** with Android Build Support, Android SDK/NDK Tools, and OpenJDK.
2. Import the required Open Concept Home author download:
   ```sh
   python3 scripts/import-private-home.py "Open Concept Home v1.2 - [2022.3.22f1] [COMPLETED].zip"
   ```
3. Open `unity/` in Unity Editor and select **Movie Night → Configure Quest project**.
4. Select **Movie Night → Build Quest APK**. Output: `unity/Builds/movie-night.apk`.
5. Connect your Quest via USB with developer mode enabled, and install:
   ```sh
   adb install -r unity/Builds/movie-night.apk
   ```

---

## Play Solo — No Server Setup

Install the Quest APK and launch Movie Night. **Solo mode starts automatically**. You can walk, sit, adjust comfort settings, customize your avatar, use the mirror, browse websites, manage tabs with the menu button, and place screens or tabletop games.

Direct HTTP(S) videos play on the headset without a room server. Discover, Stremio account/add-on import, and the web browser connect directly to their online services.

---

## Controls and In-Game Navigation

| Control | Action |
| --- | --- |
| Right-controller beam + trigger | Select menu controls or an empty chair |
| Left-controller menu button | Hide/show and recenter the menu |
| Left stick (menu hidden) | Walk |
| Right stick (menu hidden) | Smooth turn |
| Right stick over menu/web | Scroll |
| B button while seated | Stand and return to aisle |
| Hold right grip + stick up/down | Scroll active web browser tab |
| Both controller grips near piece | Pick up and move chess pieces physically |

---

## Architecture

```
unity/
├── Assets/
│   ├── Scripts/
│   │   ├── CinemaApp.cs         # In-world menu, VR interactions, UI state
│   │   ├── CinemaPlayback.cs    # VideoPlayer sync, multi-provider Load() overload
│   │   ├── MediaLibrary.cs      # Stremio addon client and stream resolver
│   │   ├── RoomClient.cs        # WebSocket room client and state sync
│   │   ├── WebCinema.cs         # Shared server browser WebRTC renderer
│   │   ├── TabletopGames.cs     # Chess and Tic-tac-toe logic
│   │   └── Providers/
│   │       ├── IMediaProvider.cs       # Core provider interfaces and DTOs
│   │       ├── MediaProviderRegistry.cs# Unified provider aggregator & active backend
│   │       ├── JellyfinProvider.cs     # Jellyfin / Remux client & views
│   │       ├── MediaStormProvider.cs   # MediaStorm debrid streaming client
│   │       ├── SiloProvider.cs         # Silo Go backend client
│   │       ├── StremioAddonProvider.cs # Universal Stremio addon manifest wrapper
│   │       ├── NuvioMellonSync.cs      # Mellon/Nuvio addon importer
│   │       └── WatchProgressSync.cs    # Trakt & Jellyfin progress scrobbler
server/
├── index.js                     # HTTP/WebSocket gateway & notification dispatcher
├── hwaccel.js                   # GPU encoder detection & Smart Remux pipeline
├── media.js                     # HLS transcoder, SSRF guard, allowed hosts
├── subtitles.js                 # SRT->VTT converter, offset shifting & TTL cache
├── notifications.js             # Discord, Slack & Apprise payload formatters
├── hosting.js                   # Dashboard UI, QR code generator & invite links
├── tunnel.js                    # Cloudflare quick & named tunnel runner
├── rooms.js                     # Room state, authority, 8-player cap, 15s grace
└── browser.js                   # Chromium worker controller
```

---

## Testing & Quality Assurance

Movie Night VR includes an extensive automated test suite covering unit tests, SSRF security fuzzing, concurrency stress tests, and a 50-cycle soak test:

```sh
# Run syntax and static analysis checks
npm run check

# Run full automated test suite (85 tests)
npm test

# Run provider and system diagnostics
npm run diagnose

# Run standalone soak stress test
node test/soak_stress.js
```

---

## Credits & License

- **Creator:** Jeremy Lakey
- **Multi-Provider & GPU Architecture:** [oOflyeyesOo/Media-VR](https://github.com/oOflyeyesOo/Media-VR)
- **Apartment Environment:** [TwilightStudio / KittyFangs.exe](https://jinxxy.com/TwilightStudio/9cvqn)
- **Chess Set:** [Jacob G](https://www.printables.com/model/32741-chess-set) (CC BY-ND 4.0)
- **Chess Logic:** [chess.js](https://github.com/jhlywa/chess.js) (BSD-2-Clause)
- **License:** GNU Affero General Public License v3 (`AGPL-3.0-only`). See [LICENSE](LICENSE).
