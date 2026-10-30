import { randomUUID } from 'node:crypto';
export class RoomLayout {
  constructor() { this.revision = 0; this.items = []; }
  snapshot() { return { revision: this.revision, items: this.items }; }
  handle(message) {
    if (message.revision !== this.revision) throw new Error('Room layout changed; reopen placement and try again');
    const index = this.items.findIndex(item => item.id === message.id);
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
    const item = { id: index >= 0 ? message.id : randomUUID(), kind, position: [...message.position], rotation };
    if (index >= 0) this.items[index] = item; else this.items.push(item);
    this.revision++;
  }
}
