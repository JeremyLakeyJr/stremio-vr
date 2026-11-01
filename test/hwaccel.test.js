import test from 'node:test';
import assert from 'node:assert/strict';
import {
  ALLOWED_UPSTREAM_HEADERS,
  sanitizeUpstreamHeaders,
  formatUpstreamHeaders,
  buildTranscodeArgs,
  detectMediaCapabilities
} from '../server/hwaccel.js';
import { createServer } from '../server/index.js';
import { once } from 'node:events';
import WebSocket from 'ws';

test('hwaccel: header sanitization strips CRLF and filters unallowed keys', () => {
  const dirty = {
    'X-Emby-Token': 'valid-token-123\r\nInjected-Header: evil',
    'Authorization': 'Bearer secret\nMalicious',
    'X-MediaStorm-Key': 'key-abc',
    'X-Evil-Header': 'should-be-removed',
    'Cookie': 'session=xyz\r'
  };

  const clean = sanitizeUpstreamHeaders(dirty);

  assert.equal(clean['x-emby-token'], 'valid-token-123Injected-Header: evil');
  assert.equal(clean['authorization'], 'Bearer secretMalicious');
  assert.equal(clean['x-mediastorm-key'], 'key-abc');
  assert.equal(clean['cookie'], 'session=xyz');
  assert.equal(clean['x-evil-header'], undefined);
  assert.ok(!clean['x-emby-token'].includes('\r'));
  assert.ok(!clean['x-emby-token'].includes('\n'));
});

test('hwaccel: formatUpstreamHeaders produces valid FFmpeg header lines', () => {
  const headers = {
    'X-Emby-Token': 'token123',
    'User-Agent': 'MovieNightVR/1.0'
  };

  const formatted = formatUpstreamHeaders(headers);
  assert.equal(formatted[0], '-headers');
  assert.match(formatted[1], /x-emby-token: token123\r\n/);
  assert.match(formatted[1], /user-agent: MovieNightVR\/1.0\r\n/);

  assert.deepEqual(formatUpstreamHeaders({}), []);
});

test('hwaccel: smart remux uses -c:v copy for h264 sources with AAC stereo audio', () => {
  const args = buildTranscodeArgs({
    input: 'http://127.0.0.1:3000/media.mkv',
    sourceCodec: 'h264',
    upstreamHeaders: { 'X-Emby-Token': 'tok' },
    outputDir: '/tmp/test'
  });

  const vcopyIndex = args.indexOf('-c:v');
  assert.ok(vcopyIndex !== -1);
  assert.equal(args[vcopyIndex + 1], 'copy');

  // Verify audio is always converted to AAC stereo for Quest
  const acopyIndex = args.indexOf('-c:a');
  assert.ok(acopyIndex !== -1);
  assert.equal(args[acopyIndex + 1], 'aac');
  assert.equal(args[args.indexOf('-ac') + 1], '2');

  // Verify headers passed before input
  const headerIdx = args.indexOf('-headers');
  const inputIdx = args.indexOf('-i');
  assert.ok(headerIdx !== -1 && headerIdx < inputIdx);
});

test('hwaccel: GPU capabilities build hardware encoder arguments when active', () => {
  const nvencBackend = {
    name: 'nvenc',
    encoder: 'h264_nvenc',
    initArgs: ['-hwaccel', 'cuda'],
    encodeArgs: (b) => ['-c:v', 'h264_nvenc', '-b:v', b],
    scaleFilter: (maxH) => `scale=-2:'min(${maxH},ih)'`
  };

  const capabilities = {
    hwAccel: true,
    backend: nvencBackend,
    maxHeight: 2160,
    maxFps: 60,
    videoBitrate: '15M'
  };

  const args = buildTranscodeArgs({
    input: 'http://127.0.0.1:3000/media.mkv',
    sourceCodec: 'hevc', // Non-h264 requires transcoding
    capabilities,
    outputDir: '/tmp/test'
  });

  assert.ok(args.includes('-hwaccel'));
  assert.ok(args.includes('cuda'));
  assert.ok(args.includes('h264_nvenc'));
  assert.ok(args.includes('15M'));
});

test('hwaccel: CPU fallback enforces 720p / 30fps default limits', () => {
  const capabilities = {
    hwAccel: false,
    backend: null,
    maxHeight: 720,
    maxFps: 30,
    videoBitrate: '3500k'
  };

  const args = buildTranscodeArgs({
    input: 'http://127.0.0.1:3000/media.mkv',
    sourceCodec: 'hevc',
    capabilities,
    outputDir: '/tmp/test'
  });

  assert.ok(args.includes('libx264'));
  assert.ok(args.includes('30'));
  assert.ok(args.includes('3500k'));
});

test('hwaccel: detectMediaCapabilities returns valid structure even with missing ffmpeg', async () => {
  const caps = await detectMediaCapabilities('nonexistent_ffmpeg_bin');
  assert.equal(caps.hwAccel, false);
  assert.equal(caps.maxHeight, 720);
  assert.equal(caps.maxFps, 30);
  assert.equal(caps.maxJobs, 2);
});

test('server: upstreamHeaders and sourceCodec in host:load_media are never leaked to room state', async (t) => {
  const app = await createServer({ media: false });
  app.server.listen(0, '127.0.0.1');
  await once(app.server, 'listening');
  t.after(() => app.close());

  const port = app.server.address().port;
  const wsHost = new WebSocket(`ws://127.0.0.1:${port}/ws`);
  await once(wsHost, 'open');

  const joined = await new Promise(res => {
    wsHost.on('message', raw => {
      const msg = JSON.parse(raw);
      if (msg.type === 'joined') res(msg);
    });
    wsHost.send(JSON.stringify({ type: 'create' }));
  });

  const wsGuest = new WebSocket(`ws://127.0.0.1:${port}/ws`);
  await once(wsGuest, 'open');

  await new Promise(res => {
    wsGuest.on('message', raw => {
      const msg = JSON.parse(raw);
      if (msg.type === 'state') res(msg);
    });
    wsGuest.send(JSON.stringify({ type: 'join', roomId: joined.roomId }));
  });

  // Host sends playback message with secret upstreamHeaders
  const secretHeader = 'secret-api-token-should-not-leak';
  const stateReceivedByGuest = new Promise((res) => {
    wsGuest.on('message', raw => {
      const msg = JSON.parse(raw);
      if (msg.type === 'state') res(raw.toString());
    });
  });

  wsHost.send(JSON.stringify({
    type: 'playback',
    url: 'https://example.com/video.mp4',
    title: 'Secret Movie',
    sourceCodec: 'h264',
    upstreamHeaders: { 'X-Emby-Token': secretHeader }
  }));

  const guestRawState = await stateReceivedByGuest;
  assert.ok(!guestRawState.includes(secretHeader), 'Room state must not leak upstream auth headers');
  assert.ok(!guestRawState.includes('sourceCodec'), 'Room state must not leak sourceCodec');
});

test('hwaccel: audioTrackIndex maps requested audio stream', () => {
  const args = buildTranscodeArgs({
    input: 'http://127.0.0.1:3000/media.mkv',
    audioTrackIndex: 2,
    outputDir: '/tmp/test'
  });

  assert.ok(args.includes('0:a:2?'));
});

test('media: provider URLs in env are auto-appended to allowed hosts', async (t) => {
  const oldJelly = process.env.JELLYFIN_URL;
  try {
    process.env.JELLYFIN_URL = 'http://192.168.1.123:8096';
    const { MediaServer } = await import('../server/media.js?t=' + Date.now());
    const ms = new MediaServer();
    t.after(() => ms.close());
    assert.ok(ms.allowed.includes('192.168.1.123:8096'));
  } finally {
    if (oldJelly) process.env.JELLYFIN_URL = oldJelly;
    else delete process.env.JELLYFIN_URL;
  }
});

test('hosting: named tunnel custom domains are accepted when mode is named', async (t) => {
  const { mkdtemp, writeFile, rm } = await import('node:fs/promises');
  const { join } = await import('node:path');
  const { tmpdir } = await import('node:os');
  const { hostingInfo } = await import('../server/hosting.js');

  const dir = await mkdtemp(join(tmpdir(), 'movie-night-named-'));
  t.after(() => rm(dir, { recursive: true, force: true }));
  const file = join(dir, 'link.json');

  await writeFile(file, JSON.stringify({
    url: 'https://vr.mightysprite.dev',
    mode: 'named',
    updatedAt: Date.now()
  }));

  const info = await hostingInfo(file);
  assert.equal(info.online, true);
  assert.equal(info.url, 'https://vr.mightysprite.dev');
  assert.equal(info.mode, 'named');
});
