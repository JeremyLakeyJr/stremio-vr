'use strict';
const emit=value=>window.movieNight.emit(value),peers=new Map();let mic=null,receiver=null,ice=[],deafened=false,generation=0;
const video=document.getElementById('video');let mediaUrl='',pendingSeek=null;
video.addEventListener('loadedmetadata',()=>{if(pendingSeek!==null){video.currentTime=pendingSeek;pendingSeek=null;}});
video.addEventListener('ended',()=>{if(mediaUrl)emit({kind:'media-ended'});});
video.addEventListener('error',()=>{if(mediaUrl)emit({kind:'status',message:'Video codec/source is unsupported. Use the room server transcoder or another source.'});});
setInterval(()=>{if(mediaUrl)emit({kind:'media-state',position:video.currentTime,duration:Number.isFinite(video.duration)?video.duration:0,prepared:video.readyState>=2,playing:!video.paused});},250);
function closePeer(peer){peer.pc.close();peer.audio?.remove();}
function closeVoice(){generation++;for(const peer of peers.values())closePeer(peer);peers.clear();mic?.getTracks().forEach(t=>t.stop());mic=null;}
async function applySignal(peer,data,reply){if(data.candidate){if(peer.pc.remoteDescription)await peer.pc.addIceCandidate(data);else if(peer.pending.length<128)peer.pending.push(data);return;}await peer.pc.setRemoteDescription(data);for(const candidate of peer.pending)await peer.pc.addIceCandidate(candidate);peer.pending=[];if(data.type==='offer'){await peer.pc.setLocalDescription(await peer.pc.createAnswer());reply(peer.pc.localDescription.toJSON());}}
function makePeer(reply){const peer={pc:new RTCPeerConnection({iceServers:ice,bundlePolicy:'max-bundle'}),pending:[]};peer.pc.onicecandidate=e=>{if(e.candidate)reply(e.candidate.toJSON());};peer.pc.onconnectionstatechange=()=>{if(peer.pc.connectionState==='failed')emit({kind:'status',message:'WebRTC connection failed. Check the room TURN settings.'});};return peer;}
window.movieNight.receive(async command=>{try{
 switch(command.method){
 case 'voice-start':{closeVoice();const run=generation;ice=command.ice||[];const stream=await navigator.mediaDevices.getUserMedia({audio:{echoCancellation:true,noiseSuppression:true,autoGainControl:true},video:false});if(run!==generation){stream.getTracks().forEach(t=>t.stop());return;}mic=stream;emit({kind:'voice-ready'});break;}
 case 'voice-connect':{if(!mic||peers.has(command.id))break;const reply=data=>emit({kind:'voice-signal',to:command.id,data});const peer=makePeer(reply);peers.set(command.id,peer);for(const track of mic.getTracks())peer.pc.addTrack(track,mic);peer.pc.ontrack=e=>{const audio=document.createElement('audio');audio.autoplay=true;audio.muted=deafened;audio.srcObject=e.streams[0]||new MediaStream([e.track]);document.body.append(audio);peer.audio=audio;audio.play().catch(()=>{});};if(command.offer){await peer.pc.setLocalDescription(await peer.pc.createOffer());reply(peer.pc.localDescription.toJSON());}break;}
 case 'voice-signal':{const peer=peers.get(command.id);if(peer)await applySignal(peer,command.data,data=>emit({kind:'voice-signal',to:command.id,data}));break;}
 case 'voice-remove':{const peer=peers.get(command.id);if(peer)closePeer(peer);peers.delete(command.id);break;}
 case 'voice-mute':mic?.getAudioTracks().forEach(t=>t.enabled=!command.value);break;
 case 'voice-deafen':deafened=command.value;for(const peer of peers.values())if(peer.audio)peer.audio.muted=deafened;break;
 case 'voice-close':closeVoice();break;
 case 'receiver-start':mediaUrl='';video.style.objectFit='fill';video.pause();video.removeAttribute('src');video.load();video.srcObject=null;if(receiver)closePeer(receiver);ice=command.ice||[];receiver=makePeer(data=>emit({kind:'browser-signal',data}));receiver.pc.ontrack=e=>{if(!video.srcObject)video.srcObject=new MediaStream();video.srcObject.addTrack(e.track);video.play().catch(()=>{});};emit({kind:'receiver-ready'});break;
 case 'receiver-signal':if(receiver)await applySignal(receiver,command.data,data=>emit({kind:'browser-signal',data}));break;
 case 'receiver-volume':video.volume=Math.max(0,Math.min(1,command.value));break;
 case 'media-sync':{if(receiver){closePeer(receiver);receiver=null;}video.srcObject=null;video.style.objectFit='contain';if(mediaUrl!==command.url){mediaUrl=command.url;pendingSeek=command.position;video.src=command.url;}else if(command.seek||(!command.solo&&Math.abs(video.currentTime-command.position)>(command.paused?.1:.75))){if(video.readyState>=1)video.currentTime=command.position;else pendingSeek=command.position;}video.volume=command.volume;if(command.paused)video.pause();else await video.play();break;}
 case 'media-pause':video.pause();break;
 case 'media-stop':mediaUrl='';video.pause();video.removeAttribute('src');video.load();break;
 case 'media-track':{const tracks=video.audioTracks;if(tracks&&tracks.length>1){let index=0;for(let i=0;i<tracks.length;i++)if(tracks[i].enabled)index=i;for(let i=0;i<tracks.length;i++)tracks[i].enabled=i===(index+1)%tracks.length;}else emit({kind:'status',message:'This source exposes one desktop audio track. Select another source for a different language.'});break;}
 case 'receiver-close':if(receiver)closePeer(receiver);receiver=null;video.srcObject=null;break;
 }
}catch(error){emit({kind:command.method==='voice-start'?'voice-error':'status',message:command.method==='voice-start'?'Microphone could not start. Check your desktop microphone permissions.':error.message});}});
emit({kind:'rtc-ready'});

setInterval(async()=>{if(!receiver)return;let videoFrames=0,audioPackets=0,fps=0;try{for(const report of (await receiver.pc.getStats()).values())if(report.type==='inbound-rtp'){if(report.kind==='video'){videoFrames=report.framesDecoded||0;fps=report.framesPerSecond||0;}if(report.kind==='audio')audioPackets=report.packetsReceived||0;}emit({kind:'receiver-stats',videoFrames,audioPackets,fps});}catch{}},2000);

setInterval(async()=>{let audioPackets=0;try{for(const peer of peers.values())for(const report of (await peer.pc.getStats()).values())if(report.type==='inbound-rtp'&&report.kind==='audio')audioPackets+=report.packetsReceived||0;if(peers.size)emit({kind:'voice-stats',audioPackets});}catch{}},2000);
