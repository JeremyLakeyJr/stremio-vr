import { randomUUID } from 'node:crypto';
export class RoomLayout {
  constructor() { this.revision = 0; this.items = []; this.mainScreen = {width:4.34,curve:0}; }
  snapshot() { return { revision: this.revision, items: this.items, mainScreen: this.mainScreen }; }
  handle(message) {
    if (message.revision !== this.revision) throw new Error('Room layout changed; reopen placement and try again');
    const appearance = (message, previous={width:2.1,curve:0}) => {
      const width=message.width??previous.width,curve=message.curve??previous.curve;
      if(!Number.isFinite(width)||width<.6||width>8||!Number.isFinite(curve)||curve<0||curve>120)throw Error('Invalid monitor size or curvature');
      return {width,curve};
    };
    if(message.action==='configure'&&message.id==='main'){this.mainScreen=appearance(message,this.mainScreen);this.revision++;return;}
    const index = this.items.findIndex(item => item.id === message.id);
    if(message.action==='configure'){if(index<0||this.items[index].kind!=='screen')throw Error('Monitor not found');Object.assign(this.items[index],appearance(message,this.items[index]));this.revision++;return;}
    if (message.action === 'remove') {
      if (index < 0) throw new Error('Object not found');
      this.items.splice(index, 1); this.revision++; return;
    }
    if (message.action !== 'place') throw new Error('Unknown layout action');
    const kind = index >= 0 ? this.items[index].kind : message.kind;
    if (!['screen', 'chess', 'tictactoe'].includes(kind)) throw new Error('Unknown object type');
    if (message.id && index < 0) throw new Error('Object not found');
    if (index < 0 && (kind === 'screen' ? this.items.filter(item => item.kind === kind).length >= 6 : this.items.some(item => item.kind === kind))) throw new Error(kind === 'screen' ? 'Limit: six extra screens' : 'Move the existing table instead');
    const vector = (value, limit) => Array.isArray(value) && value.length === 3 && value.every(n => Number.isFinite(n) && Math.abs(n) <= limit);
    if (!vector(message.position, 100) || !vector(message.rotation, 360)) throw new Error('Invalid object transform');
    const rotation = kind === 'screen' ? [...message.rotation] : [0, message.rotation[1], 0];
    const shape=kind==='screen'?appearance(message,index>=0?this.items[index]:undefined):{};
    const item = {...shape, id: index >= 0 ? message.id : randomUUID(), kind, position: [...message.position], rotation };
    if (index >= 0) this.items[index] = item; else this.items.push(item);
    this.revision++;
  }
}
