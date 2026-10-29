// Exercises the deployed HTTP/core and two-client WebSocket paths.
import assert from 'node:assert/strict';
import WebSocket from 'ws';
const base=new URL(process.argv[2]||'http://127.0.0.1:3100');
assert.ok(['http:','https:'].includes(base.protocol));
assert.equal((await (await fetch(new URL('/health',base))).json()).ok,true);
const response=await fetch(new URL('/api/core',base),{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({operation:'resolve',stream:{url:'https://example.com/test.mp4?token=a%2Fb'}})});
assert.equal(response.status,200); assert.equal((await response.json()).url,'https://example.com/test.mp4?token=a%2Fb');
const endpoint=new URL('/ws',base); endpoint.protocol=base.protocol==='https:'?'wss:':'ws:';
const sockets=[];
async function peer() {
 const ws=new WebSocket(endpoint); const messages=[]; ws.on('message',raw=>messages.push(JSON.parse(raw))); sockets.push(ws);
 await new Promise((resolve,reject)=>{ws.once('open',resolve);ws.once('error',reject);});
 return {ws,send:data=>ws.send(JSON.stringify(data)),async next(type,predicate=()=>true) { const end=Date.now()+10000; while(Date.now()<end) { const i=messages.findIndex(m=>m.type===type&&predicate(m)); if(i>=0) return messages.splice(i,1)[0]; await new Promise(r=>setTimeout(r,20)); } throw Error('Timed out waiting for '+type); }};
}
try {
 const a=await peer(), b=await peer(); a.send({type:'create',profile:{name:'Hosting check A'}}); const created=await a.next('joined');
 b.send({type:'join',roomId:created.roomId,profile:{name:'Hosting check B'}}); await b.next('joined');
 a.send({type:'seat',seatIndex:2}); await b.next('state',s=>s.players.some(p=>p.id===created.playerId&&p.seatedSeat===2));
 b.send({type:'seat',seatIndex:2}); assert.match((await b.next('error')).message,/occupied/);
 a.send({type:'playback',url:'https://example.com/test.mp4',position:12,paused:true}); await b.next('state',s=>s.playback.position===12);
 a.send({type:'pose',pose:{head:{position:[1,1.6,2],rotation:[0,90,0]}}}); assert.equal((await b.next('pose')).seated,true);
 for(const game of ['chess','tictactoe']) {
  a.send({type:'game',game,action:'join'});await b.next('state',s=>s.games?.[game]?.players[0]===created.playerId);
  b.send({type:'game',game,action:'join'});const ready=await a.next('state',s=>s.games?.[game]?.players[1]!=null);
  a.send({type:'game',game,action:'move',revision:ready.games[game].revision,...(game==='chess'?{from:'e2',to:'e4'}:{cell:4})});
  await b.next('state',s=>s.games?.[game]?.board[game==='chess'?36:4]===(game==='chess'?'P':'X'));
 }
 a.send({type:'leave'}); await a.next('left'); await b.next('state',s=>s.host!==created.playerId);
 b.send({type:'leave'}); await b.next('left');
 console.log('PASS: health, Rust core, two WebSocket clients, exclusive seating, playback, movement, chess/tic-tac-toe, and host transfer');
} finally { for(const ws of sockets) ws.close(); }
