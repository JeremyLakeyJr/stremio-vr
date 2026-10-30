// Real encrypted audio between two browser peers through the room signaling server.
// Requires Chromium on PATH. Usage: node scripts/check-webrtc.mjs http://127.0.0.1:3000
import { spawn } from 'node:child_process';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { once } from 'node:events';
import WebSocket from 'ws';
const base=new URL(process.argv[2]||'http://127.0.0.1:3000');
const profile=await mkdtemp(join(tmpdir(),'movie-night-rtc-'));
const browser=spawn(process.env.CHROMIUM||'chromium',['--headless','--no-sandbox','--disable-dev-shm-usage','--autoplay-policy=no-user-gesture-required','--remote-debugging-port=0','--user-data-dir='+profile,base.href],{stdio:'ignore'});
let cdp;
try {
 let port;for(let i=0;i<100;i++){try{port=Number((await readFile(join(profile,'DevToolsActivePort'),'utf8')).split('\n')[0]);break;}catch{await new Promise(r=>setTimeout(r,100));}}
 if(!port)throw Error('Chromium did not start');
 const pages=await(await fetch(`http://127.0.0.1:${port}/json`)).json();cdp=new WebSocket(pages.find(p=>p.type==='page').webSocketDebuggerUrl);await once(cdp,'open');
 let id=0;const pending=new Map();cdp.on('message',raw=>{const m=JSON.parse(raw);if(pending.has(m.id)){const {resolve,reject}=pending.get(m.id);pending.delete(m.id);m.error?reject(Error(m.error.message)):resolve(m.result);}});
 const call=(method,params={})=>new Promise((resolve,reject)=>{const n=++id;pending.set(n,{resolve,reject});cdp.send(JSON.stringify({id:n,method,params}));});
 await call('Page.navigate',{url:base.href});
 for(let i=0;i<100;i++){const ready=await call('Runtime.evaluate',{expression:"document.readyState==='complete' && location.origin==="+JSON.stringify(base.origin),returnByValue:true});if(ready.result.value)break;await new Promise(r=>setTimeout(r,100));}
 const result=await call('Runtime.evaluate',{awaitPromise:true,returnByValue:true,expression:`(${async function run(base){
  const wait=ms=>new Promise(r=>setTimeout(r,ms));
  async function until(check){for(let i=0;i<200;i++){const value=await check();if(value)return value;await wait(100);}throw Error('WebRTC check timed out');}
  const endpoint=base.replace(/^http/,'ws').replace(/\/$/,'')+'/ws';const peers=[];
  async function peer(room){
   const ws=new WebSocket(endpoint),p={ws,ice:[]};peers.push(p);await new Promise((r,j)=>{ws.onopen=r;ws.onerror=j;});
   p.send=data=>ws.send(JSON.stringify(data));
   ws.onmessage=async e=>{const m=JSON.parse(e.data);if(m.type==='joined'){p.id=m.playerId;p.room=m.roomId;}if(m.type==='state')p.state=m;if(m.type==='error')p.error=m.message;
    if(m.type==='signal')try{if(m.data.candidate){if(p.pc.remoteDescription)await p.pc.addIceCandidate(m.data);else p.ice.push(m.data);}else{await p.pc.setRemoteDescription(m.data);for(const c of p.ice)await p.pc.addIceCandidate(c);p.ice=[];if(m.data.type==='offer'){await p.pc.setLocalDescription(await p.pc.createAnswer());p.send({type:'signal',target:m.from,data:p.pc.localDescription.toJSON()});}}}catch(e){p.error=e.message;}
   };
   p.send(room?{type:'join',roomId:room}:{type:'create'});await until(()=>p.id);return p;
  }
  try{
   const a=await peer(),b=await peer(a.room);
   for(const [p,other] of [[a,b],[b,a]]){
    p.pc=new RTCPeerConnection({iceServers:[]});p.pc.onicecandidate=e=>{if(e.candidate)p.send({type:'signal',target:other.id,data:e.candidate.toJSON()});};p.pc.ontrack=e=>{p.audio=new Audio();p.audio.srcObject=e.streams[0]||new MediaStream([e.track]);p.audio.play();};
    p.context=new AudioContext();const osc=p.context.createOscillator(),gain=p.context.createGain(),out=p.context.createMediaStreamDestination();gain.gain.value=0.02;osc.connect(gain).connect(out);osc.start();p.pc.addTrack(out.stream.getAudioTracks()[0],out.stream);p.send({type:'voice',enabled:true,ready:true});
   }
   await until(()=>a.state.players.every(p=>p.voiceReady)&&b.state.players.every(p=>p.voiceReady));
   await a.pc.setLocalDescription(await a.pc.createOffer());a.send({type:'signal',target:b.id,data:a.pc.localDescription.toJSON()});
   const packets=await until(async()=>{if(a.error||b.error)throw Error(a.error||b.error);const counts=[];for(const p of peers){let count=0;for(const stat of(await p.pc.getStats()).values())if(stat.type==='inbound-rtp'&&stat.kind==='audio')count+=stat.packetsReceived;counts.push(count);}return counts.every(n=>n>10)?counts:null;});
   return {ok:true,connections:peers.map(p=>p.pc.connectionState),receivedAudioPackets:packets};
  }finally{for(const p of peers){p.pc?.close();p.ws.close();await p.context?.close();}}
 }} )(${JSON.stringify(base.href)})`});
 if(result.exceptionDetails)throw Error(result.exceptionDetails.exception?.description||'Browser test failed');
 console.log(JSON.stringify(result.result.value));
 if(process.env.DASHBOARD_SCREENSHOT){const shot=await call('Page.captureScreenshot',{format:'png',captureBeyondViewport:true});await writeFile(process.env.DASHBOARD_SCREENSHOT,Buffer.from(shot.data,'base64'));}
}finally{cdp?.close();browser.kill('SIGTERM');await once(browser,'close');await rm(profile,{recursive:true,force:true});}
