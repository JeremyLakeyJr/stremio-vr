import {EventEmitter} from 'node:events';
import WebSocket from 'ws';
export class Cdp extends EventEmitter {
 constructor(socket){super();this.socket=socket;this.next=0;this.pending=new Map();socket.on('message',raw=>{const m=JSON.parse(raw);if(m.id){const item=this.pending.get(m.id);if(!item)return;this.pending.delete(m.id);clearTimeout(item.timer);m.error?item.reject(Error(m.error.message)):item.resolve(m.result);}else this.emit(m.method,m.params);});socket.on('close',()=>{for(const item of this.pending.values()){clearTimeout(item.timer);item.reject(Error('Browser closed'));}this.pending.clear();});socket.on('error',()=>{});}
 static async connect(url){const socket=new WebSocket(url);await new Promise((resolve,reject)=>{socket.once('open',resolve);socket.once('error',reject);});return new Cdp(socket);}
 call(method,params={}){return new Promise((resolve,reject)=>{const id=++this.next;const timer=setTimeout(()=>{this.pending.delete(id);reject(Error('Browser command timed out'));},20000);this.pending.set(id,{resolve,reject,timer});this.socket.send(JSON.stringify({id,method,params}));});}
 async evaluate(expression){const result=await this.call('Runtime.evaluate',{expression,awaitPromise:true,returnByValue:true,userGesture:true});if(result.exceptionDetails)throw Error(result.exceptionDetails.exception?.description||'Browser script failed');return result.result.value;}
 close(){this.socket.close();}
}
