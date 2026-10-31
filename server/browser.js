// Room-authorized signaling/control. Website pixels and audio travel over WebRTC.
export class RoomBrowser {
 constructor(rooms,{url=process.env.BROWSER_SERVICE_URL,request}={}){this.rooms=rooms;this.url=url;this.request=request||this.fetch.bind(this);this.sessions=new Map();this.closing=new Map();this.commands=new Map();this.timer=setInterval(()=>this.tick(),150);this.timer.unref();}
 async fetch(id,operation='',data,method='POST'){const response=await fetch(`${this.url}/sessions/${id}${operation?'/'+operation:''}`,{method,headers:{'Content-Type':'application/json'},body:method==='GET'?undefined:JSON.stringify(data||{}),signal:AbortSignal.timeout(30000)});const result=await response.json();if(!response.ok)throw Error(result.error||'Server browser unavailable');return result;}
 member(ws){const room=this.rooms.rooms.get(ws.roomId),player=room?.players.get(ws.playerId);if(!player||player.ws!==ws)throw Error('Join a room first');return {room,player};}
 valid(s){const r=this.rooms.rooms.get(s.id);return this.sessions.get(s.id)===s&&r?.host===s.owner&&r.players.get(s.owner)?.ws===s.ws;}
 async stop(id){if(this.closing.has(id))return this.closing.get(id);const task=this.stopSession(id);this.closing.set(id,task);try{await task;}finally{this.closing.delete(id);}}
 async stopSession(id){const s=this.sessions.get(id);if(!s)return;this.sessions.delete(id);const room=this.rooms.rooms.get(id);if(room){room.browser={active:false};this.rooms.state(room);}await s.ready.catch(()=>{});await this.request(id,'',{},'DELETE').catch(()=>{});}
 tabs(s,tabs){if(this.valid(s))this.rooms.send(s.ws,{type:'browser-tabs',tabs:(tabs||[]).map((t,index)=>({...t,index}))});}
 async handle(ws,message){
  if(message?.type!=='browser')return this.handleMessage(ws,message);
  const id=ws.roomId,previous=this.commands.get(id)||Promise.resolve();const task=previous.catch(()=>{}).then(()=>this.handleMessage(ws,message));this.commands.set(id,task);try{return await task;}finally{if(this.commands.get(id)===task)this.commands.delete(id);}
 }
 async handleMessage(ws,message){
  if(!['browser','browser-watch','browser-unwatch','browser-signal'].includes(message?.type))return false;
  const {room,player}=this.member(ws);if(!this.url)throw Error('Start the browser service with Docker Compose to use shared browsing');
  let s=this.sessions.get(room.id);
  if(message.type==='browser'){
   if(room.host!==player.id)throw Error('Only the host controls the browser');
   if(message.action==='close'){await this.stop(room.id);return true;}
   if(message.action==='open'){
    await this.closing.get(room.id);this.member(ws);s=this.sessions.get(room.id);
    if(s){await s.ready;this.tabs(s,(await this.request(room.id,'tabs',{},'GET')).tabs);return true;}
    if(!/^[a-f0-9]{64}$/.test(message.profileKey||''))throw Error('Invalid browser profile key');
    s={id:room.id,owner:player.id,ws,peers:new Map(),polling:false};this.sessions.set(room.id,s);room.browser={active:true,ready:false};room.playback.paused=true;room.playback.updatedAt=this.rooms.now();this.rooms.state(room);
    s.ready=this.request(room.id,'',{profileKey:message.profileKey,url:message.url}).then(result=>{if(this.valid(s)){room.browser={active:true,ready:true,width:result.width,height:result.height,fps:result.fps};this.tabs(s,result.tabs);this.rooms.state(room);}});
    try{await s.ready;}catch(e){if(this.sessions.get(room.id)===s){this.sessions.delete(room.id);room.browser={active:false};this.rooms.state(room);}throw e;}return true;
   }
   if(!s||!this.valid(s))throw Error('Open the browser first');await s.ready;if(!this.valid(s))throw Error('Browser host changed');
   if(message.action==='forget'){await this.request(room.id,'forget',{});await this.stop(room.id);return true;}
   const result=await this.request(room.id,'action',message);if(!['pointer','scroll'].includes(message.action))this.tabs(s,result.tabs);return true;
  }
  if(!s||!this.valid(s))throw Error('Shared browser is closed');await s.ready;
  if(message.type==='browser-watch'){if(s.peers.get(player.id)===ws)return true;s.peers.set(player.id,ws);await this.request(room.id,'watch',{playerId:player.id,iceServers:this.rooms.voiceServers(player.id)});}
  if(message.type==='browser-unwatch'){s.peers.delete(player.id);await this.request(room.id,'remove',{playerId:player.id});}
  if(message.type==='browser-signal'){
   if(s.peers.get(player.id)!==ws)throw Error('Start watching the browser first');const d=message.data;
   if(!d||!((d.type==='answer'&&typeof d.sdp==='string'&&d.sdp.length<=14000)||(typeof d.candidate==='string'&&d.candidate.length<=2048&&typeof d.sdpMid==='string'&&d.sdpMid.length<=64&&Number.isInteger(d.sdpMLineIndex)&&d.sdpMLineIndex>=0&&d.sdpMLineIndex<8)))throw Error('Invalid browser signaling');
   await this.request(room.id,'signal',{playerId:player.id,data:d});
  }return true;
 }
 async tick(){for(const s of this.sessions.values()){
  if(s.polling)continue;s.polling=true;
  try{
   await s.ready;if(!this.valid(s)){await this.stop(s.id);continue;}
   const room=this.rooms.rooms.get(s.id);
   for(const [id,ws] of s.peers)if(room.players.get(id)?.ws!==ws){s.peers.delete(id);await this.request(s.id,'remove',{playerId:id});}
   if(Date.now()-(s.lastTabs||0)>2000){s.lastTabs=Date.now();this.tabs(s,(await this.request(s.id,'tabs',{},'GET')).tabs);}
   const {events}=await this.request(s.id,'events',{},'GET');
   for(const event of events||[]){if(!this.valid(s))break;if(event.closed){await this.stop(s.id);break;}const target=s.peers.get(event.target);if(target&&room.players.get(event.target)?.ws===target)this.rooms.send(target,{type:'browser-signal',data:event.data,status:event.status});}
  }catch(e){if(this.valid(s))this.rooms.send(s.ws,{type:'error',message:'Shared browser stopped: '+e.message});await this.stop(s.id);}finally{s.polling=false;}
 }}
 async close(){clearInterval(this.timer);await Promise.all([...this.sessions.keys()].map(id=>this.stop(id)));}
}
