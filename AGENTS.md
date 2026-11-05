# AGENTS.md — Media Quest VR (Multi-Provider Fork of stremio-vr)

## Project Overview
This project extends `stremio-vr` (Movie Night) to support multiple self-hosted and debrid media backends (**Stremio**, **Jellyfin**, **Remux** (`lostb1t/remux`), **MediaStorm**, **Silo**, and **Nuvio / Mellon** sync) alongside **GPU Hardware Acceleration**, **Smart Remuxing (-c:v copy)**, and **Cloudflare Named/Paid Tunnels**.

## Module Boundaries & Ownership
1. `server/` & `src/network/sync.ts` (Node.js 22+ Room Server & Transcoder):
 - Authoritative playback timeline, WebSocket room state, and FFmpeg HLS transcoding.
 - `server/hwaccel.js`: Startup hardware encoder probe (`h264_nvenc`, `h264_qsv`, `h264_vaapi`, `h264_amf`, `h264_videotoolbox`) and `-c:v copy` remux detection. Bypasses default 720p/30fps/8GiB CPU limits when GPU acceleration is active or when direct remux is possible.
 - `server/tunnel.js`: Cloudflare tunnel integration supporting both free temporary TryCloudflare tunnels and persistent/paid Cloudflare Zero Trust tunnels (via `TUNNEL_TOKEN` or custom domains).
 - Upstream authentication headers (`X-Emby-Token`, `Authorization`, `X-MediaStorm-Key`) sent by the host in `host:load_media` MUST be sanitized against CRLF injection, passed only to FFmpeg `-headers`, and NEVER broadcast to room peers.
2. `unity/` (Unity 6000.0.66f2 Meta Quest Client — C# / IL2CPP / ARM64):
 - Place all media backend adapters in `unity/Assets/Scripts/Providers/`.
 - Use `UnityWebRequest` and `JsonUtility` (avoid external NuGet dependencies that break IL2CPP builds).
 - Keep all provider credentials/tokens in Android Keystore-encrypted local storage; never broadcast tokens to room peers.
3. `core/` (Rust `stremio-vr-core` Bridge):
 - Handles Stremio manifest validation and stream routing.
4. `test/` (Integration & Unit Tests):
 - Run `npm test` to verify room logic, WebSocket wire contracts, hardware probe, and transcoder argument generation.

## Verification Commands
- Install dependencies: `npm ci`
- Build Rust core: `npm run build:core`
- Run core tests: `npm run test:core`
- Run server & room tests: `npm test`
- Rebuild offline solo rules: `npm run build:solo && npm run check:solo`
