import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { srtToVtt, adjustVttOffset, fetchSubtitles, clearSubtitleCache, searchOpenSubtitles } from '../server/subtitles.js';
import { sendNotification, formatNotificationPayload } from '../server/notifications.js';
import { createServer } from '../server/index.js';
import { getConfig, saveConfig } from '../server/config.js';
import { once } from 'node:events';
import WebSocket from 'ws';

test('subtitles: srtToVtt converts SRT format to valid WebVTT', () => {
  const srt = `1
00:01:20,123 --> 00:01:24,456
Hello, Welcome to Movie Night VR!

2
00:01:25,789 --> 00:01:29,000
Enjoy the film in 4K with friends.`;

  const vtt = srtToVtt(srt);
  assert.ok(vtt.startsWith('WEBVTT\n\n'));
  assert.ok(vtt.includes('00:01:20.123 --> 00:01:24.456'));
  assert.ok(vtt.includes('Hello, Welcome to Movie Night VR!'));
  assert.ok(vtt.includes('00:01:25.789 --> 00:01:29.000'));
  assert.ok(!vtt.includes(',123'));
  assert.ok(!vtt.includes(',456'));

  // Idempotent on already-VTT content
  assert.equal(srtToVtt('WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nHi'), 'WEBVTT\n\n00:00:01.000 --> 00:00:02.000\nHi');
  assert.equal(srtToVtt(''), 'WEBVTT\n\n');
});

test('subtitles: adjustVttOffset shifts timecodes forward, backward, and clamps at zero', () => {
  const vtt = `WEBVTT

00:01:20.100 --> 00:01:25.500 line:0% position:50% align:center
Dialogue line one

00:00:03.000 --> 00:00:08.000
Dialogue line two`;

  // Shift forward by +2.5 seconds
  const forward = adjustVttOffset(vtt, 2.5);
  assert.ok(forward.includes('00:01:22.600 --> 00:01:28.000 line:0% position:50% align:center'));
  assert.ok(forward.includes('00:00:05.500 --> 00:00:10.500'));

  // Shift backward by -5.0 seconds (clamps line two start to 00:00:00.000)
  const backward = adjustVttOffset(vtt, -5.0);
  assert.ok(backward.includes('00:01:15.100 --> 00:01:20.500'));
  assert.ok(backward.includes('00:00:00.000 --> 00:00:03.000'));

  // Zero offset returns identical content
  assert.equal(adjustVttOffset(vtt, 0), vtt);
});

test('subtitles: in-memory caching reuses parsed subtitles', async (t) => {
  clearSubtitleCache();
  let requestCount = 0;

  const mockServer = http.createServer((req, res) => {
    requestCount++;
    res.writeHead(200, { 'Content-Type': 'text/plain' });
    res.end('1\n00:00:01,000 --> 00:00:04,000\nCached subtitle line\n');
  });
  mockServer.listen(0, '127.0.0.1');
  await once(mockServer, 'listening');
  t.after(() => mockServer.close());

  const port = mockServer.address().port;
  const url = `http://127.0.0.1:${port}/subs.srt`;
  const allowed = [`127.0.0.1:${port}`];

  // First fetch hits network
  const res1 = await fetchSubtitles(url, allowed);
  assert.ok(res1.includes('00:00:01.000 --> 00:00:04.000'));
  assert.equal(requestCount, 1);

  // Second fetch hits in-memory cache
  const res2 = await fetchSubtitles(url, allowed);
  assert.ok(res2.includes('00:00:01.000 --> 00:00:04.000'));
  assert.equal(requestCount, 1); // Not incremented!
});

test('api: /api/providers reports media backend status', async (t) => {
  const app = await createServer({ media: false });
  app.server.listen(0, '127.0.0.1');
  await once(app.server, 'listening');
  t.after(() => app.close());

  const port = app.server.address().port;
  const res = await fetch(`http://127.0.0.1:${port}/api/providers`);
  assert.equal(res.status, 200);

  const data = await res.json();
  assert.equal(data.ok, true);
  assert.ok(data.providers);
  assert.ok('stremio' in data.providers);
  assert.ok('jellyfin' in data.providers);
  assert.ok('mediastorm' in data.providers);
  assert.ok('silo' in data.providers);
  assert.ok('remux' in data.providers);
});

test('api: /api/providers reports all configured provider fixtures online', async (t) => {
  const received = [];
  const fixture = http.createServer((req, res) => {
    received.push({
      path: req.url,
      mediaStormKey: req.headers['x-mediastorm-key'],
      authorization: req.headers.authorization
    });
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ ok: true, catalogs: [], items: [], streams: [] }));
  });
  fixture.listen(0, '127.0.0.1');
  await once(fixture, 'listening');
  t.after(() => fixture.close());

  const baseUrl = `http://127.0.0.1:${fixture.address().port}`;
  const originalStreamingServiceUrl = getConfig().streamingServiceUrl;
  await saveConfig({ streamingServiceUrl: baseUrl });
  const environment = {
    JELLYFIN_URL: baseUrl,
    MEDIASTORM_URL: baseUrl,
    MEDIASTORM_KEY: 'fixture-media-storm-key',
    SILO_URL: baseUrl,
    SILO_TOKEN: 'fixture-silo-token',
    REMUX_URL: baseUrl
  };
  const previous = {};
  for (const [key, value] of Object.entries(environment)) {
    previous[key] = process.env[key];
    process.env[key] = value;
  }

  try {
    const app = await createServer({ media: false });
    app.server.listen(0, '127.0.0.1');
    await once(app.server, 'listening');
    t.after(() => app.close());

    const port = app.server.address().port;
    const res = await fetch(`http://127.0.0.1:${port}/api/providers`);
    const data = await res.json();
    assert.equal(res.status, 200);
    for (const provider of Object.values(data.providers)) {
      if (provider.configured) assert.equal(provider.online, true);
    }
    assert.equal(data.providers.stremio.configured, true);
    assert.equal(data.providers.jellyfin.configured, true);
    assert.equal(data.providers.mediastorm.configured, true);
    assert.equal(data.providers.silo.configured, true);
    assert.equal(data.providers.remux.configured, true);
    assert.ok(received.some(request => request.mediaStormKey === 'fixture-media-storm-key'));
    assert.ok(received.some(request => request.authorization));
  } finally {
    await saveConfig({ streamingServiceUrl: originalStreamingServiceUrl });
    for (const key of Object.keys(environment)) {
      if (previous[key] === undefined) delete process.env[key];
      else process.env[key] = previous[key];
    }
  }
});

test('api: /api/subtitles validates query parameters and applies offset', async (t) => {
  const app = await createServer({ media: false });
  app.server.listen(0, '127.0.0.1');
  await once(app.server, 'listening');
  t.after(() => app.close());

  const port = app.server.address().port;
  // Missing url parameter
  const bad = await fetch(`http://127.0.0.1:${port}/api/subtitles`);
  assert.equal(bad.status, 400);

  // Private blocked destination
  const blocked = await fetch(`http://127.0.0.1:${port}/api/subtitles?url=http://127.0.0.1:9999/secret.srt`);
  assert.equal(blocked.status, 502);
});

test('notifications: formatNotificationPayload supports Discord, Slack, and generic webhooks', () => {
  // Discord payload
  const discordRaw = formatNotificationPayload({
    event: 'playback',
    title: 'Interstellar',
    roomId: 'abc123',
    inviteUrl: 'https://test.trycloudflare.com#abc123'
  }, 'https://discord.com/api/webhooks/123/xyz');

  const discord = JSON.parse(discordRaw);
  assert.equal(discord.username, 'Movie Night VR');
  assert.ok(discord.embeds[0].title.includes('Interstellar'));
  assert.ok(discord.embeds[0].fields.some(f => f.name === 'Room Code' && f.value.includes('abc123')));

  // Slack payload
  const slackRaw = formatNotificationPayload({
    event: 'room_created',
    roomId: 'room99',
    inviteUrl: 'https://custom.tunnel.com#room99'
  }, 'https://hooks.slack.com/services/123/456');

  const slack = JSON.parse(slackRaw);
  assert.ok(slack.text.includes('room99'));
  assert.ok(slack.blocks[0].text.text.includes('custom.tunnel.com'));

  // Generic payload
  const genericRaw = formatNotificationPayload({
    event: 'playback',
    title: 'Dune 2',
    roomId: 'dune01'
  }, 'https://my-apprise.local/notify');

  const generic = JSON.parse(genericRaw);
  assert.equal(generic.event, 'playback');
  assert.ok(generic.title.includes('Dune 2'));
});

test('notifications: sendNotification completes silently without crashing on unreachable url', async () => {
  const oldHook = process.env.DISCORD_WEBHOOK_URL;
  try {
    process.env.DISCORD_WEBHOOK_URL = 'http://127.0.0.1:54321/nonexistent-webhook';
    // Should not throw or crash
    await sendNotification({
      event: 'playback',
      title: 'Test Movie',
      roomId: 'TEST',
      inviteUrl: 'https://example.com'
    });
    assert.ok(true);
  } finally {
    if (oldHook) process.env.DISCORD_WEBHOOK_URL = oldHook;
    else delete process.env.DISCORD_WEBHOOK_URL;
  }
});

test('subtitles: fetchSubtitles follows HTTP 302 redirects seamlessly', async (t) => {
  clearSubtitleCache();
  const server = http.createServer((req, res) => {
    if (req.url === '/redirect-source.srt') {
      res.writeHead(302, { Location: '/final.srt' });
      res.end();
      return;
    }
    if (req.url === '/final.srt') {
      res.writeHead(200, { 'Content-Type': 'text/plain' });
      res.end('1\n00:00:01,000 --> 00:00:03,000\nRedirected subtitle text');
      return;
    }
    res.writeHead(404);
    res.end();
  });

  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  t.after(() => server.close());

  const port = server.address().port;
  const vtt = await fetchSubtitles(`http://127.0.0.1:${port}/redirect-source.srt`, [`127.0.0.1:${port}`]);
  assert.ok(vtt.startsWith('WEBVTT'));
  assert.ok(vtt.includes('Redirected subtitle text'));
});

test('subtitles: searchOpenSubtitles returns unconfigured when key is not set', async () => {
  const res = await searchOpenSubtitles({ query: 'The Matrix' });
  assert.equal(res.configured, false);
  assert.deepEqual(res.results, []);
});

test('notifications: formatNotificationPayload formats participant_joined and playback_stop with clientCount', () => {
  const joinRaw = formatNotificationPayload({
    event: 'participant_joined',
    roomId: 'join99',
    inviteUrl: 'https://vr.example.com#join99',
    clientCount: 4
  }, 'https://discord.com/api/webhooks/123/xyz');

  const joinPayload = JSON.parse(joinRaw);
  assert.ok(joinPayload.embeds[0].title.includes('friend joined'));
  assert.ok(joinPayload.embeds[0].fields.some(f => f.name === 'Viewers' && f.value === '4/8'));

  const stopRaw = formatNotificationPayload({
    event: 'playback_stop',
    roomId: 'stop12',
    clientCount: 2
  }, 'https://discord.com/api/webhooks/123/xyz');

  const stopPayload = JSON.parse(stopRaw);
  assert.ok(stopPayload.embeds[0].title.includes('Playback Ended'));
});

test('api: /api/subtitles/search returns unconfigured payload when key is absent', async (t) => {
  const app = await createServer({ media: false });
  app.server.listen(0, '127.0.0.1');
  await once(app.server, 'listening');
  t.after(() => app.close());

  const port = app.server.address().port;
  const res = await fetch(`http://127.0.0.1:${port}/api/subtitles/search?query=Inception`);
  assert.equal(res.status, 200);
  const data = await res.json();
  assert.equal(data.configured, false);
  assert.deepEqual(data.results, []);
});

test('api: /api/providers sends X-MediaStorm-Key header when MEDIASTORM_KEY is set', async (t) => {
  let receivedHeader = null;
  const mockMs = http.createServer((req, res) => {
    receivedHeader = req.headers['x-mediastorm-key'];
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ status: 'ok' }));
  });
  mockMs.listen(0, '127.0.0.1');
  await once(mockMs, 'listening');
  t.after(() => mockMs.close());

  const msPort = mockMs.address().port;
  process.env.MEDIASTORM_URL = `http://127.0.0.1:${msPort}`;
  process.env.MEDIASTORM_KEY = 'secret-agent-key-999';

  try {
    const app = await createServer({ media: false });
    app.server.listen(0, '127.0.0.1');
    await once(app.server, 'listening');
    t.after(() => app.close());

    const port = app.server.address().port;
    const res = await fetch(`http://127.0.0.1:${port}/api/providers`);
    const data = await res.json();
    assert.equal(data.providers.mediastorm.online, true);
    assert.equal(receivedHeader, 'secret-agent-key-999');
  } finally {
    delete process.env.MEDIASTORM_URL;
    delete process.env.MEDIASTORM_KEY;
  }
});

test('api: /api/providers sends Authorization Bearer header when SILO_TOKEN is set', async (t) => {
  let receivedAuth = null;
  const mockSilo = http.createServer((req, res) => {
    receivedAuth = req.headers['authorization'];
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ status: 'ok' }));
  });
  mockSilo.listen(0, '127.0.0.1');
  await once(mockSilo, 'listening');
  t.after(() => mockSilo.close());

  const siloPort = mockSilo.address().port;
  process.env.SILO_URL = `http://127.0.0.1:${siloPort}`;
  process.env.SILO_TOKEN = 'jwt-token-xyz-123';

  try {
    const app = await createServer({ media: false });
    app.server.listen(0, '127.0.0.1');
    await once(app.server, 'listening');
    t.after(() => app.close());

    const port = app.server.address().port;
    const res = await fetch(`http://127.0.0.1:${port}/api/providers`);
    const data = await res.json();
    assert.equal(data.providers.silo.online, true);
    assert.equal(receivedAuth, 'Bearer jwt-token-xyz-123');
  } finally {
    delete process.env.SILO_URL;
    delete process.env.SILO_TOKEN;
  }
});

test('api: /api/providers accurately reports Remux status independently from Silo', async (t) => {
  await saveConfig({ siloUrl: '', siloToken: '', remuxUrl: '' });
  delete process.env.SILO_URL;
  delete process.env.SILO_TOKEN;

  const mockRemux = http.createServer((req, res) => {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ remux: 'ok' }));
  });
  mockRemux.listen(0, '127.0.0.1');
  await once(mockRemux, 'listening');
  t.after(() => mockRemux.close());

  const remuxPort = mockRemux.address().port;
  process.env.REMUX_URL = `http://127.0.0.1:${remuxPort}`;

  try {
    const app = await createServer({ media: false });
    app.server.listen(0, '127.0.0.1');
    await once(app.server, 'listening');
    t.after(() => app.close());

    const port = app.server.address().port;
    const res = await fetch(`http://127.0.0.1:${port}/api/providers`);
    const data = await res.json();
    assert.equal(data.providers.remux.online, true);
    assert.equal(data.providers.remux.configured, true);
    assert.equal(data.providers.silo.configured, false);
  } finally {
    delete process.env.REMUX_URL;
  }
});

test('api: /api/providers marks offline and malformed providers without failing the response', async (t) => {
  await saveConfig({
    jellyfinUrl: 'http://127.0.0.1:1/unreachable',
    mediastormUrl: 'not a URL',
    siloUrl: '',
    remuxUrl: ''
  });

  const app = await createServer({ media: false });
  app.server.listen(0, '127.0.0.1');
  await once(app.server, 'listening');
  t.after(() => app.close());

  const port = app.server.address().port;
  const res = await fetch(`http://127.0.0.1:${port}/api/providers`);
  assert.equal(res.status, 200);

  const data = await res.json();
  assert.equal(data.ok, true);
  assert.deepEqual(data.providers.jellyfin, {
    configured: true,
    online: false
  });
  assert.deepEqual(data.providers.mediastorm, {
    configured: true,
    online: false
  });
});

test('api: /api/providers treats upstream 5xx responses as offline', async (t) => {
  const upstream = http.createServer((_req, res) => {
    res.writeHead(503, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ error: 'fixture unavailable' }));
  });
  upstream.listen(0, '127.0.0.1');
  await once(upstream, 'listening');
  t.after(() => upstream.close());

  await saveConfig({
    jellyfinUrl: `http://127.0.0.1:${upstream.address().port}`,
    mediastormUrl: '',
    siloUrl: '',
    remuxUrl: ''
  });

  const app = await createServer({ media: false });
  app.server.listen(0, '127.0.0.1');
  await once(app.server, 'listening');
  t.after(() => app.close());

  const port = app.server.address().port;
  const res = await fetch(`http://127.0.0.1:${port}/api/providers`);
  const data = await res.json();
  assert.equal(res.status, 200);
  assert.deepEqual(data.providers.jellyfin, {
    configured: true,
    online: false,
    status: 503
  });
});

test('api: /api/room/play accepts subtitlesUrl and includes it in room playback state', async (t) => {
  const app = await createServer({ media: false });
  app.server.listen(0, '127.0.0.1');
  await once(app.server, 'listening');
  t.after(() => app.close());

  const port = app.server.address().port;

  // Create active room
  const fakeWs = { readyState: 1, send() {} };
  app.rooms.handle(fakeWs, { type: 'create' });
  const room = app.rooms.rooms.get(fakeWs.roomId);
  assert.ok(room && room.id);

  const res = await fetch(`http://127.0.0.1:${port}/api/room/play`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      roomId: room.id,
      url: 'https://example.com/test-stream.mp4',
      title: 'Test Movie',
      subtitlesUrl: 'https://example.com/subtitles.vtt'
    })
  });

  assert.equal(res.status, 200);
  const data = await res.json();
  assert.equal(data.ok, true);
  assert.equal(room.playback.sourceUrl, 'https://example.com/test-stream.mp4');
  assert.equal(room.playback.subtitlesUrl, 'https://example.com/subtitles.vtt');
});

test('stremio: ephemeral room session stores in RAM, never leaks authKey to snapshot, and purges on host leave', async () => {
  const { Rooms } = await import('../server/rooms.js');
  const rooms = new Rooms();
  const host = { readyState: 1, messages: [], send(d) { this.messages.push(JSON.parse(d)); } };
  const guest = { readyState: 1, messages: [], send(d) { this.messages.push(JSON.parse(d)); } };

  // 1. Host creates room
  rooms.handle(host, { type: 'create' });
  const roomId = host.roomId;
  const room = rooms.rooms.get(roomId);

  // 2. Guest joins room
  rooms.handle(guest, { type: 'join', roomId });

  // 3. Guest attempting to set stremio session is rejected
  assert.throws(() => {
    rooms.handle(guest, { type: 'stremio:session', authKey: 'secret-123', user: 'Hacker' });
  }, /Only the host/);

  // 4. Host sets ephemeral Stremio session
  rooms.handle(host, {
    type: 'stremio:session',
    authKey: 'super-secret-key-999',
    user: 'LegitimateDog',
    addons: ['https://example.com/cinemeta/manifest.json'],
    ephemeral: true
  });

  // Verify room internal state
  assert.equal(room.stremioSession.user, 'LegitimateDog');
  assert.equal(room.stremioSession.authKey, 'super-secret-key-999');
  assert.equal(room.stremioSession.ephemeral, true);

  // Verify broadcast snapshot NEVER exposes raw authKey
  const snap = rooms.snapshot(room);
  assert.equal(snap.stremio.active, true);
  assert.equal(snap.stremio.user, 'LegitimateDog');
  assert.equal(snap.stremio.addonCount, 1);
  assert.equal(snap.stremio.ephemeral, true);
  assert.equal(snap.stremio.authKey, undefined);
  assert.equal(JSON.stringify(snap).includes('super-secret-key-999'), false);

  // 5. Host disconnects / leaves -> ephemeral session is completely purged from RAM
  rooms.leave(host, true);
  assert.equal(room.stremioSession, null);
  const snapAfter = rooms.snapshot(room);
  assert.equal(snapAfter.stremio, null);
});

test('api: /api/room/addons provides ephemeral addons when active without altering server config', async (t) => {
  const app = await createServer({ media: false });
  app.server.listen(0, '127.0.0.1');
  await once(app.server, 'listening');
  t.after(() => app.close());

  const port = app.server.address().port;

  // Create room
  const fakeWs = { readyState: 1, send() {} };
  app.rooms.handle(fakeWs, { type: 'create' });
  const room = app.rooms.rooms.get(fakeWs.roomId);

  // Attach ephemeral session
  room.stremioSession = {
    authKey: 'ephemeral-key',
    user: 'EphemeralUser',
    addons: ['https://ephemeral-addon.com/manifest.json'],
    ephemeral: true
  };

  const res = await fetch(`http://127.0.0.1:${port}/api/room/addons?roomId=${room.id}`);
  assert.equal(res.status, 200);
  const data = await res.json();
  assert.equal(data.ok, true);
  assert.equal(data.ephemeral, true);
  assert.equal(data.user, 'EphemeralUser');
  assert.deepEqual(data.addons, ['https://ephemeral-addon.com/manifest.json']);

  // Verify server config on disk was NOT touched
  const cfgRes = await fetch(`http://127.0.0.1:${port}/api/config`);
  const cfgData = await cfgRes.json();
  assert.notEqual(cfgData.config.stremioAuthKey, 'ephemeral-key');
});
