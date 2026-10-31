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

export async function createServer(options = {}) {
  const rooms = new Rooms(options);
  const media = options.media === false ? null : new MediaServer(options.mediaOptions);
  const streams = new Map();
  const browser = new RoomBrowser(rooms, options.browserOptions);
  async function handle(ws, message) {
    if (await browser.handle(ws, message)) {
      if(message.type==='browser'&&message.action==='open'){const old=streams.get(ws.roomId);if(old){streams.delete(ws.roomId);await media?.stop(old.job);const room=rooms.rooms.get(ws.roomId);if(room){room.playback={...room.playback,url:'',paused:true,position:0,updatedAt:rooms.now(),revision:room.playback.revision+1};rooms.state(room);}}}
      return;
    }
    if (media && message?.type === 'playback') {
      const room = rooms.rooms.get(ws.roomId), player = room?.players.get(ws.playerId);
      if (!player || player.ws !== ws || room.host !== player.id) throw new Error('Only the host controls playback');
      if (message.position !== undefined && (!Number.isFinite(message.position) || message.position < 0 || message.position > 864000)) throw new Error('Invalid playback position');
      if (message.paused !== undefined && typeof message.paused !== 'boolean') throw new Error('Invalid paused state');
      if(message.url!==undefined)await browser.stop(room.id);
      const old = streams.get(room.id);
      if (message.url !== undefined || message.seek === true) {
        const source = message.url ?? old?.source;
        if (!source) throw new Error('Choose a video first');
        const record = { source, owner: player.id, pending: true }; streams.set(room.id, record);
        await media.stop(old?.job);
        if (streams.get(room.id) !== record || !rooms.rooms.has(room.id)) return;
        room.mediaStatus = 'Server is preparing video and AAC audio…'; room.playback.paused = true; rooms.state(room);
        try {
          const job = await media.start(source, message.position || 0, job => { record.job = job; });
          if (streams.get(room.id) !== record || !rooms.rooms.has(room.id) || room.host !== player.id || player.ws !== ws) {
            await media.stop(job);
            if (streams.get(room.id) === record) { streams.delete(room.id); room.mediaStatus = 'Stream preparation cancelled after the host disconnected'; rooms.state(room); }
            return;
          }
          record.pending = false; room.mediaStatus = '';
          rooms.handle(ws, { type: 'playback', url: job.url, title: message.title ?? room.playback.title, position: job.offset, paused: message.paused ?? true }, job);
        } catch (error) {
          if (streams.get(room.id) === record) { streams.delete(room.id); room.mediaStatus = error.message; rooms.state(room); throw error; }
        }
        return;
      }
      if (old?.pending) throw new Error('Wait for server video preparation to finish');
    }
    rooms.handle(ws, message);
  }
  const handler = async (req, res) => {
    res.setHeader('X-Content-Type-Options', 'nosniff');
    res.setHeader('Content-Type', 'application/json');
    if (media && await media.serve(req, res)) return;
    if (req.method === 'GET' && req.url === '/') { res.setHeader('Content-Type','text/html; charset=utf-8'); res.end(dashboard); return; }
    if (req.method === 'GET' && req.url === '/api/hosting') {res.setHeader('Cache-Control','no-store');res.end(JSON.stringify(await hostingInfo()));return;}
    if (req.method === 'GET' && /^\/screenshots\/apartment-(lounge|screen|overview)\.png$/.test(req.url)) {
      try {const body=await readFile(new URL('../docs'+req.url,import.meta.url));res.setHeader('Content-Type','image/png');res.end(body);}catch{res.writeHead(404);res.end();}return;
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
      res.end(JSON.stringify({ name: 'Movie Night', license: 'AGPL-3.0-only', source: process.env.SOURCE_URL || 'https://github.com/JeremyLakeyJr/stremio-vr' })); return;
    }
    if (req.method === 'GET' && req.url === '/health') {
      res.end(JSON.stringify({ ok: true, rooms: rooms.rooms.size })); return;
    }
    res.writeHead(404); res.end(JSON.stringify({ error: 'Not found' }));
  };
  const server = options.tls ? https.createServer(options.tls, handler) : http.createServer(handler);
  const wss = new WebSocketServer({ noServer: true, maxPayload: 16384 });
  server.on('upgrade', (req, socket, head) => {
    const origin = req.headers.origin;
    const allowed = options.origin || `${options.tls ? 'https' : 'http'}://${req.headers.host}`;
    if (req.url !== '/ws' || (origin && origin !== allowed)) { socket.destroy(); return; }
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
      if (!room) { streams.delete(id); media.stop(record.job).catch(() => {}); }
      else if (record.job?.failed && !record.pending) { room.mediaStatus = record.job.failed; room.playback.paused = true; }
      else if (record.job?.duration && !record.pending) room.playback.duration = record.job.duration;
    }
    for (const room of rooms.rooms.values()) rooms.state(room);
  }, 5000); timer.unref();
  return { server, rooms, close: async () => { clearInterval(timer); for (const ws of wss.clients) ws.terminate(); await new Promise(resolve => wss.close(resolve)); await browser.close(); rooms.close(); if (media) await media.close(); await new Promise(resolve => server.close(resolve)); } };
}
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const tls = process.env.TLS_CERT && process.env.TLS_KEY ? { cert: await readFile(process.env.TLS_CERT), key: await readFile(process.env.TLS_KEY) } : undefined;
  const app = await createServer({ tls, origin: process.env.PUBLIC_ORIGIN, iceServers: JSON.parse(process.env.ICE_SERVERS || '[{"urls":"stun:stun.cloudflare.com:3478"}]') });
  app.server.listen(Number(process.env.PORT || 3000), process.env.HOST || '0.0.0.0', () => console.log(`Cinema listening on ${tls ? 'https' : 'http'}://localhost:${process.env.PORT || 3000}`));
  for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, async () => { await app.close(); process.exit(0); });
}
