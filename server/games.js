import { Chess } from 'chess.js';

const lines = [[0,1,2],[3,4,5],[6,7,8],[0,3,6],[1,4,7],[2,5,8],[0,4,8],[2,4,6]];
export class TableGames {
  constructor() {
    this.chess = new Chess();
    this.states = {
      chess: { players: [null,null], revision: 0 },
      tictactoe: { players: [null,null], revision: 0, board: Array(9).fill(''), turn: 0, outcome: '' },
    };
    this.updateChess();
  }
  updateChess() {
    const c=this.chess, state=this.states.chess;
    state.board=c.board().flat().map(p=>p?(p.color==='w'?p.type.toUpperCase():p.type):'');
    state.fen=c.fen(); state.turn=c.turn()==='w'?0:1;
    state.outcome=c.isCheckmate()?`${c.turn()==='w'?'Black':'White'} wins by checkmate`:c.isStalemate()?'Draw by stalemate':c.isThreefoldRepetition()?'Draw by repetition':c.isInsufficientMaterial()?'Draw: insufficient material':c.isDraw()?'Draw':'';
    state.check=c.isCheck(); state.last=c.history().at(-1)||'';
    state.legal=c.isGameOver()?[]:c.moves({verbose:true}).map(m=>({from:m.from,to:m.to,promotion:m.promotion}));
  }
  snapshot() { return this.states; }
  remove(player) { for(const state of Object.values(this.states)) { state.players=state.players.map(id=>id===player?null:id); state.revision++; } }
  handle(player, host, message) {
    if(!Object.hasOwn(this.states,message.game)) throw new Error('Unknown tabletop game');
    const state=this.states[message.game], seat=state.players.indexOf(player);
    if(message.action==='join') {
      if(seat>=0)return;
      const slot=state.players.indexOf(null); if(slot<0)throw new Error('Both game seats are taken');
      state.players[slot]=player;state.revision++;return;
    }
    if(message.action==='leave') { if(seat>=0){state.players[seat]=null;state.revision++;}return; }
    if(message.action==='reset') {
      if(seat<0 && player!==host)throw new Error('Only a player or room host can reset the board');
      if(message.game==='chess') { this.chess.reset();this.updateChess(); }
      else { state.board=Array(9).fill('');state.turn=0;state.outcome=''; }
      state.revision++;return;
    }
    if(message.action!=='move')throw new Error('Unknown game action');
    if(seat<0)throw new Error('Join this game before moving');
    if(state.players.includes(null))throw new Error('Waiting for a second player');
    if(state.outcome)throw new Error('Game finished. Reset the board to play again');
    if(seat!==state.turn)throw new Error('Wait for your turn');
    if(message.revision!==state.revision)throw new Error('Board changed. Select your move again');
    if(message.game==='chess') {
      if(!/^[a-h][1-8]$/.test(message.from)||!/^[a-h][1-8]$/.test(message.to)||!['q','r','b','n'].includes(message.promotion??'q'))throw new Error('Invalid chess move');
      try { this.chess.move({from:message.from,to:message.to,promotion:message.promotion??'q'}); }
      catch {throw new Error('Illegal chess move');}
      this.updateChess();
    } else {
      const index=message.cell;
      if(!Number.isInteger(index)||index<0||index>8||state.board[index])throw new Error('Choose an empty square');
      state.board[index]=seat===0?'X':'O';
      if(lines.some(line=>line.every(i=>state.board[i]===state.board[index])))state.outcome=state.board[index]+' wins';
      else if(state.board.every(Boolean))state.outcome='Draw';
      state.turn=1-state.turn;
    }
    state.revision++;
  }
}
