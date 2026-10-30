import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import {readFileSync} from 'node:fs';
const source=readFileSync(new URL('../unity/Assets/StreamingAssets/solo.js',import.meta.url),'utf8');
function setup(saved){
 const context=vm.createContext({URL,Date,console});vm.runInContext(source,context);
 const messages=[],tasks=[];const runtime=new context.MovieNightSolo({generation:5,profile:{name:'Solo player'},saved},m=>messages.push(JSON.parse(JSON.stringify(m))),fn=>tasks.push(fn));runtime.start();
 return {runtime,messages,tasks,get state(){return messages.filter(m=>m.message.type==='state').at(-1).message;},send(m){runtime.command(m);},move(game,rest){runtime.command({type:'game',game,action:'move',revision:runtime.games.states[game].revision,...rest});}};
}
test('solo starts and handles playback, seat and layout with no network APIs',()=>{
 const f=setup();assert.equal(f.state.solo,true);assert.equal(f.state.players.length,1);
 f.send({type:'layout',action:'place',kind:'screen',revision:0,position:[1,2,3],rotation:[0,20,0]});assert.equal(f.state.layout.items.length,1);
 f.send({type:'seat',seatIndex:2});assert.equal(f.state.players[0].seatedSeat,2);
 f.send({type:'playback',url:'https://example.com/video.mp4',title:'Solo video',position:37});assert.equal(f.state.playback.position,37);assert.equal(f.state.playback.paused,false);
 f.send({type:'playback',position:42,paused:true});assert.equal(f.state.playback.position,42);assert.equal(f.state.playback.paused,true);
 f.send({type:'layout',action:'remove',id:f.state.layout.items[0].id,revision:0});assert.match(f.messages.at(-1).message.message,/changed/);assert.equal(f.state.layout.items.length,1);
});
test('solo computer responds with a legal chess move and cannot move after closing',()=>{
 const f=setup();f.move('chess',{from:'e2',to:'e4'});assert.equal(f.state.games.chess.turn,1);assert.equal(f.tasks.length,1);
 const legal=f.state.games.chess.legal.map(m=>m.from+m.to);f.tasks.shift()();const move=f.runtime.games.chess.history({verbose:true}).at(-1);assert.ok(legal.includes(move.from+move.to));assert.equal(f.state.games.chess.turn,0);
 f.move('chess',{from:'d2',to:'d4'});const count=f.messages.length;f.runtime.close();f.tasks.shift()();assert.equal(f.messages.length,count);
});
test('solo practice controls both seats and preserves layout and game history after restart',()=>{
 const f=setup();f.send({type:'solo-opponent',computer:false});f.move('chess',{from:'e2',to:'e4'});f.move('chess',{from:'e7',to:'e5'});
 f.move('tictactoe',{cell:0});f.move('tictactoe',{cell:4});
 f.send({type:'layout',action:'place',kind:'chess',revision:0,position:[0,0,2],rotation:[0,90,0]});
 const restored=setup(JSON.parse(JSON.stringify(f.runtime.saved())));assert.equal(restored.state.games.chess.fen,f.state.games.chess.fen);assert.deepEqual(restored.state.games.tictactoe.board,f.state.games.tictactoe.board);assert.deepEqual(restored.state.layout.items[0].position,[0,0,2]);assert.equal(restored.state.computer,false);assert.deepEqual(restored.state.games.chess.players,['solo','solo']);
});
test('solo tic-tac-toe computer blocks a loss and reset cancels queued replies',()=>{
 const f=setup();f.send({type:'solo-opponent',computer:false});for(const cell of [0,4,1])f.move('tictactoe',{cell});
 f.send({type:'solo-opponent',computer:true});f.tasks.shift()();assert.equal(f.state.games.tictactoe.board[2],'O');
 f.move('tictactoe',{cell:6});f.send({type:'game',game:'tictactoe',action:'reset'});while(f.tasks.length)f.tasks.shift()();assert.deepEqual(f.state.games.tictactoe.board,Array(9).fill(''));
});
test('solo rejects illegal chess and invalid URLs without changing state',()=>{
 const f=setup();const fen=f.state.games.chess.fen;f.move('chess',{from:'e2',to:'e5'});assert.match(f.messages.at(-1).message.message,/Illegal/);assert.equal(f.state.games.chess.fen,fen);
 f.send({type:'playback',url:'file:///private/test'});assert.equal(f.state.playback.url,'');assert.match(f.messages.at(-1).message.message,/HTTP/);
});
