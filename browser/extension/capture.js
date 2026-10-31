// Runs only in the private server extension page, never in a website's JS context.
const peers=new Map();let capture,activeTab,quality={width:1920,height:1080,fps:60,bitrate:20000000,deviceScale:1.5};
const emit=value=>globalThis.movieNightSignal?.(JSON.stringify(value));
async function startCapture(tabId){
 if(activeTab&&activeTab!==tabId)try{await chrome.debugger.sendCommand({tabId:activeTab},'Runtime.evaluate',{expression:'document.querySelectorAll("video,audio").forEach(v=>v.pause())'});}catch{}
 const id=await chrome.tabCapture.getMediaStreamId({targetTabId:tabId});
 const stream=await navigator.mediaDevices.getUserMedia({audio:{mandatory:{chromeMediaSource:'tab',chromeMediaSourceId:id}},video:{mandatory:{chromeMediaSource:'tab',chromeMediaSourceId:id,minWidth:quality.width,maxWidth:quality.width,minHeight:quality.height,maxHeight:quality.height,minFrameRate:quality.fps,maxFrameRate:quality.fps}}});
 for(const track of stream.getVideoTracks())track.contentHint='detail';
 for(const peer of peers.values())for(const sender of peer.pc.getSenders()){const track=stream.getTracks().find(t=>t.kind===sender.track?.kind);if(track)await sender.replaceTrack(track);}
 const old=capture;capture=stream;activeTab=tabId;old?.getTracks().forEach(t=>t.stop());return stream.getVideoTracks()[0].getSettings();
}
async function watch(id,iceServers){
 if(!capture)throw Error('Open a browser tab first');remove(id);
 const pc=new RTCPeerConnection({iceServers}),peer={pc,pending:[]};peers.set(id,peer);
 pc.onicecandidate=e=>{if(e.candidate)emit({target:id,data:e.candidate.toJSON()});};
 pc.onconnectionstatechange=()=>emit({target:id,status:pc.connectionState});
 for(const track of capture.getTracks()){
  const sender=pc.addTrack(track,capture);
  if(track.kind==='video'){
   const transceiver=pc.getTransceivers().find(t=>t.sender===sender),codecs=RTCRtpSender.getCapabilities('video').codecs;
   transceiver.setCodecPreferences([...codecs.filter(c=>c.mimeType==='video/H264'),...codecs.filter(c=>c.mimeType!=='video/H264')]);
   const params=sender.getParameters();params.encodings=[{maxBitrate:quality.bitrate,maxFramerate:quality.fps,scaleResolutionDownBy:1}];params.degradationPreference='maintain-resolution';await sender.setParameters(params);
  }
 }
 const offer=await pc.createOffer();offer.sdp=offer.sdp.replace(/(a=fmtp:\d+ [^\r\n]*packetization-mode[^\r\n]*)/g,'$1;x-google-start-bitrate=12000;x-google-max-bitrate='+Math.round(quality.bitrate/1000));await pc.setLocalDescription(offer);emit({target:id,data:pc.localDescription.toJSON()});
}
async function signal(id,data){const p=peers.get(id);if(!p)return;if(data.candidate){if(p.pc.remoteDescription)await p.pc.addIceCandidate(data);else p.pending.push(data);}else{await p.pc.setRemoteDescription(data);for(const candidate of p.pending)await p.pc.addIceCandidate(candidate);p.pending=[];}}
function remove(id){const p=peers.get(id);p?.pc.close();peers.delete(id);}
async function tabs(){return (await chrome.tabs.query({})).filter(t=>/^https?:/.test(t.url||'')).map(t=>({id:t.id,title:t.title,url:t.url,active:t.id===activeTab}));}
async function attach(tabId){
 try{await chrome.debugger.attach({tabId},'1.3');}catch(e){if(!String(e).includes('already attached'))throw e;}
 // Size the actual tab surface, not just its emulated viewport, to avoid capture letterboxing.
 await chrome.debugger.sendCommand({tabId},'Emulation.clearDeviceMetricsOverride');
 const {result}=await chrome.debugger.sendCommand({tabId},'Runtime.evaluate',{expression:'({x:outerWidth-innerWidth,y:outerHeight-innerHeight})',returnByValue:true});
 const tab=await chrome.tabs.get(tabId);await chrome.windows.update(tab.windowId,{state:'normal',left:0,top:0,width:quality.width+Math.max(0,result.value.x),height:quality.height+Math.max(0,result.value.y)});
 await chrome.debugger.sendCommand({tabId},'Emulation.setDeviceMetricsOverride',{width:Math.round(quality.width/quality.deviceScale),height:Math.round(quality.height/quality.deviceScale),deviceScaleFactor:quality.deviceScale,mobile:false});
}
async function open(url){if((await tabs()).length>=6)throw Error('Close a tab first (six tabs maximum)');const tab=await chrome.tabs.create({url,active:true});await attach(tab.id);await startCapture(tab.id);return tab.id;}
async function select(id){if(id===activeTab)return;await chrome.tabs.update(id,{active:true});await attach(id);await startCapture(id);}
async function close(id){await chrome.tabs.remove(id);if(id===activeTab){capture?.getTracks().forEach(t=>t.stop());capture=null;const next=await tabs();if(next.length)await select(next[0].id);else await open('https://web.stremio.com/');}}
async function input(message){if(!activeTab)return;const target={tabId:activeTab};
 if(message.action==='pointer'){const types=['mousePressed','mouseReleased','mouseMoved','mouseReleased'];await chrome.debugger.sendCommand(target,'Input.dispatchMouseEvent',{type:types[message.phase]||'mouseMoved',x:message.x*quality.width/quality.deviceScale,y:message.y*quality.height/quality.deviceScale,button:message.phase===2?'none':'left',buttons:message.phase===0||message.phase===2?1:0,clickCount:message.phase===2?0:1});}
 if(message.action==='scroll')await chrome.debugger.sendCommand(target,'Input.dispatchMouseEvent',{type:'mouseWheel',x:quality.width/(2*quality.deviceScale),y:quality.height/(2*quality.deviceScale),deltaX:0,deltaY:message.amount});
 if(message.action==='text')await chrome.debugger.sendCommand(target,'Input.insertText',{text:message.value});
 if(message.action==='reload')await chrome.tabs.reload(activeTab);
 if(message.action==='back')await chrome.tabs.goBack(activeTab);
 if(message.action==='forward')await chrome.tabs.goForward(activeTab);
 if(message.action==='navigate')await chrome.tabs.update(activeTab,{url:message.url});
}
async function stats(id){const p=peers.get(id);if(!p)return {};const reports=[...(await p.pc.getStats()).values()];return reports.filter(s=>s.type==='outbound-rtp').map(s=>({kind:s.kind,framesPerSecond:s.framesPerSecond,frameWidth:s.frameWidth,frameHeight:s.frameHeight,framesEncoded:s.framesEncoded,bytesSent:s.bytesSent,qualityLimitationReason:s.qualityLimitationReason}));}
globalThis.MovieNightCapture={open,select,close,tabs,input,watch,signal,remove,stats,startCapture,captureSettings:()=>capture?.getVideoTracks()[0].getSettings(),configure:value=>{quality={...quality,...value};}};
