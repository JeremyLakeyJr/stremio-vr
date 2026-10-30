import test from 'node:test';
import assert from 'node:assert/strict';
import { Rooms } from '../server/rooms.js';
import { RoomLayout } from '../server/layout.js';
const place = (layout, kind = 'screen', extra = {}) => layout.handle({ action: 'place', kind, position: [1, 2, 3], rotation: [15, 90, 0], revision: layout.revision, ...extra });
test('layout starts empty, validates transforms, caps screens and tables, and rejects stale edits', () => {
  const layout = new RoomLayout(); assert.deepEqual(layout.items, []);
  for (let i=0;i<6;i++)place(layout);
  assert.throws(()=>place(layout),/six/);place(layout,'chess');place(layout,'tictactoe');assert.throws(()=>place(layout,'chess'),/existing/);
  assert.throws(()=>place(layout,'screen',{revision:0}),/changed/);
  const id=layout.items[0].id;
  for(const position of [[NaN,0,0],[101,0,0],[0,0],null])assert.throws(()=>place(layout,'screen',{id,position}),/transform/);
  place(layout,'screen',{id,position:[-3,1,7]});assert.deepEqual(layout.items[0].position,[-3,1,7]);
  assert.deepEqual(layout.items.find(i=>i.kind==='chess').rotation,[0,90,0]);
  layout.handle({action:'remove',id,revision:layout.revision});assert.equal(layout.items.length,7);place(layout);assert.equal(layout.items.length,8);
});
const socket=()=>({readyState:1,messages:[],send(value){this.messages.push(JSON.parse(value));}});
test('host-only layout is isolated, survives host transfer and is sent to late joiners without resetting games',t=>{
 const rooms=new Rooms();t.after(()=>rooms.close());const host=socket(),guest=socket(),outsider=socket();rooms.handle(host,{type:'create'});rooms.handle(guest,{type:'join',roomId:host.roomId});rooms.handle(outsider,{type:'create'});
 const room=rooms.rooms.get(host.roomId);const command={type:'layout',action:'place',kind:'chess',revision:0,position:[1,0,2],rotation:[0,45,0]};
 assert.throws(()=>rooms.handle(guest,command),/host/);rooms.handle(host,command);
 rooms.handle(host,{type:'game',game:'chess',action:'join'});const game=room.games.snapshot().chess;
 rooms.handle(host,{...command,id:room.layout.items[0].id,revision:1,position:[3,0,4]});assert.deepEqual(room.games.snapshot().chess,game);
 const late=socket();rooms.handle(late,{type:'join',roomId:host.roomId});assert.equal(late.messages.at(-1).layout.items.length,1);assert.equal(rooms.rooms.get(outsider.roomId).layout.items.length,0);
 rooms.handle(host,{type:'leave'});rooms.handle(guest,{...command,id:room.layout.items[0].id,revision:2,position:[4,0,5]});assert.deepEqual(room.layout.items[0].position,[4,0,5]);
});
