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

The `internet` container prints an `https://…trycloudflare.com` address. Enter it
in My room on every headset; create a room and share its code. Keep this computer
and both containers running. Stop with Ctrl-C, then `docker compose down`.

This exposes the cinema server to the internet. It does not expose the project
files or your Stremio account token. Room codes grant access; share them privately.
The address changes on tunnel restart and the service has no uptime guarantee.
Cloudflare documents Quick Tunnels as a development/testing feature:
https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/

## Permanent service: Render

`Dockerfile` builds the pinned Stremio core and the Node relay in one non-root
container. `render.yaml` defines one paid Starter web service with a health check.
It has not been deployed automatically and no hosting account was created.

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

The relay synchronizes a URL; it does not upload or rebroadcast the video itself.
Every participant must be able to reach that media URL. `localhost`, `127.0.0.1`,
and private LAN streaming-service URLs will not work for remote friends. Use a
publicly reachable media source or separately hosted streaming service. Account
add-ons are imported locally; a selected playback URL is shared with the room.

The current service supports eight people per room. PCM voice is bandwidth heavy,
and there is no echo cancellation. Use headphones. Restarting/redeploying the
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
