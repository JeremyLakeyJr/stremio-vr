import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import WebSocket from 'ws';
import http from 'node:http';
import { createServer } from '../server/index.js';
import { srtToVtt, adjustVttOffset, fetchSubtitles, clearSubtitleCache } from '../server/subtitles.js';
import { destination } from '../server/media.js';
import { sanitizeUpstreamHeaders, formatUpstreamHeaders } from '../server/hwaccel.js';
import { sendNotification, formatNotificationPayload } from '../server/notifications.js';

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

test('bug-test: multiple concurrent rooms remain fully isolated', async (t) => {
  const { app, port } = await setupServer(t);

  // Create 3 independent rooms
  const roomsData = [];
  for (let i = 0; i < 3; i++) {
    const ws = await connectWs(port);
    t.after(() => ws.terminate());
    const joinedPromise = waitForMessage(ws, m => m.type === 'joined');
    ws.send(JSON.stringify({ type: 'create', profile: { name: `Host_Room_${i}` } }));
    const joined = await joinedPromise;
    roomsData.push({ ws, roomId: joined.roomId, hostId: joined.playerId });
  }

  // Ensure all 3 room IDs are distinct
  const roomIds = roomsData.map(r => r.roomId);
  assert.equal(new Set(roomIds).size, 3);

  // Set distinct playback in each room
  for (let i = 0; i < 3; i++) {
    const { ws, roomId } = roomsData[i];
    ws.send(JSON.stringify({
      type: 'playback',
      url: `https://example.com/movie_${i}.mp4`,
      title: `Movie Title ${i}`,
      position: i * 100,
      paused: false
    }));
  }

  await new Promise(r => setTimeout(r, 100));

  // Verify that playback state in each room is independent
  for (let i = 0; i < 3; i++) {
    const room = app.rooms.rooms.get(roomsData[i].roomId);
    assert.ok(room);
    assert.equal(room.playback.title, `Movie Title ${i}`);
    assert.equal(room.playback.url, `https://example.com/movie_${i}.mp4`);
  }
});

test('bug-test: guest attempting host actions is rejected without affecting state', async (t) => {
  const { app, port } = await setupServer(t);

  const hostWs = await connectWs(port);
  t.after(() => hostWs.terminate());
  const hostJoinedPromise = waitForMessage(hostWs, m => m.type === 'joined');
  hostWs.send(JSON.stringify({ type: 'create', profile: { name: 'Host' } }));
  const { roomId } = await hostJoinedPromise;

  const guestWs = await connectWs(port);
  t.after(() => guestWs.terminate());
  const guestStatePromise = waitForMessage(guestWs, m => m.type === 'state');
  guestWs.send(JSON.stringify({ type: 'join', roomId, profile: { name: 'Guest' } }));
  await guestStatePromise;

  // Guest tries to trigger playback
  const errPromise1 = waitForMessage(guestWs, m => m.type === 'error');
  guestWs.send(JSON.stringify({ type: 'playback', url: 'https://malicious.com/ad.mp4' }));
  const err1 = await errPromise1;
  assert.match(err1.message, /Only the host/);

  // Guest tries to change world
  const errPromise2 = waitForMessage(guestWs, m => m.type === 'error');
  guestWs.send(JSON.stringify({ type: 'world', world: 'home' }));
  const err2 = await errPromise2;
  assert.match(err2.message, /Only the host/);

  // Guest tries to arrange layout
  const errPromise3 = waitForMessage(guestWs, m => m.type === 'error');
  guestWs.send(JSON.stringify({ type: 'layout', action: 'reset' }));
  const err3 = await errPromise3;
  assert.match(err3.message, /Only the host/);
});

test('bug-test: session resumption with token closes old connection with code 4000', async (t) => {
  const { port } = await setupServer(t);

  const ws1 = await connectWs(port);
  const joinedPromise = waitForMessage(ws1, m => m.type === 'joined');
  ws1.send(JSON.stringify({ type: 'create', profile: { name: 'User1' } }));
  const { roomId, token } = await joinedPromise;

  const closePromise = new Promise((resolve) => {
    ws1.on('close', (code, reason) => resolve({ code, reason: reason.toString() }));
  });

  // Connect second socket with identical token
  const ws2 = await connectWs(port);
  t.after(() => ws2.terminate());
  ws2.send(JSON.stringify({ type: 'join', roomId, token }));

  const closed = await closePromise;
  assert.equal(closed.code, 4000);
  assert.match(closed.reason, /Session resumed elsewhere/);
});

test('bug-test: host explicit leave instantly re-elects next available connected player', async (t) => {
  const { app, port } = await setupServer(t);

  const hostWs = await connectWs(port);
  const hostJoinedPromise = waitForMessage(hostWs, m => m.type === 'joined');
  hostWs.send(JSON.stringify({ type: 'create', profile: { name: 'FirstHost' } }));
  const { roomId, playerId: hostId } = await hostJoinedPromise;

  const guestWs = await connectWs(port);
  t.after(() => guestWs.terminate());
  const guestJoinedPromise = waitForMessage(guestWs, m => m.type === 'joined');
  guestWs.send(JSON.stringify({ type: 'join', roomId, profile: { name: 'SecondGuest' } }));
  const { playerId: guestId } = await guestJoinedPromise;

  // Host explicitly leaves
  hostWs.send(JSON.stringify({ type: 'leave' }));
  await new Promise(r => setTimeout(r, 60));

  const room = app.rooms.rooms.get(roomId);
  assert.ok(room);
  assert.equal(room.host, guestId);
  assert.notEqual(room.host, hostId);
});

test('bug-test: header injection fuzzing handles weird delimiters, null bytes and bad formatting', () => {
  const edgeCases = {
    '': 'empty-key',
    'X-Emby-Token': 'valid-token\x00with-null-byte',
    'X-Mediastorm-Key': 'tab\tcharacter\tinside',
    'Authorization': 'Bearer \r\nInjected: evil\r\n',
    'Cookie': 'session="bad\nnewline"',
    'Content-Length': '12345', // Forbidden header
    'Transfer-Encoding': 'chunked', // Forbidden header
    'Host': 'attacker.com' // Forbidden header
  };

  const sanitized = sanitizeUpstreamHeaders(edgeCases);

  // Forbidden headers must not exist in sanitized result
  assert.equal(sanitized['content-length'], undefined);
  assert.equal(sanitized['transfer-encoding'], undefined);
  assert.equal(sanitized['host'], undefined);

  // Newlines and null bytes must be stripped
  assert.ok(!sanitized['authorization'].includes('\r'));
  assert.ok(!sanitized['authorization'].includes('\n'));
  assert.ok(!sanitized['x-emby-token'].includes('\x00'));

  const formatted = formatUpstreamHeaders(sanitized);
  for (const line of formatted) {
    assert.ok(!line.includes('\r\nInjected:'));
  }
});

test('bug-test: sub-millisecond and extreme subtitle cue time shifting', () => {
  const srt = `1
00:00:00,050 --> 00:00:01,000
Quick cue

2
01:30:00,000 --> 01:30:05,500
Late cue`;

  const vtt = srtToVtt(srt);
  assert.ok(vtt.includes('00:00:00.050 --> 00:00:01.000'));

  // Test negative shift larger than cue 1 start
  const shifted = adjustVttOffset(vtt, -0.5);
  // 50ms - 500ms = negative -> clamped to 00:00:00.000
  assert.ok(shifted.includes('00:00:00.000 --> 00:00:00.500'));
  // 1h30m - 0.5s = 01:29:59.500
  assert.ok(shifted.includes('01:29:59.500 --> 01:30:05.000'));
});

test('reaction: room broadcasts emoji reactions to all peers in the room', async (t) => {
  const { port } = await setupServer(t);
  const host = await connectWs(port);
  t.after(() => host.close());

  host.send(JSON.stringify({ type: 'create' }));
  const hostJoined = await waitForMessage(host, m => m.type === 'joined');
  const roomId = hostJoined.roomId;

  const guest = await connectWs(port);
  t.after(() => guest.close());
  guest.send(JSON.stringify({ type: 'join', roomId }));
  await waitForMessage(guest, m => m.type === 'joined');

  // Guest sends popcorn reaction
  guest.send(JSON.stringify({ type: 'reaction', emoji: '🍿' }));

  const hostRx = await waitForMessage(host, m => m.type === 'reaction');
  assert.equal(hostRx.emoji, '🍿');
  assert.ok(hostRx.playerId);
});
