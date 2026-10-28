import http from 'node:http';
import https from 'node:https';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { WebSocketServer } from 'ws';
import { Rooms } from './rooms.js';
import { runCore } from './core.js';

export async function createServer(options = {}) {
  const rooms = new Rooms(options);
  const handler = async (req, res) => {
    res.setHeader('X-Content-Type-Options', 'nosniff');
    res.setHeader('Content-Type', 'application/json');
    if (req.method === 'POST' && req.url === '/api/core') {
      try {
        req.setEncoding('utf8');
        let input = ''; let bytes = 0;
        for await (const chunk of req) { bytes += Buffer.byteLength(chunk); if (bytes > 65536) { res.writeHead(413); res.end(JSON.stringify({ error: 'Request too large' })); return; } input += chunk.toString(); }
        let data; try { data = JSON.parse(input); } catch { res.writeHead(400); res.end(JSON.stringify({ error: 'Invalid JSON' })); return; }
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
    ws.on('message', raw => {
      if (Date.now() - since > 1000) { since = Date.now(); count = 0; }
      if (++count > 80) { ws.close(1008, 'Rate limit'); return; }
      try { rooms.handle(ws, JSON.parse(raw.toString())); } catch (error) { rooms.send(ws, { type: 'error', message: error.message }); }
    });
    ws.on('error', () => {});
    ws.on('close', () => rooms.leave(ws));
  });
  const timer = setInterval(() => {
    for (const ws of wss.clients) { if (!ws.alive) { ws.terminate(); continue; } ws.alive = false; ws.ping(); }
    for (const room of rooms.rooms.values()) rooms.state(room);
  }, 5000); timer.unref();
  return { server, rooms, close: async () => { clearInterval(timer); for (const ws of wss.clients) ws.terminate(); await new Promise(resolve => wss.close(resolve)); rooms.close(); await new Promise(resolve => server.close(resolve)); } };
}
if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const tls = process.env.TLS_CERT && process.env.TLS_KEY ? { cert: await readFile(process.env.TLS_CERT), key: await readFile(process.env.TLS_KEY) } : undefined;
  const app = await createServer({ tls, origin: process.env.PUBLIC_ORIGIN, iceServers: JSON.parse(process.env.ICE_SERVERS || '[]') });
  app.server.listen(Number(process.env.PORT || 3000), process.env.HOST || '0.0.0.0', () => console.log(`Cinema listening on ${tls ? 'https' : 'http'}://localhost:${process.env.PORT || 3000}`));
  for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, async () => { await app.close(); process.exit(0); });
}
