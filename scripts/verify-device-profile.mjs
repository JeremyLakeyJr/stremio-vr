// Read-only check of a headset's advertised profile after an app restart.
// Usage: node scripts/verify-device-profile.mjs saved-profile.json ROOM_CODE
import { readFileSync } from 'node:fs';
import WebSocket from 'ws';
const expected = JSON.parse(readFileSync(process.argv[2], 'utf8')).profile;
const ws = new WebSocket(process.env.ROOM_SERVER || 'ws://127.0.0.1:3000/ws');
const timer = setTimeout(() => { console.error('No matching device profile appeared'); ws.terminate(); process.exitCode = 1; }, 10000);
ws.on('open', () => ws.send(JSON.stringify({ type: 'join', roomId: process.argv[3], profile: { name: 'Profile verification' } })));
ws.on('message', raw => {
  const message = JSON.parse(raw);
  if (message.type === 'state' && message.players.some(player => player.connected && Object.entries(expected).every(([key, value]) => player.profile[key] === value))) {
    console.log('PASS: rejoined device advertises the saved avatar profile');
    clearTimeout(timer); ws.send(JSON.stringify({ type: 'leave' })); ws.close();
  }
});
ws.on('error', error => { clearTimeout(timer); console.error(error.message); process.exitCode = 1; });
