import test from 'node:test';
import assert from 'node:assert/strict';
import { Rooms } from '../server/rooms.js';
function client() {return {readyState:1,messages:[],send(raw){this.messages.push(JSON.parse(raw));},close(){this.readyState=3;}};}
function setup(t) {
 const rooms=new Rooms();t.after(()=>rooms.close());const a=client(),b=client(),observer=client();rooms.handle(a,{type:'create'});for(const c of [b,observer])rooms.handle(c,{type:'join',roomId:a.roomId});
 const room=rooms.rooms.get(a.roomId); const action=(who,game,act,rest={})=>rooms.handle(who,{type:'game',game,action:act,revision:room.games.states[game]?.revision,...rest});
 return {rooms,room,a,b,observer,action,join(game){action(a,game,'join');action(b,game,'join');}};
}
test('game seats, turns, revision and observer authority are enforced',t=>{
 const f=setup(t);f.join('chess');const state=f.room.games.states.chess;
 assert.throws(()=>f.action(f.observer,'chess','join'),/taken/);
 assert.throws(()=>f.action(f.observer,'chess','move',{from:'e2',to:'e4'}),/Join/);
 assert.throws(()=>f.action(f.b,'chess','move',{from:'e7',to:'e5'}),/turn/);
 assert.throws(()=>f.action(f.a,'chess','move',{from:'e2',to:'e5'}),/Illegal/);
 const revision=state.revision;f.action(f.a,'chess','move',{from:'e2',to:'e4'});
 assert.throws(()=>f.action(f.b,'chess','move',{from:'e7',to:'e5',revision}),/changed/);
 assert.equal(state.board[36],'P');assert.equal(state.turn,1);
 assert.throws(()=>f.action(f.observer,'chess','reset'),/Only/);
});
test('chess detects checkmate and disallows moves afterward',t=>{
 const f=setup(t);f.join('chess');for(const [who,from,to] of [[f.a,'f2','f3'],[f.b,'e7','e5'],[f.a,'g2','g4'],[f.b,'d8','h4']])f.action(who,'chess','move',{from,to});
 assert.match(f.room.games.states.chess.outcome,/Black wins by checkmate/);
 assert.throws(()=>f.action(f.a,'chess','move',{from:'a2',to:'a3'}),/finished/);
 f.action(f.b,'chess','reset');assert.equal(f.room.games.states.chess.outcome,'');assert.equal(f.room.games.states.chess.turn,0);
});
test('chess preserves castling, en passant, and underpromotion rules',t=>{
 const f=setup(t);f.join('chess');const games=f.room.games;
 games.chess.load('r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1');games.updateChess();f.action(f.a,'chess','move',{from:'e1',to:'g1'});assert.equal(games.chess.get('f1').type,'r');
 games.chess.load('4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2');games.updateChess();f.action(f.a,'chess','move',{from:'e5',to:'d6'});assert.equal(games.chess.get('d5'),undefined);assert.equal(games.chess.get('d6').type,'p');
 games.chess.load('4k3/P7/8/8/8/8/8/4K3 w - - 0 1');games.updateChess();f.action(f.a,'chess','move',{from:'a7',to:'a8',promotion:'n'});assert.equal(games.chess.get('a8').type,'n');
});
test('tic-tac-toe validates cells, win, draw, and reset',t=>{
 const f=setup(t);f.join('tictactoe');for(const [i,cell] of [0,3,1,4,2].entries())f.action(i%2?f.b:f.a,'tictactoe','move',{cell});
 assert.equal(f.room.games.states.tictactoe.outcome,'X wins');f.action(f.a,'tictactoe','reset');
 assert.throws(()=>f.action(f.a,'tictactoe','move',{cell:9}),/empty square/);
 for(const [i,cell] of [0,1,2,4,3,5,7,6,8].entries())f.action(i%2?f.b:f.a,'tictactoe','move',{cell});
 assert.equal(f.room.games.states.tictactoe.outcome,'Draw');
});
test('games are room-isolated, visible to late joiners, and seats release on leave',t=>{
 const f=setup(t);f.join('chess');f.action(f.a,'chess','move',{from:'e2',to:'e4'});
 const late=client();f.rooms.handle(late,{type:'join',roomId:f.a.roomId});assert.equal(late.messages.at(-1).games.chess.board[36],'P');
 const other=client();f.rooms.handle(other,{type:'create'});f.action(other,'chess','join');assert.equal(f.rooms.rooms.get(other.roomId).games.states.chess.board[36],'');
 f.rooms.handle(f.b,{type:'leave'});assert.equal(f.room.games.states.chess.players[1],null);f.action(f.observer,'chess','join');assert.equal(f.room.games.states.chess.players[1],f.observer.playerId);
});
test('game seats and board survive reconnect inside grace window',t=>{
 const f=setup(t);f.join('chess');f.action(f.a,'chess','move',{from:'d2',to:'d4'});const token=f.a.messages.find(m=>m.type==='joined').token;
 f.rooms.leave(f.a);const resumed=client();f.rooms.handle(resumed,{type:'join',roomId:f.room.id,token});assert.equal(f.room.games.states.chess.players[0],resumed.playerId);assert.equal(f.room.games.states.chess.board[35],'P');
});
