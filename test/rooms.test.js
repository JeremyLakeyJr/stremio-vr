import test from 'node:test';
import assert from 'node:assert/strict';
import { Rooms, position, profile } from '../server/rooms.js';

function client() { return { readyState: 1, messages: [], send(raw) { this.messages.push(JSON.parse(raw)); }, close(code) { this.code = code; this.readyState = 3; } }; }
function last(ws, type) { return ws.messages.filter(m => m.type === type).at(-1); }
function setup(t, options) { const rooms = new Rooms(options); t.after(() => rooms.close()); const host = client(); rooms.handle(host, { type: 'create', profile: { name: 'Host' } }); return { rooms, host, id: host.roomId }; }
function join(rooms, id, extra = {}) { const ws = client(); rooms.handle(ws, { type: 'join', roomId: id, ...extra }); return ws; }

test('creates isolated rooms and never exposes reconnect tokens in state', t => {
  const { rooms, host, id } = setup(t); const guest = join(rooms, id); const other = client(); rooms.handle(other, { type: 'create' });
  assert.equal(last(host, 'state').players.length, 2); assert.equal(last(other, 'state').players.length, 1);
  assert.equal(last(guest, 'state').host, host.playerId);
  assert.ok(last(guest, 'joined').token); assert.equal(JSON.stringify(last(host, 'state')).includes('token'), false);
});
test('host authority, late join position and pause/seek preserve server timeline', t => {
  let now = 1000; const { rooms, host, id } = setup(t, { now: () => now });
  rooms.handle(host, { type: 'playback', url: 'https://example.com/movie.mp4' });
  rooms.handle(host, { type: 'playback', paused: false, position: 20 }); now += 5000;
  const guest = join(rooms, id); assert.equal(position(last(guest, 'state').playback, now), 25);
  assert.throws(() => rooms.handle(guest, { type: 'playback', paused: true }), /Only the host/);
  rooms.handle(host, { type: 'playback', paused: true }); now += 5000;
  assert.equal(position(last(host, 'state').playback, now), 25);
  rooms.handle(host, { type: 'playback', position: 8 }); assert.equal(last(host, 'state').playback.position, 8);
});
test('rejects malformed playback without partially changing room state', t => {
  const { rooms, host } = setup(t); const before = last(host, 'state').playback;
  for (const message of [{ position: -1 }, { position: NaN }, { paused: 'yes' }, { url: 'file:///etc/passwd' }, { url: 'https://user:secret@example.com/' }, { url: 'https://example.com/', position: Infinity }]) assert.throws(() => rooms.handle(host, { type: 'playback', ...message }));
  assert.deepEqual(rooms.rooms.get(host.roomId).playback, before);
});
test('reconnect token restores identity and host within grace period', t => {
  const { rooms, host, id } = setup(t); const credentials = last(host, 'joined'); rooms.leave(host);
  const resumed = join(rooms, id, { token: credentials.token });
  assert.equal(resumed.playerId, credentials.playerId); assert.equal(last(resumed, 'state').host, credentials.playerId);
  assert.equal(last(resumed, 'state').players.length, 1);
});
test('live session replacement closes old socket and ignores its later close', t => {
  const { rooms, host, id } = setup(t); const credentials = last(host, 'joined');
  const replacement = join(rooms, id, { token: credentials.token }); rooms.leave(host);
  assert.equal(host.code, 4000); assert.equal(last(replacement, 'state').players[0].connected, true);
});
test('host transfers after explicit leave, final leave removes room', t => {
  const { rooms, host, id } = setup(t); const guest = join(rooms, id);
  rooms.handle(host, { type: 'leave' }); assert.equal(last(guest, 'state').host, guest.playerId);
  rooms.handle(guest, { type: 'leave' }); assert.equal(rooms.rooms.size, 0);
});
test('host transfers after reconnect grace expires', async t => {
  const { rooms, host, id } = setup(t, { grace: 15 }); const guest = join(rooms, id); rooms.leave(host);
  await new Promise(resolve => setTimeout(resolve, 30));
  assert.equal(last(guest, 'state').host, guest.playerId); assert.equal(last(guest, 'state').players.length, 1);
});
test('rooms enforce capacity and prevent joining two rooms', t => {
  const { rooms, host, id } = setup(t); for (let i = 0; i < 7; i++) join(rooms, id);
  assert.throws(() => join(rooms, id), /full/); assert.throws(() => rooms.handle(host, { type: 'create' }), /Leave/);
  assert.throws(() => join(rooms, 'missing'), /not found/);
});
test('avatar profiles are normalized and broadcast only within their room', t => {
  const { rooms, host, id } = setup(t); const guest = join(rooms, id); const outsider = client(); rooms.handle(outsider, { type: 'create' }); outsider.messages = [];
  rooms.handle(host, { type: 'profile', profile: { name: 'A'.repeat(100), hair: 'tall', skin: 'invalid', accessory: 'hat' } });
  const p = last(guest, 'state').players[0].profile;
  assert.equal(p.name.length, 32); assert.equal(p.skin, '#b98059'); assert.equal(p.hair, 'tall'); assert.equal(p.accessory, 'hat'); assert.equal(outsider.messages.length, 0);
  assert.equal(profile({ head: 'bogus' }).head, 'round');
});
test('poses accept full rotations, reject malformed values and stay in room', t => {
  const { rooms, host, id } = setup(t); const guest = join(rooms, id); const outsider = client(); rooms.handle(outsider, { type: 'create' }); outsider.messages = [];
  rooms.handle(host, { type: 'pose', pose: { head: { position: [0, 1.6, 0], rotation: [0, 359, 0] } } });
  assert.equal(last(guest, 'pose').playerId, host.playerId); assert.equal(outsider.messages.length, 0);
  assert.throws(() => rooms.handle(host, { type: 'pose', pose: { head: { position: [Infinity, 0, 0], rotation: [0, 0, 0] } } }), /Invalid pose/);
});
test('voice requires opt-in, validates frames, throttles and isolates rooms', t => {
  let now = 1000; const { rooms, host, id } = setup(t, { now: () => now }); const guest = join(rooms, id); const outsider = client(); rooms.handle(outsider, { type: 'create' }); outsider.messages = [];
  const audio = { type: 'audio', pcm: Buffer.alloc(3200).toString('base64') };
  assert.throws(() => rooms.handle(host, audio), /Enable voice/);
  rooms.handle(host, { type: 'voice', enabled: true });
  rooms.handle(host, audio); rooms.handle(host, audio); assert.equal(guest.messages.filter(m => m.type === 'audio').length, 1);
  now += 100; rooms.handle(host, audio); assert.equal(guest.messages.filter(m => m.type === 'audio').length, 2);
  assert.equal(outsider.messages.length, 0);
  assert.throws(() => rooms.handle(host, { type: 'audio', pcm: 'bad' }), /Invalid PCM/);
  rooms.handle(host, { type: 'voice', enabled: false }); assert.throws(() => rooms.handle(host, audio), /Enable voice/);
});
test('signaling cannot target participants in another room', t => {
  const { rooms, host, id } = setup(t); const guest = join(rooms, id); const outsider = client(); rooms.handle(outsider, { type: 'create' });
  assert.throws(() => rooms.handle(host, { type: 'signal', target: outsider.playerId, data: {} }), /unavailable/);
  rooms.handle(host, { type: 'signal', target: guest.playerId, data: { hello: true } }); assert.equal(last(guest, 'signal').from, host.playerId);
});
test('malformed profiles do not leak empty rooms and null profiles normalize', t => {
  const rooms = new Rooms(); t.after(() => rooms.close());
  assert.throws(() => rooms.handle(client(), { type: 'create', profile: { name: { toString: {} } } }));
  assert.equal(rooms.rooms.size, 0);
  const ws = client(); rooms.handle(ws, { type: 'create', profile: null }); assert.equal(last(ws, 'state').players[0].profile.name, 'Guest');
});
test('seats remain stable when another player leaves and are reused on join', t => {
  const { rooms, host, id } = setup(t); const guest = join(rooms, id);
  const seat = last(guest, 'state').players.find(p => p.id === guest.playerId).seat;
  rooms.handle(host, { type: 'leave' });
  assert.equal(last(guest, 'state').players[0].seat, seat);
  const newcomer = join(rooms, id); assert.equal(last(newcomer, 'state').players.find(p => p.id === newcomer.playerId).seat, 0);
});

test('chairs are exclusive and released on standing or explicit leave', t => {
  const { rooms, host, id } = setup(t); const guest = join(rooms,id);
  rooms.handle(host,{type:'seat',seatIndex:4});
  assert.equal(last(guest,'state').players.find(p=>p.id===host.playerId).seatedSeat,4);
  assert.throws(()=>rooms.handle(guest,{type:'seat',seatIndex:4}),/occupied/);
  for(const value of [-2,32,1.5,'2',null]) assert.throws(()=>rooms.handle(guest,{type:'seat',seatIndex:value}),/Invalid seat/);
  rooms.handle(host,{type:'seat',seatIndex:-1}); rooms.handle(guest,{type:'seat',seatIndex:4});
  rooms.handle(guest,{type:'leave'}); rooms.handle(host,{type:'seat',seatIndex:4});
});
test('late joiners receive movement pose and only hosts can select supported worlds', t => {
  const { rooms, host, id } = setup(t); const pose={head:{position:[4,1.65,-2],rotation:[0,90,0]}};
  rooms.handle(host,{type:'pose',pose}); const guest=join(rooms,id);
  assert.deepEqual(last(guest,'state').players.find(p=>p.id===host.playerId).pose,pose);
  assert.equal(last(guest,'state').world,'cinema');
  assert.throws(()=>rooms.handle(guest,{type:'world',world:'cinema'}),/Only the host/);
  assert.throws(()=>rooms.handle(host,{type:'world',world:'unknown'}),/Unknown world/);
  rooms.handle(host,{type:'world',world:'cinema'});
});

test('changing worlds clears seating and poses and applies destination capacity', t => {
  const { rooms, host, id }=setup(t); const guest=join(rooms,id);
  rooms.handle(guest,{type:'seat',seatIndex:20}); rooms.handle(guest,{type:'pose',pose:{head:{position:[0,1,0],rotation:[0,0,0]}}});
  rooms.handle(host,{type:'world',world:'home'}); const state=last(guest,'state');
  assert.equal(state.world,'home'); assert.ok(state.players.every(p=>p.seatedSeat===-1 && p.pose===undefined));
  assert.throws(()=>rooms.handle(guest,{type:'seat',seatIndex:6}),/Invalid seat/);
  rooms.handle(guest,{type:'seat',seatIndex:5}); rooms.handle(host,{type:'world',world:'cinema'}); rooms.handle(guest,{type:'seat',seatIndex:31});
});
