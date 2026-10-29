import { randomUUID, randomBytes } from 'node:crypto';
import { TableGames } from './games.js';

export const worlds = { cinema: { seats: 32 }, home: { seats: 6 } };
export const presets = { head: ['round', 'square'], body: ['regular', 'wide'], hair: ['none', 'short', 'tall'], outfit: ['casual', 'formal', 'sport'], accessory: ['none', 'glasses', 'hat', 'headset'] };
export function profile(input = {}) {
  if (!input || typeof input !== 'object' || Array.isArray(input)) input = {};
  const result = { name: String(input.name || 'Guest').trim().slice(0, 32) || 'Guest', skin: /^#[\da-f]{6}$/i.test(input.skin) ? input.skin : '#b98059', color: /^#[\da-f]{6}$/i.test(input.color) ? input.color : '#7958ff' };
  for (const [key, values] of Object.entries(presets)) result[key] = values.includes(input[key]) ? input[key] : values[0];
  return result;
}
export function mediaURL(value) {
  if (typeof value !== 'string' || value.length > 4096) throw new Error('Invalid stream URL');
  const url = new URL(value);
  if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password) throw new Error('Use an HTTP(S) stream URL');
  return url.href;
}
export function position(state, now = Date.now()) { return state.position + (state.paused ? 0 : Math.max(0, now - state.updatedAt) / 1000); }
export class Rooms {
  constructor({ now = Date.now, grace = 15000 } = {}) { this.rooms = new Map(); this.now = now; this.grace = grace; }
  send(ws, data) { if (ws?.readyState === 1) ws.send(JSON.stringify(data)); }
  snapshot(room) {
    return { type: 'state', id: room.id, host: room.host, world: room.world, serverTime: this.now(), players: [...room.players.values()].map(p => ({ id: p.id, profile: p.profile, seat: p.seat, seatedSeat: p.seatedSeat ?? -1, pose: p.pose, connected: !!p.ws, voice: p.voice })), playback: room.playback, games: room.games.snapshot() };
  }
  broadcast(room, data, except) { for (const p of room.players.values()) if (p.id !== except) this.send(p.ws, data); }
  state(room) { this.broadcast(room, this.snapshot(room)); }
  leave(ws, immediate = false) {
    const room = this.rooms.get(ws.roomId), player = room?.players.get(ws.playerId);
    if (!player || player.ws !== ws) return;
    player.ws = null; player.voice = false;
    const remove = () => {
      if (player.ws) return;
      room.players.delete(player.id); room.games.remove(player.id);
      if (room.host === player.id) room.host = [...room.players.values()].find(p => p.ws)?.id || room.players.keys().next().value;
      if (!room.players.size) this.rooms.delete(room.id); else this.state(room);
    };
    if (immediate) remove(); else { player.timer = setTimeout(remove, this.grace); player.timer.unref?.(); this.state(room); }
    ws.roomId = null; ws.playerId = null;
  }
  handle(ws, message) {
    if (!message || typeof message !== 'object') throw new Error('Invalid message');
    const { type } = message;
    if (type === 'ping') { this.send(ws, { type: 'pong', sentAt: message.sentAt, serverTime: this.now() }); return; }
    if (type === 'create' || type === 'join') {
      const normalizedProfile = profile(message.profile);
      if (ws.roomId) throw new Error('Leave the current room first');
      let room;
      if (type === 'create') {
        if (this.rooms.size >= 500) throw new Error('Server is full');
        const id = randomBytes(6).toString('hex');
        room = { id, games: new TableGames(), world: 'cinema', players: new Map(), playback: { url: '', title: '', position: 0, paused: true, updatedAt: this.now(), revision: 0 } };
        this.rooms.set(id, room);
      } else { room = this.rooms.get(String(message.roomId)); if (!room) throw new Error('Room not found'); }
      let player = [...room.players.values()].find(p => p.token === message.token);
      if (player) {
        clearTimeout(player.timer);
        if (player.ws) { player.ws.roomId = null; player.ws.close(4000, 'Session resumed elsewhere'); }
      } else {
        if (room.players.size >= 8) throw new Error('Room is full (8 participants)');
        player = { id: randomUUID(), token: randomBytes(24).toString('hex'), profile: normalizedProfile, seat: Array.from({ length: 8 }, (_, i) => i).find(i => ![...room.players.values()].some(p => p.seat === i)), voice: false };
        room.players.set(player.id, player);
      }
      player.ws = ws; ws.roomId = room.id; ws.playerId = player.id;
      room.host ||= player.id;
      this.send(ws, { type: 'joined', playerId: player.id, token: player.token, roomId: room.id }); this.state(room); return;
    }
    const room = this.rooms.get(ws.roomId), player = room?.players.get(ws.playerId);
    if (!player || player.ws !== ws) throw new Error('Join a room first');
    if (type === 'leave') { this.leave(ws, true); this.send(ws, { type: 'left' }); return; }
    if (type === 'profile') { player.profile = profile(message.profile); this.state(room); return; }
    if (type === 'world') {
      if (room.host !== player.id) throw new Error('Only the host changes worlds');
      if (!Object.hasOwn(worlds, message.world)) throw new Error('Unknown world');
      if(room.world !== message.world) { room.world = message.world; for(const member of room.players.values()) { member.seatedSeat = -1; member.pose = undefined; } }
      this.state(room); return;
    }
    if (type === 'game') { room.games.handle(player.id,room.host,message); this.state(room); return; }
    if (type === 'seat') {
      const index = message.seatIndex;
      if (!Number.isInteger(index) || index < -1 || index >= worlds[room.world].seats) throw new Error('Invalid seat');
      if (index >= 0 && [...room.players.values()].some(p => p.id !== player.id && p.seatedSeat === index)) throw new Error('Seat is occupied');
      player.seatedSeat = index; player.pose = undefined; this.state(room); return;
    }
    if (type === 'voice') { player.voice = message.enabled === true; this.state(room); return; }
    if (type === 'playback') {
      if (room.host !== player.id) throw new Error('Only the host controls playback');
      const next = { ...room.playback, position: position(room.playback, this.now()) };
      if (message.url !== undefined) { next.url = mediaURL(message.url); next.title = String(message.title || 'Shared stream').slice(0, 160); next.position = 0; next.paused = true; }
      if (message.position !== undefined) { if (!Number.isFinite(message.position) || message.position < 0 || message.position > 864000) throw new Error('Invalid playback position'); next.position = message.position; }
      if (message.paused !== undefined) { if (typeof message.paused !== 'boolean') throw new Error('Invalid paused state'); next.paused = message.paused; }
      next.updatedAt = this.now(); next.revision++; room.playback = next; this.state(room); return;
    }
    if (type === 'pose') {
      const valid = (v, bound) => Array.isArray(v) && v.length === 3 && v.every(n => Number.isFinite(n) && Math.abs(n) <= bound);
      const pose = {};
      for (const key of ['head', 'left', 'right']) { const part = message.pose?.[key]; if (part && valid(part.position, 100) && valid(part.rotation, 360)) pose[key] = { position: part.position, rotation: part.rotation }; }
      if (!pose.head) throw new Error('Invalid pose');
      player.pose = pose; this.broadcast(room, { type: 'pose', playerId: player.id, seated: (player.seatedSeat ?? -1) >= 0, pose }, player.id); return;
    }
    if (type === 'audio') {
      if (!player.voice) throw new Error('Enable voice first');
      if (typeof message.pcm !== 'string' || message.pcm.length !== 4268 || !/^[A-Za-z0-9+/]+=$/.test(message.pcm) || Buffer.from(message.pcm, 'base64').length !== 3200) throw new Error('Invalid PCM audio frame');
      const now = this.now();
      if (player.lastAudio !== undefined && now - player.lastAudio < 80) return;
      player.lastAudio = now;
      this.broadcast(room, { type: 'audio', playerId: player.id, pcm: message.pcm }, player.id); return;
    }
    if (type === 'signal') {
      const target = room.players.get(message.target);
      if (!target?.ws || target === player) throw new Error('Participant unavailable');
      this.send(target.ws, { type: 'signal', from: player.id, data: message.data }); return;
    }
    throw new Error('Unknown message type');
  }
  close() { for (const room of this.rooms.values()) for (const p of room.players.values()) clearTimeout(p.timer); this.rooms.clear(); }
}
