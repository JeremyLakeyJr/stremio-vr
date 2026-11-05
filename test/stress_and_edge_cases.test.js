import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import WebSocket from 'ws';
import { createServer } from '../server/index.js';
import { srtToVtt } from '../server/subtitles.js';
import { destination } from '../server/media.js';
import { sanitizeUpstreamHeaders, formatUpstreamHeaders, buildTranscodeArgs } from '../server/hwaccel.js';

async function setupServer(t) {
  const app = await createServer({ media: false });
  app.server.listen(0, '127.0.0.1');
  await once(app.server, 'listening');
  t.after(() => app.close());
  const port = app.server.address().port;
  return { app, port };
}

function connectWs(port) {
  return new Promise((resolve, reject) => {
    const ws = new WebSocket(`ws://127.0.0.1:${port}/ws`);
    ws.on('open', () => resolve(ws));
    ws.on('error', reject);
  });
}

function waitForMessage(ws, predicate = () => true, timeoutMs = 4000) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      ws.off('message', handler);
      reject(new Error('Timed out waiting for message'));
    }, timeoutMs);

    function handler(raw) {
      try {
        const msg = JSON.parse(raw.toString());
        if (predicate(msg)) {
          clearTimeout(timer);
          ws.off('message', handler);
          resolve(msg);
        }
      } catch {}
    }
    ws.on('message', handler);
  });
}

test('stress: concurrent clients fill room to capacity (8 players), 9th is rejected, pose updates stream smoothly', async (t) => {
  const { app, port } = await setupServer(t);

  const hostWs = await connectWs(port);
  t.after(() => hostWs.terminate());

  const hostJoinedPromise = waitForMessage(hostWs, m => m.type === 'joined');
  hostWs.send(JSON.stringify({ type: 'create', profile: { name: 'Host' } }));
  const hostJoined = await hostJoinedPromise;
  const roomId = hostJoined.roomId;

  // Connect 7 guest clients concurrently (filling room to 8 max participants)
  const guests = await Promise.all(Array.from({ length: 7 }).map(async (_, i) => {
    const ws = await connectWs(port);
    t.after(() => ws.terminate());
    const statePromise = waitForMessage(ws, m => m.type === 'state');
    ws.send(JSON.stringify({ type: 'join', roomId, profile: { name: `Guest_${i}` } }));
    await statePromise;
    return ws;
  }));

  // Verify room is at full capacity of 8
  const room = app.rooms.rooms.get(roomId);
  assert.ok(room);
  assert.equal(room.players.size, 8);

  // 9th player joining should be rejected with room full error
  const ninthWs = await connectWs(port);
  t.after(() => ninthWs.terminate());
  const errPromise = waitForMessage(ninthWs, m => m.type === 'error');
  ninthWs.send(JSON.stringify({ type: 'join', roomId, profile: { name: 'Guest_Overflow' } }));
  const err = await errPromise;
  assert.match(err.message, /Room is full/);

  // Concurrently stream pose updates from all 8 participants
  for (let round = 0; round < 5; round++) {
    for (const ws of [hostWs, ...guests]) {
      ws.send(JSON.stringify({
        type: 'pose',
        pose: {
          head: { position: [round * 0.1, 1.7, 0], rotation: [0, 0, 0] }
        }
      }));
    }
  }

  // Disconnect all guests by sending leave, then closing
  for (const ws of guests) {
    ws.send(JSON.stringify({ type: 'leave' }));
    ws.close();
  }

  // Wait for state update to process
  await new Promise(r => setTimeout(r, 50));
  assert.equal(app.rooms.rooms.get(roomId)?.players.size, 1);
});

test('stress: rapid play/pause/seek cycling does not corrupt playback state', async (t) => {
  const { app, port } = await setupServer(t);

  const ws = await connectWs(port);
  t.after(() => ws.terminate());

  const joinedPromise = waitForMessage(ws, m => m.type === 'joined');
  ws.send(JSON.stringify({ type: 'create' }));
  const { roomId } = await joinedPromise;

  // Send initial playback
  ws.send(JSON.stringify({
    type: 'playback',
    url: 'https://example.com/video.mp4',
    title: 'Stress Film',
    paused: false,
    position: 0
  }));

  // Rapidly toggle paused and seek
  for (let i = 0; i < 20; i++) {
    ws.send(JSON.stringify({
      type: 'playback',
      paused: i % 2 === 0,
      position: i * 5,
      seek: true
    }));
  }

  // Wait for state to settle
  await new Promise(r => setTimeout(r, 100));
  const room = app.rooms.rooms.get(roomId);
  assert.ok(room);
  assert.equal(room.playback.title, 'Stress Film');
  assert.equal(typeof room.playback.paused, 'boolean');
  assert.ok(room.playback.position >= 0);
});

test('fuzz: malformed and extreme JSON messages do not crash server', async (t) => {
  const { port } = await setupServer(t);
  const ws = await connectWs(port);
  t.after(() => ws.terminate());

  const payloads = [
    '',
    'not a json',
    '{"type": null}',
    '{"type": 12345}',
    '{"type": "playback", "position": -9999}',
    '{"type": "playback", "position": "one hundred"}',
    '{"type": "playback", "paused": "not_a_boolean"}',
    '{"type": "seat", "seatIndex": "bad"}',
    '{"type": "game", "game": "chess", "action": "invalid_action"}',
    '{"type": "signal", "target": null}',
    JSON.stringify({ type: 'profile', profile: { name: 'A'.repeat(5000) } })
  ];

  for (const p of payloads) {
    ws.send(p);
  }

  // Send valid ping to confirm socket and server are alive
  const pongPromise = waitForMessage(ws, m => m.type === 'pong');
  ws.send(JSON.stringify({ type: 'ping', sentAt: Date.now() }));
  const pong = await pongPromise;
  assert.ok(pong.serverTime > 0);
});

test('subtitles: fuzzing weird, empty, and malformed subtitle blocks', () => {
  const fuzzed = [
    null,
    undefined,
    '',
    '   \n\n   ',
    'Just plain text with no timecodes',
    '00:00:01,000 --> 00:00:05,000\nMissing cue number',
    '999\n00:00,000 --> 00:05,000\nShort timecode',
    '1\n99:99:99,999 --> 99:99:99,999\nExtreme timecode',
    'WEBVTT\n\nAlready WebVTT header'
  ];

  for (const input of fuzzed) {
    const out = srtToVtt(input);
    assert.equal(typeof out, 'string');
    assert.ok(out.startsWith('WEBVTT'));
  }
});

test('security: SSRF destination checks block evasive loopback and private IP representations', async () => {
  const blockedIps = [
    'http://0.0.0.0:3000',
    'http://127.0.0.1:8080',
    'http://127.1.1.1:9000',
    'http://10.255.255.255/stream',
    'http://172.16.0.1/video.mp4',
    'http://172.31.255.255/video.mp4',
    'http://192.168.0.1:8096/stream',
    'http://169.254.169.254/latest/meta-data',
    'http://[::1]:8080/media',
    'http://[::ffff:127.0.0.1]:3000/media',
    'http://[fe80::1]/media'
  ];

  // When ALLOW_PRIVATE_NETWORKS is false (default)
  delete process.env.ALLOW_PRIVATE_NETWORKS;

  for (const url of blockedIps) {
    await assert.rejects(
      destination(url),
      /Private media source blocked/
    );
  }
});

test('hwaccel: header injection via tabs, spaces, and duplicate headers is handled cleanly', () => {
  const dirty = {
    'X-Emby-Token': '   my-token-abc   ',
    'x-emby-token': 'lowercase-override',
    'X-Mediastorm-Key': 'key\r\nInjected-Header: malicious\r\n',
    'Cookie': 'uid=1; secret=xyz',
    'Unrecognized-Header': 'discarded'
  };

  const sanitized = sanitizeUpstreamHeaders(dirty);
  assert.equal(sanitized['x-emby-token'], 'lowercase-override');
  assert.equal(sanitized['x-mediastorm-key'], 'keyInjected-Header: malicious');
  assert.equal(sanitized['cookie'], 'uid=1; secret=xyz');
  assert.equal(sanitized['unrecognized-header'], undefined);

  const formatted = formatUpstreamHeaders(dirty);
  assert.equal(formatted[0], '-headers');
  // Must not have CRLF followed by Injected-Header (no new header line created)
  assert.ok(!formatted[1].includes('\r\nInjected-Header:'));
});
