# Internet multiplayer

The authoritative Node server relays room state, positions, seating, video timing,
and voice over one WebSocket connection. A public HTTPS/WSS address works across
NATs without LAN discovery, router port forwarding, or a peer-to-peer TURN server.
All users enter the same server address and room code in My room. HTTPS URLs are
automatically converted to WSS in the Quest app.

## No hosting account: temporary tunnel

With Docker and Docker Compose installed:

```sh
docker compose --profile internet up --build
```

Open **http://localhost:3000** and click **Copy invite**. The `internet` container also prints the `https://…trycloudflare.com` address. Enter it
in My room on every headset; create a room and share its code. Keep this computer
and all containers running. Stop with Ctrl-C, then `docker compose down`.

This exposes the cinema server to the internet. It does not expose the project
files or your Stremio account token. Room codes grant access; share them privately.
The address changes on tunnel restart and the service has no uptime guarantee.
Cloudflare documents Quick Tunnels as a development/testing feature:
https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/

## Permanent service: Render

`Dockerfile` builds the pinned Stremio core, FFmpeg, and the Node server in one non-root
container. `render.yaml` defines one paid Starter web service with a health check.
It has not been deployed automatically and no hosting account was created. The Starter declaration is a deployment template, not a verified capacity recommendation for transcoding. Size CPU, temporary disk, and outbound bandwidth for your streams. Compose also runs a Stremio upstream service; Render does not deploy that sidecar automatically.

1. Put this repository in your Git provider account.
2. Create a Render account and a Blueprint from the repository; review the service
   price before deploying. Keep a single instance because room state is in memory.
3. Use the resulting `https://YOUR-SERVICE.onrender.com` address in My room.
4. Connect a second headset using a different internet connection and validate
   sync, voice, seating, reconnects, and host handoff.

Render terminates TLS and supports public WebSockets:
https://render.com/docs/websocket
https://render.com/docs/docker

## Playback and operational limits

The server fetches the selected source, transcodes to H.264/AAC, and serves HLS
through `/media/` on the same HTTPS origin as the room API. All participants use
that server stream; only the server needs to reach the upstream media URL.
Compose supplies an internal Stremio streaming service for torrents/proxy headers.
Private upstream hosts need an exact `MEDIA_ALLOWED_HOSTS` entry; see the README.
Each room uses one transcoder, but outbound bandwidth scales with viewer count.
The default cache is capped at 8 GiB per room and two active room streams. Use
hosting suitable for continuous video delivery; a temporary development tunnel
has not been validated for long movie sessions. Stremio account add-ons and auth
stay on each headset; the selected source URL goes to the server for fetching.

The current service supports eight people per room. Voice uses WebRTC/Opus with
DTLS-SRTP. Configure TURN for restrictive NATs; the HTTP tunnel does not carry
WebRTC audio. Two-headset internet audio validation remains pending. Restarting/redeploying the
server discards rooms. Production accounts, moderation, persistence, multiple
regions, and shared state across replicas are separate work.

## Local container smoke check

```sh
docker build -t stremio-vr:dev .
docker run --rm -p 127.0.0.1:3100:3000 stremio-vr:dev
curl http://127.0.0.1:3100/health
```

## Validation

On 2026-10-28 the no-account tunnel passed health, core conversion, two public WSS clients, exclusive seating, playback, pose delivery, and host transfer. The Quest also joined through the public WSS endpoint. This validates the internet route; two physical headsets on separate networks and prolonged voice performance remain acceptance tasks. Temporary tunnel URLs are intentionally not committed as permanent configuration.
