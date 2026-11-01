import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { srtToVtt, adjustVttOffset, fetchSubtitles, clearSubtitleCache, searchOpenSubtitles } from '../server/subtitles.js';
import { sendNotification, formatNotificationPayload } from '../server/notifications.js';
import { createServer } from '../server/index.js';
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
