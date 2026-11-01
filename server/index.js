import http from 'node:http';
import { RoomBrowser } from './browser.js';
import https from 'node:https';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { WebSocketServer } from 'ws';
import { Rooms } from './rooms.js';
import { runCore } from './core.js';
import { MediaServer } from './media.js';
import { hostingInfo, dashboard } from './hosting.js';
import { detectMediaCapabilities } from './hwaccel.js';
import { fetchSubtitles, searchOpenSubtitles } from './subtitles.js';
import { sendNotification } from './notifications.js';

export async function createServer(options = {}) {
  const capabilities = options.capabilities ?? await detectMediaCapabilities(options.ffmpegBinary || process.env.FFMPEG_BINARY || 'ffmpeg');
  const rooms = new Rooms(options);
  const media = options.media === false ? null : new MediaServer({ ...options.mediaOptions, capabilities });
  const streams = new Map();
  const browser = new RoomBrowser(rooms, options.browserOptions);

  async function handle(ws, message) {
    if (await browser.handle(ws, message)) {
      if (message.type === 'browser' && message.action === 'open') {
        const old = streams.get(ws.roomId);
        if (old) {
          streams.delete(ws.roomId);
          await media?.stop(old.job);
          const room = rooms.rooms.get(ws.roomId);
          if (room) {
            room.playback = { ...room.playback, url: '', paused: true, position: 0, updatedAt: rooms.now(), revision: room.playback.revision + 1 };
            rooms.state(room);
          }
        }
      }
      return;
    }

    if (media && (message?.type === 'playback' || message?.type === 'host:load_media')) {
      const room = rooms.rooms.get(ws.roomId), player = room?.players.get(ws.playerId);
      if (!player || player.ws !== ws || room.host !== player.id) throw new Error('Only the host controls playback');
      
      const pos = message.type === 'host:load_media' ? (message.startTimeSeconds ?? 0) : message.position;
      if (pos !== undefined && (!Number.isFinite(pos) || pos < 0 || pos > 864000)) throw new Error('Invalid playback position');
      if (message.paused !== undefined && typeof message.paused !== 'boolean') throw new Error('Invalid paused state');
      if (message.url !== undefined) await browser.stop(room.id);
      
      const old = streams.get(room.id);
      if (message.url !== undefined || message.seek === true) {
        const source = message.url ?? old?.source;
        if (!source) throw new Error('Choose a video first');
        const record = { source, owner: player.id, pending: true }; streams.set(room.id, record);
        await media.stop(old?.job);
        if (streams.get(room.id) !== record || !rooms.rooms.has(room.id)) return;
        room.mediaStatus = 'Server is preparing video and AAC audio.'; room.playback.paused = true; rooms.state(room);
        try {
          const job = await media.start(source, pos || 0, job => { record.job = job; }, {
            sourceCodec: message.sourceCodec,
            audioTrackIndex: message.audioTrackIndex,
            upstreamHeaders: message.upstreamHeaders
          });
          if (streams.get(room.id) !== record || !rooms.rooms.has(room.id) || room.host !== player.id || player.ws !== ws) {
            await media.stop(job);
            if (streams.get(room.id) === record) { streams.delete(room.id); room.mediaStatus = 'Stream preparation cancelled after the host disconnected'; rooms.state(room); }
            return;
          }
          record.pending = false; room.mediaStatus = '';
          // Broadcast clean playback state without exposing host upstreamHeaders or internal codec
          rooms.handle(ws, {
            type: 'playback',
            url: job.url,
            title: message.title ?? room.playback.title,
            position: job.offset,
            paused: message.paused ?? true
          }, job);

          hostingInfo().then(info => {
            sendNotification({
              event: 'playback',
              title: message.title ?? room.playback.title ?? 'Movie',
              roomId: room.id,
              inviteUrl: info.url,
              clientCount: room?.players?.size ?? 1
            }).catch(() => {});
          }).catch(() => {});
        } catch (error) {
          if (streams.get(room.id) === record) { streams.delete(room.id); room.mediaStatus = error.message; rooms.state(room); throw error; }
        }
        return;
      }
      if (old?.pending) throw new Error('Wait for server video preparation to finish');
    }
    rooms.handle(ws, message);
    if (message?.type === 'create' && ws.roomId) {
      hostingInfo().then(info => {
        sendNotification({
          event: 'room_created',
          roomId: ws.roomId,
          inviteUrl: info.url,
          clientCount: 1
        }).catch(() => {});
      }).catch(() => {});
    }
    if (message?.type === 'join' && ws.roomId) {
      const room = rooms.rooms.get(ws.roomId);
      hostingInfo().then(info => {
        sendNotification({
          event: 'participant_joined',
          roomId: ws.roomId,
          inviteUrl: info.url,
          clientCount: room?.players?.size ?? 1
        }).catch(() => {});
      }).catch(() => {});
    }
    if (message?.type === 'playback' && message.url === '' && ws.roomId) {
      const room = rooms.rooms.get(ws.roomId);
      hostingInfo().then(info => {
        sendNotification({
          event: 'playback_stop',
          roomId: ws.roomId,
          inviteUrl: info.url,
          clientCount: room?.players?.size ?? 1
        }).catch(() => {});
      }).catch(() => {});
    }
  }

  const handler = async (req, res) => {
    res.setHeader('X-Content-Type-Options', 'nosniff');
    res.setHeader('Content-Type', 'application/json');
    if (media && await media.serve(req, res)) return;
    if (req.method === 'GET' && req.url === '/') { res.setHeader('Content-Type', 'text/html; charset=utf-8'); res.end(dashboard); return; }
    if (req.method === 'GET' && req.url === '/api/hosting') { res.setHeader('Cache-Control', 'no-store'); res.end(JSON.stringify(await hostingInfo())); return; }

    if (req.method === 'GET' && req.url === '/api/providers') {
      res.setHeader('Cache-Control', 'no-store');
      const check = async (u, headers = {}) => {
        if (!u) return { configured: false, online: false };
        try {
          const parsed = new URL(u);
          const r = await fetch(parsed.origin, { headers, signal: AbortSignal.timeout(2500) });
          return { configured: true, online: r.status < 500, status: r.status };
        } catch {
          return { configured: true, online: false };
        }
      };
      const msHeaders = process.env.MEDIASTORM_KEY ? { 'X-MediaStorm-Key': process.env.MEDIASTORM_KEY } : {};
      const siloHeaders = process.env.SILO_TOKEN ? { 'Authorization': `Bearer ${process.env.SILO_TOKEN}` } : {};
      const [stremio, jellyfin, mediastorm, silo, remux] = await Promise.all([
        check(process.env.STREAMING_SERVICE_URL || 'http://stremio:11470'),
        check(process.env.JELLYFIN_URL),
        check(process.env.MEDIASTORM_URL, msHeaders),
        check(process.env.SILO_URL, siloHeaders),
        check(process.env.REMUX_URL)
      ]);
      res.end(JSON.stringify({
        ok: true,
        providers: { stremio, jellyfin, mediastorm, silo, remux }
      }));
      return;
    }

    if (req.method === 'GET' && (req.url === '/api/subtitles/search' || req.url.startsWith('/api/subtitles/search?'))) {
      try {
        const query = new URL(req.url, 'http://localhost').searchParams;
        const q = query.get('query');
        const imdbId = query.get('imdb_id') || query.get('imdb');
        const type = query.get('type');
        const season = query.get('season');
        const episode = query.get('episode');
        const languages = query.get('languages') || 'en';
        const searchResult = await searchOpenSubtitles({ query: q, imdbId, type, season, episode, languages });
        res.setHeader('Content-Type', 'application/json');
        res.end(JSON.stringify(searchResult));
      } catch (err) {
        res.writeHead(500); res.end(JSON.stringify({ error: err.message }));
      }
      return;
    }

    if (req.method === 'GET' && (req.url === '/api/subtitles' || req.url.startsWith('/api/subtitles?'))) {
      try {
        const query = new URL(req.url, 'http://localhost').searchParams;
        const subUrl = query.get('url');
        if (!subUrl) { res.writeHead(400); res.end(JSON.stringify({ error: 'Missing subtitle url' })); return; }
        const offset = parseFloat(query.get('offset') || '0');
        const vtt = await fetchSubtitles(subUrl, media?.allowed || [], { offset });
        res.setHeader('Content-Type', 'text/vtt; charset=utf-8');
        res.setHeader('Cache-Control', 'public, max-age=3600');
        res.end(vtt);
      } catch (err) {
        res.writeHead(502); res.end(JSON.stringify({ error: err.message }));
      }
      return;
    }

    if (req.method === 'GET' && /^\/screenshots\/apartment-(lounge|screen|overview)\.png$/.test(req.url)) {
      try { const body = await readFile(new URL('../docs' + req.url, import.meta.url)); res.setHeader('Content-Type', 'image/png'); res.end(body); } catch { res.writeHead(404); res.end(); } return;
    }
    if (req.method === 'POST' && req.url === '/api/core') {
      try {
        req.setEncoding('utf8');
        let input = ''; let bytes = 0;
        for await (const chunk of req) { bytes += Buffer.byteLength(chunk); if (bytes > 65536) { res.writeHead(413); res.end(JSON.stringify({ error: 'Request too large' })); return; } input += chunk.toString(); }
        let data; try { data = JSON.parse(input); } catch { res.writeHead(400); res.end(JSON.stringify({ error: 'Invalid JSON' })); return; }
        if (data?.operation === 'resolve' && process.env.STREAMING_SERVICE_URL) data.streamingService = process.env.STREAMING_SERVICE_URL;
        const result = await runCore(data, options.coreBinary);
        res.end(JSON.stringify(result));
      } catch (error) { res.writeHead(error.status || 500); res.end(JSON.stringify({ error: error.message })); }
      return;
    }
    if (req.method === 'GET' && req.url === '/source') {
      res.end(JSON.stringify({ name: 'Movie Night', license: 'AGPL-3.0-only', source: process.env.SOURCE_URL || 'https://github.com/oOflyeyesOo/Media-VR' })); return;
    }
    if (req.method === 'GET' && req.url === '/api/system/capabilities') {
      res.setHeader('Content-Type', 'application/json');
      res.end(JSON.stringify({
        version: '0.2.0',
        transcoder: capabilities,
        streamingService: Boolean(process.env.STREAMING_SERVICE_URL),
        providers: {
          jellyfin: Boolean(process.env.JELLYFIN_URL),
          remux: Boolean(process.env.REMUX_URL),
          mediastorm: Boolean(process.env.MEDIASTORM_URL),
          silo: Boolean(process.env.SILO_URL)
        },
        tunnels: {
          cloudflare: Boolean(process.env.TUNNEL_TOKEN || process.env.TUNNEL_URL),
          tailscale: Boolean(process.env.TAILSCALE_URL || process.env.TS_HOSTNAME)
        }
      }));
      return;
    }
    if (req.method === 'GET' && req.url === '/health') {
      res.end(JSON.stringify({
        ok: true,
        rooms: rooms.rooms.size,
        transcoder: {
          hwAccel: capabilities.hwAccel,
          encoder: capabilities.backend?.encoder ?? 'libx264',
          maxHeight: capabilities.maxHeight,
          maxFps: capabilities.maxFps
        }
      }));
      return;
    }
    res.writeHead(404); res.end(JSON.stringify({ error: 'Not found' }));
  };

  const server = options.tls ? https.createServer(options.tls, handler) : http.createServer(handler);
  const wss = new WebSocketServer({ noServer: true, maxPayload: 16384 });
  server.on('upgrade', (req, socket, head) => {
    const origin = req.headers.origin;
    let allowed = false;
    if (!origin) {
      allowed = true;
    } else if (options.origin && origin === options.origin) {
      allowed = true;
    } else {
      try {
        const originUrl = new URL(origin);
        const hostHeader = (req.headers.host || '').toLowerCase();
        if (originUrl.host.toLowerCase() === hostHeader) allowed = true;
      } catch {}
    }
    if (req.url !== '/ws' || !allowed) { socket.destroy(); return; }
    wss.handleUpgrade(req, socket, head, ws => wss.emit('connection', ws));
  });

  wss.on('connection', ws => {
    ws.alive = true; let count = 0, since = Date.now();
    ws.on('pong', () => { ws.alive = true; });
    ws.on('message', async raw => {
      if (Date.now() - since > 1000) { since = Date.now(); count = 0; }
      if (++count > 80) { ws.close(1008, 'Rate limit'); return; }
      try { await handle(ws, JSON.parse(raw.toString())); } catch (error) { rooms.send(ws, { type: 'error', message: error.message }); }
    });
    ws.on('error', () => {});
    ws.on('close', () => rooms.leave(ws));
  });

  const timer = setInterval(() => {
    for (const ws of wss.clients) { if (!ws.alive) { ws.terminate(); continue; } ws.alive = false; ws.ping(); }
    for (const [id, record] of streams) {
      const room = rooms.rooms.get(id);
      if (!room) { streams.delete(id); media?.stop(record.job).catch(() => {}); }
      else if (record.job?.failed && !record.pending) { room.mediaStatus = record.job.failed; room.playback.paused = true; }
      else if (record.job?.duration && !record.pending) room.playback.duration = record.job.duration;
    }
    for (const room of rooms.rooms.values()) rooms.state(room);
  }, 5000); timer.unref();

  return {
    server,
    rooms,
    capabilities,
    close: async () => {
      clearInterval(timer);
      for (const ws of wss.clients) ws.terminate();
      await new Promise(resolve => wss.close(resolve));
      await browser.close();
      rooms.close();
      if (media) await media.close();
      await new Promise(resolve => server.close(resolve));
    }
  };
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const tls = process.env.TLS_CERT && process.env.TLS_KEY ? { cert: await readFile(process.env.TLS_CERT), key: await readFile(process.env.TLS_KEY) } : undefined;
  const app = await createServer({ tls, origin: process.env.PUBLIC_ORIGIN, iceServers: JSON.parse(process.env.ICE_SERVERS || '[{"urls":"stun:stun.cloudflare.com:3478"}]') });
  app.server.listen(Number(process.env.PORT || 3000), process.env.HOST || '0.0.0.0', () => console.log(`Cinema listening on ${tls ? 'https' : 'http'}://localhost:${process.env.PORT || 3000}`));
  for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, async () => { await app.close(); process.exit(0); });
}
