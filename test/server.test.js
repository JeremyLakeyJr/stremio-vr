import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import WebSocket from 'ws';
import { createServer } from '../server/index.js';

async function fixture(t) {
  const app = await createServer({ media: false }); app.server.listen(0, '127.0.0.1'); await once(app.server, 'listening'); t.after(() => app.close());
  return { ...app, port: app.server.address().port };
}
async function socket(port) { const ws = new WebSocket(`ws://127.0.0.1:${port}/ws`); await once(ws, 'open'); return ws; }
function receive(ws, type, predicate = () => true) { return new Promise((resolve, reject) => {
  const timer = setTimeout(() => { ws.off('message', listener); reject(new Error('Timed out: ' + type)); }, 2000);
  function listener(raw) { const message = JSON.parse(raw); if (message.type === type && predicate(message)) { clearTimeout(timer); ws.off('message', listener); resolve(message); } }
  ws.on('message', listener);
}); }

test('HTTP health and real WebSocket create/join/playback round trip', async t => {
  const { port } = await fixture(t);
  const health = await fetch(`http://127.0.0.1:${port}/health`); assert.deepEqual(await health.json(), { ok: true, rooms: 0 });
  const host = await socket(port); const joined = receive(host, 'joined'); host.send(JSON.stringify({ type: 'create' })); const credentials = await joined;
  const guest = await socket(port); const ready = receive(guest, 'state'); guest.send(JSON.stringify({ type: 'join', roomId: credentials.roomId })); const state = await ready; assert.equal(state.players.length, 2);
  const playback = receive(guest, 'state'); host.send(JSON.stringify({ type: 'playback', url: 'https://example.com/a.mp4', paused: false })); assert.equal((await playback).playback.paused, false);
  const error = receive(guest, 'error'); guest.send(JSON.stringify({ type: 'playback', paused: true })); assert.match((await error).message, /host/);
});
test('invalid JSON produces a recoverable error and ping still works', async t => {
  const { port } = await fixture(t); const ws = await socket(port); const error = receive(ws, 'error'); ws.send('{'); assert.equal((await error).type, 'error');
  const pong = receive(ws, 'pong'); ws.send(JSON.stringify({ type: 'ping', sentAt: 10 })); assert.equal((await pong).sentAt, 10);
});
test('cross-origin browser connections are rejected', async t => {
  const { port } = await fixture(t); const ws = new WebSocket(`ws://127.0.0.1:${port}/ws`, { origin: 'https://untrusted.example' });
  const [error] = await once(ws, 'error'); assert.ok(error); assert.notEqual(ws.readyState, WebSocket.OPEN);
});
test('message flood closes connection', async t => {
  const { port } = await fixture(t); const ws = await socket(port); const closed = once(ws, 'close');
  for (let i = 0; i < 90; i++) ws.send(JSON.stringify({ type: 'ping' }));
  const [code] = await closed; assert.equal(code, 1008);
});

test('Stremio core resolves signed streams and validates manifests over HTTP', async t => {
  const { port } = await fixture(t);
  const post = body => fetch(`http://127.0.0.1:${port}/api/core`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const url = 'https://example.com/video.mp4?token=a%2Fb';
  const resolved = await post({ operation: 'resolve', stream: { url } });
  assert.equal(resolved.status, 200); assert.equal((await resolved.json()).url, url);
  const manifest = await post({ operation: 'manifest', manifest: { id: 'test', name: '映画', version: '1.0.0', types: ['movie'], resources: ['stream'], catalogs: [] } });
  assert.equal(manifest.status, 200); assert.equal((await manifest.json()).name, '映画');
  assert.equal((await post({ operation: 'resolve', stream: { url: 'file:///etc/passwd' } })).status, 400);
  assert.equal((await post({ operation: 'manifest', manifest: { id: 'broken' } })).status, 400);
});
test('core endpoint rejects malformed and oversized bodies', async t => {
  const { port } = await fixture(t);
  const bad = await fetch(`http://127.0.0.1:${port}/api/core`, { method: 'POST', body: '{' }); assert.equal(bad.status, 400);
  const large = await fetch(`http://127.0.0.1:${port}/api/core`, { method: 'POST', body: JSON.stringify({ text: 'x'.repeat(70000) }) }); assert.equal(large.status, 413);
});
test('missing core binary returns an actionable service-unavailable response', async t => {
  const app = await createServer({ coreBinary: '/nonexistent/stremio-vr-core' }); app.server.listen(0, '127.0.0.1'); await once(app.server, 'listening'); t.after(() => app.close());
  const response = await fetch(`http://127.0.0.1:${app.server.address().port}/api/core`, { method: 'POST', body: JSON.stringify({ operation: 'resolve', stream: { url: 'https://example.com/a' } }) });
  assert.equal(response.status, 503); assert.match((await response.json()).error, /build:core/);
});

test('two WebSocket players see the same tabletop move', async t => {
  const {port}=await fixture(t);const host=await socket(port),joined=receive(host,'joined');host.send(JSON.stringify({type:'create'}));const session=await joined;
  const guest=await socket(port),ready=receive(guest,'state');guest.send(JSON.stringify({type:'join',roomId:session.roomId}));await ready;
  let changed=receive(guest,'state',s=>s.games.tictactoe.players[0]!==null);host.send(JSON.stringify({type:'game',game:'tictactoe',action:'join'}));await changed;
  changed=receive(host,'state',s=>s.games.tictactoe.players[1]!==null);guest.send(JSON.stringify({type:'game',game:'tictactoe',action:'join'}));const state=await changed;
  changed=receive(guest,'state',s=>s.games.tictactoe.revision>state.games.tictactoe.revision);host.send(JSON.stringify({type:'game',game:'tictactoe',action:'move',cell:4,revision:state.games.tictactoe.revision}));const next=await changed;
  assert.equal(next.games.tictactoe.board[4],'X');assert.equal(next.games.tictactoe.turn,1);
});
