// Local development fixture: a Stremio add-on, range-capable media server, and room host.
// Usage: node scripts/device-smoke.mjs /absolute/path/to/demo.mp4
import http from 'node:http';
import { createReadStream, statSync } from 'node:fs';
import readline from 'node:readline';
import WebSocket from 'ws';

const file = process.argv[2];
if (!file) throw new Error('Provide an MP4 file path');
const size = statSync(file).size;
const port = Number(process.env.MEDIA_PORT || 3001);
const base = `http://127.0.0.1:${port}`;
const manifest = { id: 'org.stremiovr.smoke', version: '1.0.0', name: 'Local cinema test', description: 'Generated test pattern for native device validation', resources: ['catalog', 'stream'], types: ['movie'], catalogs: [{ type: 'movie', id: 'demo', name: 'Device test' }] };
const server = http.createServer((req, res) => {
  const json = data => { res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify(data)); };
  if (req.url === '/manifest.json') return json(manifest);
  if (req.url === '/catalog/movie/demo.json') return json({ metas: [{ id: 'demo', type: 'movie', name: 'Cinema test pattern' }] });
  if (req.url === '/stream/movie/demo.json') return json({ streams: [{ name: 'Local H.264/AAC', url: base + '/demo.mp4' }] });
  if (req.url !== '/demo.mp4') { res.writeHead(404); res.end(); return; }
  let start = 0, end = size - 1;
  if (req.headers.range) {
    const match = /^bytes=(\d+)-(\d*)$/.exec(req.headers.range);
    if (!match) { res.writeHead(416, { 'Content-Range': `bytes */${size}` }); res.end(); return; }
    start = Number(match[1]); end = match[2] ? Math.min(Number(match[2]), size - 1) : size - 1;
    if (start > end || start >= size) { res.writeHead(416, { 'Content-Range': `bytes */${size}` }); res.end(); return; }
    res.statusCode = 206; res.setHeader('Content-Range', `bytes ${start}-${end}/${size}`);
  }
  res.setHeader('Content-Type', 'video/mp4'); res.setHeader('Accept-Ranges', 'bytes'); res.setHeader('Content-Length', end - start + 1);
  console.log(`MEDIA ${req.method} ${start}-${end}`);
  if (req.method === 'HEAD') { res.end(); return; }
  const stream = createReadStream(file, { start, end }); stream.on('error', () => res.destroy()); res.on('close', () => stream.destroy()); stream.pipe(res);
});
server.listen(port, '127.0.0.1', () => console.log(`ADDON ${base}/manifest.json`));
const ws = new WebSocket(process.env.ROOM_SERVER || 'ws://127.0.0.1:3000/ws');
const send = data => { if (ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify(data)); };
let previous = '', poseSeen = new Set(), voiceTimer, audioReceived = 0;
ws.on('open', () => send({ type: 'create', profile: { name: 'Device test host', accessory: 'hat', color: '#238caa' } }));
ws.on('message', raw => {
  const data = JSON.parse(raw);
  if (data.type === 'joined') { console.log(`ROOM ${data.roomId}`); send({ type: 'playback', url: base + '/demo.mp4', title: 'Cinema test pattern', paused: true }); }
  if (data.type === 'state') {
    const key = JSON.stringify([data.players.map(p => [p.id, p.connected, p.voice, p.seatedSeat, p.profile]), data.playback]);
    if (key !== previous) console.log('STATE', JSON.stringify({ players: data.players.map(p => ({ name: p.profile.name, connected: p.connected, voice: p.voice, seatedSeat: p.seatedSeat })), playback: data.playback })); previous = key;
  }
  if (data.type === 'pose' && !poseSeen.has(data.playerId)) { poseSeen.add(data.playerId); console.log('POSE received from native participant'); }
  if (data.type === 'audio' && ++audioReceived % 10 === 1) console.log('MIC frames received', audioReceived);
  if (data.type === 'error') console.error(data.message);
});
ws.on('error', error => console.error(error.message));
const input = readline.createInterface({ input: process.stdin });
input.on('line', line => {
  const [command, argument] = line.trim().split(/\s+/);
  if (command === 'world') send({ type: 'world', world: argument });
  else if (command === 'play') send({ type: 'playback', paused: false });
  else if (command === 'pause') send({ type: 'playback', paused: true });
  else if (command === 'seek' && Number.isFinite(Number(argument))) send({ type: 'playback', seek: true, position: Number(argument) });
  else if (command === 'avatar') send({ type: 'profile', profile: { name: 'Changed test avatar', head: 'square', hair: 'tall', outfit: 'formal', accessory: 'glasses', color: '#ce5353' } });
  else if (command === 'tone') {
    clearInterval(voiceTimer); send({ type: 'voice', enabled: true });
    const pcm = Buffer.alloc(3200); for (let i = 0; i < 1600; i++) pcm.writeInt16LE(Math.round(Math.sin(2 * Math.PI * 440 * i / 16000) * 1600), i * 2);
    voiceTimer = setInterval(() => send({ type: 'audio', pcm: pcm.toString('base64') }), 100);
  } else if (command === 'mute') { clearInterval(voiceTimer); send({ type: 'voice', enabled: false }); }
  else if (command === 'leave') { clearInterval(voiceTimer); send({ type: 'leave' }); }
  else console.log('Commands: play, pause, seek <seconds>, avatar, tone, mute, leave');
});
function stop() { clearInterval(voiceTimer); input.close(); ws.close(); server.close(); }
process.on('SIGINT', stop); process.on('SIGTERM', stop);
