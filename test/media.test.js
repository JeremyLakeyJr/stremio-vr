import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { once } from 'node:events';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import WebSocket from 'ws';
import { MediaServer, destination, rewritePlaylist } from '../server/media.js';
import { createServer } from '../server/index.js';
const exec = promisify(execFile);

test('upstream policy blocks private, mapped, rebinding and credential URLs', async () => {
  for (const url of ['http://127.0.0.1/a', 'http://[::1]/', 'http://[::ffff:127.0.0.1]/', 'http://169.254.169.254/', 'http://192.168.0.1/', 'file:///etc/passwd', 'https://user:secret@example.com/']) await assert.rejects(destination(url));
  await assert.rejects(destination('https://example.com', [], async () => [{ address: '8.8.8.8', family: 4 }, { address: '10.0.0.1', family: 4 }]));
  assert.equal((await destination('http://127.0.0.1:9000/', ['127.0.0.1:9000'])).address.address, '127.0.0.1');
  await assert.rejects(destination('http://127.0.0.1:9001/', ['127.0.0.1:9000']));
});
test('HLS rewrites nested playlists, maps and segment URLs and rejects encryption', () => {
  const found = [];
  const result = rewritePlaylist('#EXTM3U\n#EXT-X-MAP:URI="init.mp4"\n../variant/list.m3u8\nsegment.ts?token=private\n', 'https://example.com/path/list.m3u8', url => { found.push(url); return '/safe/' + found.length; });
  assert.deepEqual(found, ['https://example.com/path/init.mp4', 'https://example.com/variant/list.m3u8', 'https://example.com/path/segment.ts?token=private']);
  assert.ok(!result.includes('private')); assert.match(result, /URI="\/safe\/1"/);
  assert.throws(() => rewritePlaylist('#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI="key"', 'https://example.com', () => ''), /licensed/);
});

async function fixture(t) {
  const dir = await mkdtemp(join(tmpdir(), 'movie-night-test-'));
  t.after(() => rm(dir, { recursive: true, force: true }));
  const file = join(dir, 'source.mkv');
  await exec('ffmpeg', ['-v', 'error', '-f', 'lavfi', '-i', 'testsrc2=size=320x180:rate=30', '-f', 'lavfi', '-i', 'sine=frequency=440:sample_rate=48000', '-t', '6', '-c:v', 'libx264', '-threads', '1', '-c:a', 'ac3', file]);
  await exec('ffmpeg', ['-v', 'error', '-i', file, '-c', 'copy', '-f', 'hls', '-hls_time', '2', join(dir, 'source.m3u8')]);
  const data = await readFile(file);
  const source = http.createServer(async (req, res) => {
    if (/^\/source(?:\.m3u8|\d+\.ts)$/.test(req.url)) {
      const body = await readFile(join(dir, req.url.slice(1))); res.setHeader('Content-Type', req.url.endsWith('.m3u8') ? 'application/vnd.apple.mpegurl' : 'video/mp2t'); res.end(body); return;
    }
    if (req.url === '/redirect') { res.writeHead(302, { Location: 'http://127.0.0.1:1/private' }); res.end(); return; }
    const range = /bytes=(\d+)-(\d*)/.exec(req.headers.range || '');
    const start = range ? Number(range[1]) : 0, end = range?.[2] ? Number(range[2]) : data.length - 1;
    res.writeHead(range ? 206 : 200, { 'Content-Type': 'video/x-matroska', 'Content-Length': end - start + 1, 'Accept-Ranges': 'bytes', ...(range ? { 'Content-Range': `bytes ${start}-${end}/${data.length}` } : {}) });
    res.end(data.subarray(start, end + 1));
  }); source.listen(0, '127.0.0.1'); await once(source, 'listening'); t.after(() => new Promise(resolve => source.close(resolve)));
  const host = `127.0.0.1:${source.address().port}`;
  return { url: `http://${host}/source.mkv`, allowedHosts: [host] };
}
test('FFmpeg converts AC3 to shared H264/AAC HLS, supports offset and cleans jobs', { timeout: 60000 }, async t => {
  const input = await fixture(t); const media = new MediaServer({ allowedHosts: input.allowedHosts }); t.after(() => media.close());
  const job = await media.start(input.url, 2);
  while (!job.finished) await new Promise(resolve => setTimeout(resolve, 50));
  assert.equal(job.failed, null); assert.equal(job.offset, 2);
  const { stdout } = await exec('ffprobe', ['-v', 'error', '-show_streams', '-of', 'json', join(job.dir, 'index.m3u8')]);
  const streams = JSON.parse(stdout).streams;
  assert.ok(streams.some(s => s.codec_name === 'h264')); assert.ok(streams.some(s => s.codec_name === 'aac' && s.channels === 2));
  assert.ok(!String(await readFile(join(job.dir, 'index.m3u8'))).includes(input.url));
  const path = job.dir; await media.stop(job); assert.equal(media.jobs.size, 0); await assert.rejects(readFile(join(path, 'index.m3u8')));
  const hls = await media.start(input.url.replace('.mkv', '.m3u8')); assert.match(hls.url, /^\/media\//); await media.stop(hls);
  await assert.rejects(media.start(input.url.replace('/source.mkv', '/redirect')), /transcode|timed out/);
});
function receive(ws, type, predicate = () => true) { return new Promise((resolve, reject) => {
  const timer = setTimeout(() => { ws.off('message', listener); reject(new Error('Timed out: ' + type)); }, 50000);
  function listener(raw) { const message = JSON.parse(raw); if (message.type === type && predicate(message)) { clearTimeout(timer); ws.off('message', listener); resolve(message); } } ws.on('message', listener);
}); }
test('real peers receive one server stream; guests cannot start jobs; seek replaces stream', { timeout: 120000 }, async t => {
  const input = await fixture(t), app = await createServer({ mediaOptions: { allowedHosts: input.allowedHosts } });
  app.server.listen(0, '127.0.0.1'); await once(app.server, 'listening'); t.after(() => app.close());
  const base = `http://127.0.0.1:${app.server.address().port}`;
  const host = new WebSocket(base.replace('http:', 'ws:') + '/ws'); await once(host, 'open');
  let pending = receive(host, 'joined'); host.send(JSON.stringify({ type: 'create' })); const joined = await pending;
  const guest = new WebSocket(base.replace('http:', 'ws:') + '/ws'); await once(guest, 'open');
  pending = receive(guest, 'state'); guest.send(JSON.stringify({ type: 'join', roomId: joined.roomId })); await pending;
  pending = receive(guest, 'error'); guest.send(JSON.stringify({ type: 'playback', url: input.url })); assert.match((await pending).message, /host/);
  const hostState = receive(host, 'state', s => s.playback.url.startsWith('/media/'));
  pending = receive(guest, 'state', s => s.playback.url.startsWith('/media/')); host.send(JSON.stringify({ type: 'playback', url: input.url }));
  const state = await pending; assert.equal(state.playback.url, (await hostState).playback.url);
  const response = await fetch(base + state.playback.url); assert.equal(response.status, 200); const playlist = await response.text();
  const segment = playlist.split('\n').find(line => line.endsWith('.ts')); assert.equal((await fetch(new URL(segment, base + state.playback.url))).status, 200);
  assert.equal((await fetch(base + state.playback.url.replace(/\/[^/]+\/index/, '/wrong/index'))).status, 404);
  pending = receive(guest, 'state', s => s.playback.mediaStart === 2); host.send(JSON.stringify({ type: 'playback', seek: true, position: 2 })); const sought = await pending;
  assert.notEqual(sought.playback.url, state.playback.url); assert.equal(sought.playback.position, 2);
  assert.equal((await fetch(base + state.playback.url)).status, 404);
});
