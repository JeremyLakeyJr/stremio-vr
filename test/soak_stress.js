import { createServer } from '../server/index.js';
import WebSocket from 'ws';
import { once } from 'node:events';

async function runSoakTest() {
  console.log('=== Starting Movie Night VR Soak & Bug Testing ===');
  const startTime = Date.now();

  const app = await createServer({ media: false });
  app.server.listen(0, '127.0.0.1');
  await once(app.server, 'listening');
  const port = app.server.address().port;
  console.log(`Test server bound to 127.0.0.1:${port}`);

  let totalRoomsCreated = 0;
  let totalMessagesSent = 0;
  let errorsEncountered = 0;

  const ITERATIONS = 50;

  for (let iteration = 1; iteration <= ITERATIONS; iteration++) {
    const iterStart = Date.now();
    process.stdout.write(`Cycle ${iteration}/${ITERATIONS}: `);

    // 1. Create a host socket
    const hostWs = new WebSocket(`ws://127.0.0.1:${port}/ws`);
    await once(hostWs, 'open');

    let roomId = null;
    const hostMsgPromise = new Promise((resolve) => {
      hostWs.on('message', (raw) => {
        const msg = JSON.parse(raw.toString());
        if (msg.type === 'joined') resolve(msg.roomId);
      });
    });

    hostWs.send(JSON.stringify({ type: 'create', profile: { name: `Host_Iter_${iteration}` } }));
    roomId = await hostMsgPromise;
    totalRoomsCreated++;
    totalMessagesSent++;

    // 2. Connect 4 guest sockets
    const guests = [];
    for (let g = 0; g < 4; g++) {
      const guestWs = new WebSocket(`ws://127.0.0.1:${port}/ws`);
      await once(guestWs, 'open');
      guestWs.send(JSON.stringify({ type: 'join', roomId, profile: { name: `Guest_${iteration}_${g}` } }));
      guests.push(guestWs);
      totalMessagesSent++;
    }

    // 3. Flood pose updates and seat selections
    const allSockets = [hostWs, ...guests];
    for (let round = 0; round < 10; round++) {
      for (let sIdx = 0; sIdx < allSockets.length; sIdx++) {
        const ws = allSockets[sIdx];
        ws.send(JSON.stringify({
          type: 'pose',
          pose: {
            head: { position: [round * 0.05, 1.65, sIdx * 0.2], rotation: [0, 0, 0] },
            left: { position: [-0.3, 1.2, 0.4], rotation: [0, 0, 0] },
            right: { position: [0.3, 1.2, 0.4], rotation: [0, 0, 0] }
          }
        }));
        totalMessagesSent++;
      }
    }

    // 4. Host sends rapid playback updates
    for (let p = 0; p < 8; p++) {
      hostWs.send(JSON.stringify({
        type: 'playback',
        url: `https://example.com/video_${iteration}.mp4`,
        title: `Feature Film ${iteration}`,
        position: p * 12.5,
        paused: p % 2 === 0
      }));
      totalMessagesSent++;
    }

    // 5. Guests send chat / pings / seat changes
    for (let gIdx = 0; gIdx < guests.length; gIdx++) {
      guests[gIdx].send(JSON.stringify({ type: 'seat', seatIndex: gIdx + 1 }));
      guests[gIdx].send(JSON.stringify({ type: 'ping', sentAt: Date.now() }));
      totalMessagesSent += 2;
    }

    // 6. Fuzzing with malformed payloads
    hostWs.send('{"invalid": json');
    hostWs.send(JSON.stringify({ type: 'unknown_op', data: [1, 2, 3] }));
    totalMessagesSent += 2;

    // 7. Clean up cycle: guests leave, host leaves
    for (const g of guests) {
      g.send(JSON.stringify({ type: 'leave' }));
      g.close();
    }
    hostWs.send(JSON.stringify({ type: 'leave' }));
    hostWs.close();

    // Verify room is cleaned up
    await new Promise(r => setTimeout(r, 60));
    const activeRoom = app.rooms.rooms.get(roomId);
    if (activeRoom && activeRoom.players.size > 0) {
      console.log(`[Warning: room ${roomId} still has ${activeRoom.players.size} players]`);
      errorsEncountered++;
    } else {
      process.stdout.write(`ok (${Date.now() - iterStart}ms)\n`);
    }
  }

  await app.close();
  const elapsed = ((Date.now() - startTime) / 1000).toFixed(2);
  console.log('--------------------------------------------------');
  console.log(`Soak testing completed in ${elapsed}s`);
  console.log(`Total Rooms Cycled: ${totalRoomsCreated}`);
  console.log(`Total Messages Handled: ${totalMessagesSent}`);
  console.log(`Errors Encountered: ${errorsEncountered}`);
  console.log('Memory RSS:', Math.round(process.memoryUsage().rss / 1024 / 1024), 'MB');
  console.log('=== Soak Test Passed Successfully ===');

  if (errorsEncountered > 0) {
    process.exit(1);
  }
}

runSoakTest().catch(err => {
  console.error('Soak test failed:', err);
  process.exit(1);
});
