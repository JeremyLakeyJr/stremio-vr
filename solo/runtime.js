// Bundled with the same chess.js, TableGames and RoomLayout rules used by multiplayer.
// This engine runs in an isolated, network-disabled WebView on the headset.
class MovieNightSolo {
 constructor(initial={},emit=()=>{},schedule=fn=>setTimeout(fn,350)){
  this.emit=emit;this.schedule=schedule;this.generation=initial.generation;this.profile=initial.profile||{name:'Player'};
  this.games=new TableGames();this.layout=new RoomLayout();this.computer=true;this.closed=false;this.pending=new Set();
  this.playback={url:'',title:'',position:0,paused:true,updatedAt:Date.now(),revision:0};this.seat=-1;
  const save=initial.saved||{};
  try{
   if(save.version===1){
    this.computer=save.computer!==false;
    for(const item of save.layout?.items||[])this.layout.handle({...item,type:'layout',action:'place',id:undefined,revision:this.layout.revision});
    for(const move of save.chessMoves||[])this.games.chess.move(move);
    if(Array.isArray(save.ticMoves))for(const cell of save.ticMoves)this.ticMove(cell);
   }
  }catch{this.games=new TableGames();this.layout=new RoomLayout();this.ticMoves=[];}
  this.games.updateChess();this.seats();
 }
 seats(){for(const s of Object.values(this.games.states))s.players=['solo','solo-other'];}
 start(){this.emit({generation:this.generation,message:{type:'joined',playerId:'solo',roomId:'solo',iceServers:[]}});this.publish();this.queueBots();}
 saved(){return {version:1,computer:this.computer,layout:this.layout.snapshot(),chessMoves:this.games.chess.history({verbose:true}).map(m=>({from:m.from,to:m.to,promotion:m.promotion})),ticMoves:this.ticMoves||[]};}
 publish(){
  const games=JSON.parse(JSON.stringify(this.games.snapshot()));
  for(const s of Object.values(games))s.players=['solo',this.computer?'computer':'solo'];
  this.emit({generation:this.generation,saved:this.saved(),message:{type:'state',id:'solo',host:'solo',world:'home',solo:true,computer:this.computer,serverTime:Date.now(),players:[{id:'solo',profile:this.profile,seat:0,seatedSeat:this.seat,connected:true,voice:false,voiceReady:false}],playback:this.playback,layout:this.layout.snapshot(),games}});
 }
 command(message){
  if(this.closed)return;
  try{
   switch(message.type){
    case 'layout':this.layout.handle(message);break;
    case 'profile':this.profile=message.profile;break;
    case 'seat':if(!Number.isInteger(message.seatIndex)||message.seatIndex< -1||message.seatIndex>=6)throw Error('Invalid seat');this.seat=message.seatIndex;break;
    case 'world':if(message.world!=='home')throw Error('Unknown world');break;
    case 'playback':{
     const p={...this.playback},now=Date.now();p.position+=p.paused?0:Math.max(0,now-p.updatedAt)/1000;
     if(message.url!==undefined){const u=new URL(message.url);if(!['http:','https:'].includes(u.protocol)||u.username||u.password)throw Error('Use an HTTP(S) video URL');p.url=u.href;p.title=String(message.title||'Video');p.position=0;p.paused=false;}
     if(message.position!==undefined){if(!Number.isFinite(message.position)||message.position<0)throw Error('Invalid playback position');p.position=message.position;}
     if(message.paused!==undefined)p.paused=message.paused===true;p.updatedAt=now;p.revision++;this.playback=p;break;
    }
    case 'solo-opponent':this.computer=message.computer!==false;for(const s of Object.values(this.games.states))s.revision++;break;
    case 'game':{
     const state=this.games.states[message.game];if(!state)throw Error('Unknown game');
     if(['join','leave'].includes(message.action))break; // Both local seats are available immediately.
     const actor=this.computer?'solo':state.players[state.turn];
     this.games.handle(actor,'solo',message);
     if(message.game==='tictactoe'){if(message.action==='reset')this.ticMoves=[];else if(message.action==='move')(this.ticMoves||=[]).push(message.cell);}
     break;
    }
    case 'pose':case 'ping':case 'voice':case 'signal':return;
    default:throw Error('Unknown solo action');
   }
   this.publish();this.queueBots();
  }catch(error){this.emit({generation:this.generation,message:{type:'error',message:error.message}});}
 }
 ticMove(cell){this.seats();const s=this.games.states.tictactoe;this.games.handle(s.players[s.turn],'solo',{game:'tictactoe',action:'move',cell,revision:s.revision});(this.ticMoves||=[]).push(cell);}
 queueBots(){
  if(!this.computer||this.closed)return;
  for(const game of ['chess','tictactoe']){
   const s=this.games.states[game];if(s.turn!==1||s.outcome||this.pending.has(game))continue;
   this.pending.add(game);const revision=s.revision;
   this.schedule(()=>{this.pending.delete(game);if(this.closed||!this.computer)return;const state=this.games.states[game];if(state.revision!==revision){this.queueBots();return;}if(state.turn!==1||state.outcome)return;
    try{const move=game==='chess'?this.chessReply():{cell:this.ticReply()};if(!move)return;
     this.games.handle('solo-other','solo',{type:'game',game,action:'move',revision,...move});if(game==='tictactoe')(this.ticMoves||=[]).push(move.cell);this.publish();
    }catch(error){this.emit({generation:this.generation,message:{type:'error',message:'Computer move failed: '+error.message}});}
   });
  }
 }
 chessReply(){
  const chess=this.games.chess,values={p:100,n:320,b:330,r:500,q:900,k:0};
  const evaluate=()=>{if(chess.isCheckmate())return chess.turn()==='w'?100000:-100000;if(chess.isDraw())return 0;return chess.board().flat().reduce((sum,p)=>sum+(p?(p.color==='b'?1:-1)*values[p.type]:0),0);};
  let best=null,score=-Infinity;
  for(const m of chess.moves({verbose:true})){
   chess.move(m);let value=evaluate();const replies=chess.isGameOver()?[]:chess.moves({verbose:true});
   for(const reply of replies){chess.move(reply);value=Math.min(value,evaluate());chess.undo();}
   chess.undo();if(value>score){score=value;best={from:m.from,to:m.to,promotion:m.promotion};}
  }
  return best;
 }
 ticReply(){
  const board=[...this.games.states.tictactoe.board],wins=[[0,1,2],[3,4,5],[6,7,8],[0,3,6],[1,4,7],[2,5,8],[0,4,8],[2,4,6]];
  function score(turn){for(const line of wins)if(board[line[0]]&&line.every(i=>board[i]===board[line[0]]))return board[line[0]]==='O'?10:-10;if(board.every(Boolean))return 0;let best=turn==='O'?-Infinity:Infinity;for(let i=0;i<9;i++)if(!board[i]){board[i]=turn;const value=score(turn==='O'?'X':'O');board[i]='';best=turn==='O'?Math.max(best,value):Math.min(best,value);}return best;}
  let result=-1,best=-Infinity;for(const i of [4,0,2,6,8,1,3,5,7])if(!board[i]){board[i]='O';const value=score('X');board[i]='';if(value>best){best=value;result=i;}}return result;
 }
 close(){this.closed=true;}
}
globalThis.MovieNightSolo=MovieNightSolo;
