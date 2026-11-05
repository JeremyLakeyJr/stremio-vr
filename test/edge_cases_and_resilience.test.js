import { test } from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import WebSocket from 'ws';
import { createServer } from '../server/index.js';
import { srtToVtt, adjustVttOffset } from '../server/subtitles.js';
import { sanitizeUpstreamHeaders, formatUpstreamHeaders, BITRATE_LADDER } from '../server/hwaccel.js';

test('edge-cases: subtitle timecode parsing handles irregular formatting and single-digit hours', () => {
  const irregularSrt = `1
0:01:05,200 --> 0:01:08,750
Single digit hour cue

2
00:02:10,005 --> 00:02:14,000
<b>HTML Tagged Line</b>
Second line with trailing whitespace   

3
00:00:00,100 --> 00:00:00,800
Ultra short cue
`;
  const vtt = srtToVtt(irregularSrt);
  assert.ok(vtt.startsWith('WEBVTT\n\n'));
  assert.ok(vtt.includes('00:01:05.200 --> 00:01:08.750'), 'Single digit hour normalized to 00:01:05.200');
  assert.ok(vtt.includes('00:02:10.005 --> 00:02:14.000'));
  assert.ok(vtt.includes('HTML Tagged Line'));
  assert.ok(vtt.includes('Ultra short cue'));
});

test('edge-cases: subtitle offset clamps properly on large negative offsets', () => {
  const vtt = `WEBVTT

00:00:02.500 --> 00:00:05.000
Early cue

00:01:00.000 --> 00:01:05.000
Later cue
`;
  const adjusted = adjustVttOffset(vtt, -10);
  assert.ok(adjusted.includes('00:00:00.000 --> 00:00:00.000'));
  assert.ok(adjusted.includes('00:00:50.000 --> 00:00:55.000'));
});

test('edge-cases: subtitle offset handles extreme positive offset without integer overflow', () => {
  const vtt = `WEBVTT

00:00:10.000 --> 00:00:15.000
Test cue
`;
  const adjusted = adjustVttOffset(vtt, 7200); // +2 hours
  assert.ok(adjusted.includes('02:00:10.000 --> 02:00:15.000'));
});

test('edge-cases: upstream headers strictly filter auth keys and block CRLF attacks', () => {
  const headers = {
    'X-Emby-Token': 'valid_token_123',
    'Authorization': 'Bearer secret_key',
    'X-MediaStorm-Key': 'storm_abc',
    'Cookie': 'session=token_abc',
    'Injected-Evil\r\nHeader': 'evil',
    'Evil-Header': 'should_be_dropped',
    'X-Custom-Malicious': 'dropped'
  };
  const sanitized = sanitizeUpstreamHeaders(headers);
  assert.equal(sanitized['x-emby-token'], 'valid_token_123');
  assert.equal(sanitized['authorization'], 'Bearer secret_key');
  assert.equal(sanitized['x-mediastorm-key'], 'storm_abc');
  assert.equal(sanitized['cookie'], 'session=token_abc');
  assert.equal(sanitized['evil-header'], undefined);
  assert.equal(sanitized['injected-evil\r\nheader'], undefined);
  assert.equal(sanitized['x-custom-malicious'], undefined);

  const formatted = formatUpstreamHeaders(sanitized);
  assert.equal(formatted[0], '-headers');
  assert.ok(formatted[1].includes('x-emby-token: valid_token_123\r\n'));
  assert.ok(formatted[1].includes('authorization: Bearer secret_key\r\n'));
  assert.ok(formatted[1].includes('x-mediastorm-key: storm_abc\r\n'));
  assert.ok(formatted[1].includes('cookie: session=token_abc\r\n'));
  assert.ok(!formatted[1].includes('evil'));
  assert.ok(!formatted[1].includes('malicious'));
});

test('edge-cases: rapid playback seek and pause state updates retain playback timeline', async () => {
  const app = await createServer({ media: false });
  app.server.listen(0, '127.0.0.1');
  await once(app.server, 'listening');
  const port = app.server.address().port;

  const ws = new WebSocket(`ws://127.0.0.1:${port}/ws`);
  await once(ws, 'open');

  const messages = [];
  ws.on('message', data => {
    try { messages.push(JSON.parse(data.toString())); } catch {}
  });

  ws.send(JSON.stringify({
    type: 'create',
    profile: { name: 'HostTester', avatar: 'R6' }
  }));

  await new Promise(r => setTimeout(r, 60));

  ws.send(JSON.stringify({
    type: 'playback',
    url: 'http://example.com/movie.mp4',
    title: 'The Great VR Film',
    position: 120,
    paused: false
  }));

  await new Promise(r => setTimeout(r, 60));

  for (let i = 1; i <= 5; i++) {
    ws.send(JSON.stringify({
      type: 'playback',
      url: 'http://example.com/movie.mp4',
      title: 'The Great VR Film',
      position: 120 + i * 10,
      paused: i % 2 === 0
    }));
  }

  await new Promise(r => setTimeout(r, 80));

  const lastState = messages.filter(m => m.type === 'state').pop();
  assert.ok(lastState);
  assert.equal(lastState.playback.url, 'http://example.com/movie.mp4');
  assert.equal(lastState.playback.title, 'The Great VR Film');
  assert.ok(lastState.playback.position >= 160);

  ws.close();
  await app.close();
});

test('edge-cases: multiple layout monitors can be placed, curved, and removed without state corruption', async () => {
  const app = await createServer({ media: false });
  app.server.listen(0, '127.0.0.1');
  await once(app.server, 'listening');
  const port = app.server.address().port;

  const ws = new WebSocket(`ws://127.0.0.1:${port}/ws`);
  await once(ws, 'open');

  let latestState = null;
  ws.on('message', data => {
    try {
      const msg = JSON.parse(data.toString());
      if (msg.type === 'state') latestState = msg;
    } catch {}
  });

  ws.send(JSON.stringify({
    type: 'create',
    profile: { name: 'MonitorPlacer', avatar: 'R6' }
  }));

  await new Promise(r => setTimeout(r, 60));

  // Place 2 extra monitors
  for (let i = 1; i <= 2; i++) {
    const rev = latestState?.layout?.revision ?? 0;
    ws.send(JSON.stringify({
      type: 'layout',
      action: 'place',
      revision: rev,
      kind: 'screen',
      position: [i * 1.5, 1.5, -2.0],
      rotation: [0, i * 15, 0],
      width: 2.1,
      curve: i * 20
    }));
    await new Promise(r => setTimeout(r, 50));
  }

  assert.ok(latestState.layout);
  assert.equal(latestState.layout.items.length, 2);

  // Remove first monitor
  const firstId = latestState.layout.items[0].id;
  const rev = latestState.layout.revision;
  ws.send(JSON.stringify({
    type: 'layout',
    action: 'remove',
    revision: rev,
    id: firstId
  }));

  await new Promise(r => setTimeout(r, 60));

  assert.equal(latestState.layout.items.length, 1);
  assert.ok(!latestState.layout.items.some(it => it.id === firstId));

  ws.close();
  await app.close();
});

test('edge-cases: hardware ladder profiles provide proper scale down at lower bandwidths', () => {
  const profiles = Object.keys(BITRATE_LADDER);
  assert.ok(profiles.length >= 5);
  const highest = BITRATE_LADDER['4k'];
  const lowest = BITRATE_LADDER['480p'];

  assert.ok(highest.height > lowest.height);
  assert.equal(highest.height, 2160);
  assert.equal(lowest.height, 480);
  assert.equal(highest.fps, 60);
  assert.equal(lowest.fps, 30);
});
