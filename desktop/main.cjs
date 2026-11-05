'use strict';
const {app,BrowserWindow,ipcMain,session,safeStorage}=require('electron');
const fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto');
const {WebSocketServer}=require('ws');const {SoloRuntime}=require('./solo.cjs');
const token=process.env.MOVIE_NIGHT_TOKEN,dataPath=process.env.MOVIE_NIGHT_DATA,port=Number(process.env.MOVIE_NIGHT_PORT);
if(!/^[a-f0-9]{64}$/.test(token||'')||!dataPath||!Number.isInteger(port)||port<1024||port>65535)throw Error('Launch this runtime through Movie Night');
if(process.env.MOVIE_NIGHT_TEST_MEDIA==='1'){app.commandLine.appendSwitch('use-fake-device-for-media-stream');app.commandLine.appendSwitch('use-fake-ui-for-media-stream');}
if(process.env.MOVIE_NIGHT_BROWSER_SOFTWARE==='1')app.disableHardwareAcceleration();
if(process.platform==='linux')app.commandLine.appendSwitch('ozone-platform','x11');
app.setPath('userData',path.join(dataPath,'desktop-browser'));app.commandLine.appendSwitch('autoplay-policy','no-user-gesture-required');app.commandLine.appendSwitch('force-color-profile','srgb');app.commandLine.appendSwitch('force-device-scale-factor','1');
let socket,server,solo,rtc,rtcReady=false,rtcQueue=[],tabs=[],active=-1,nextId=0,receiverMode=false;
let webPartition='movie-night-web-session';
const WIDTH=1920,HEIGHT=1080;
function send(value){if(socket?.readyState===1)socket.send(JSON.stringify(value));}
function frame(window,image){if(socket?.readyState!==1||socket.bufferedAmount>WIDTH*HEIGHT*4)return;if(receiverMode?window!==rtc:window!==tabs[active]?.window)return;const size=image.getSize();if(size.width!==WIDTH||size.height!==HEIGHT){console.warn('Unexpected offscreen size',size);return;}const header=Buffer.alloc(8);header.writeUInt32LE(size.width);header.writeUInt32LE(size.height,4);socket.send(header,{fin:false});socket.send(image.toBitmap(),{fin:true});}
function rtcCommand(value){if(!rtcReady)rtcQueue.push(value);else rtc.webContents.send('movie-night-command',value);}
function url(value){const result=new URL(value||'https://web.stremio.com/');if(!['https:','http:'].includes(result.protocol)||result.username||result.password)throw Error('Use an HTTP(S) browser address');return result.href;}
function listTabs(){send({kind:'browser-tabs',tabs:tabs.map((t,i)=>({id:t.id,title:t.window.webContents.getTitle()||'New tab',url:t.window.webContents.getURL(),active:i===active}))});}
function select(index){if(index<0||index>=tabs.length)return;active=index;for(let i=0;i<tabs.length;i++){tabs[i].window.webContents.setAudioMuted(i!==active);if(i===active&&!receiverMode){tabs[i].window.webContents.startPainting();tabs[i].window.webContents.invalidate();}else tabs[i].window.webContents.stopPainting();}listTabs();}
function newTab(value){if(tabs.length>=6)throw Error('Close a tab before opening another');const window=new BrowserWindow({width:WIDTH,height:HEIGHT,useContentSize:true,frame:false,show:false,webPreferences:{offscreen:true,nodeIntegration:false,contextIsolation:true,sandbox:true,partition:webPartition,backgroundThrottling:false}});window.setContentSize(WIDTH,HEIGHT);window.webContents.setFrameRate(60);window.webContents.on('paint',(_event,_dirty,image)=>frame(window,image));window.webContents.setWindowOpenHandler(({url:target})=>{try{newTab(url(target));}catch{}return {action:'deny'};});window.webContents.on('will-navigate',(event,target)=>{try{url(target);}catch{event.preventDefault();}});window.webContents.on('will-redirect',(event,target)=>{try{url(target);}catch{event.preventDefault();}});window.webContents.on('page-title-updated',listTabs);window.webContents.on('did-navigate',listTabs);window.webContents.on('render-process-gone',()=>send({kind:'status',message:'Browser tab stopped; close it and open another.'}));tabs.push({id:String(++nextId),window});select(tabs.length-1);window.loadURL(url(value)).catch(()=>send({kind:'status',message:'Browser page could not load'}));}
async function browser(method,args=[]){let web=tabs[active]?.window.webContents;switch(method){
 case 'open':receiverMode=false;rtcCommand({method:'receiver-close'});rtcCommand({method:'media-pause'});rtc.webContents.stopPainting();if(!tabs.length)newTab(args[0]);else select(active);break;
 case 'close':for(const tab of tabs)tab.window.destroy();tabs=[];active=-1;listTabs();break;
 case 'newTab':newTab(args[0]);break;case 'selectTab':select(args[0]);break;
 case 'closeTab':if(tabs[args[0]]){tabs.splice(args[0],1)[0].window.destroy();if(tabs.length)select(Math.min(active,tabs.length-1));else newTab();}break;
 case 'navigate':if(web)await web.loadURL(url(args[0]));break;
 case 'back':if(web?.navigationHistory.canGoBack())web.navigationHistory.goBack();break;case 'forward':if(web?.navigationHistory.canGoForward())web.navigationHistory.goForward();break;case 'reload':web?.reload();break;
 case 'text':web?.insertText(String(args[0]).slice(0,8192));break;
 case 'pointer':if(web){const x=Math.round(Math.max(0,Math.min(1,args[0]))*(WIDTH-1)),y=Math.round(Math.max(0,Math.min(1,args[1]))*(HEIGHT-1));const phase=args[2];web.sendInputEvent({type:'mouseMove',x,y});if(phase===0||phase===1||phase===3)web.sendInputEvent({type:phase===0?'mouseDown':'mouseUp',x,y,button:'left',clickCount:1});}break;
 case 'scroll':web?.sendInputEvent({type:'mouseWheel',x:WIDTH/2,y:HEIGHT/2,deltaY:Number(args[0]),deltaX:0,canScroll:true});break;
 case 'forgetSession':await session.fromPartition(webPartition).clearStorageData();web?.reload();break;
 case 'shareVideo':if(web){const data=await web.executeJavaScript("(()=>{const v=document.querySelector('video');return v?JSON.stringify({url:v.currentSrc,title:document.title,position:v.currentTime,paused:v.paused}):null})()");send({kind:'browser-video',value:JSON.stringify(data)});}break;
 }}
function vault(command){const name=command.name;if(!/^[a-z0-9-]{1,64}$/.test(name))throw Error('Invalid vault key');const file=path.join(dataPath,'vault',name+'.bin');const available=safeStorage.isEncryptionAvailable()&&!(process.platform==='linux'&&safeStorage.getSelectedStorageBackend()==='basic_text');if(command.method==='remove'){if(fs.existsSync(file))fs.unlinkSync(file);return;}if(!available){send({kind:'vault',name,value:null,available:false});return;}if(command.method==='save'){fs.mkdirSync(path.dirname(file),{recursive:true,mode:0o700});fs.writeFileSync(file,safeStorage.encryptString(String(command.value)),{mode:0o600});}else {let value=null;try{if(fs.existsSync(file))value=safeStorage.decryptString(fs.readFileSync(file));}catch{send({kind:'status',message:'Saved sign-in could not be decrypted. Sign in again on this computer.'});}send({kind:'vault',name,value,available:true});}}
async function command(data){switch(data.op){
 case 'solo-start':solo.start(data.initial);break;case 'solo-send':solo.command(data.data);break;case 'solo-close':solo.close();break;
 case 'browser':await browser(data.method,data.args);break;
 case 'rtc':if(data.method==='receiver-start'||data.method==='media-sync'){receiverMode=true;for(const t of tabs){t.window.webContents.setAudioMuted(true);t.window.webContents.stopPainting();}rtc.webContents.startPainting();}if(data.method==='receiver-close'){receiverMode=false;rtc.webContents.stopPainting();}rtcCommand(data);break;
 case 'vault':vault(data);break;
 case 'quit':app.quit();break;
 }}
app.whenReady().then(()=>{
 if(safeStorage.isEncryptionAvailable()&&!(process.platform==='linux'&&safeStorage.getSelectedStorageBackend()==='basic_text'))webPartition='persist:movie-night-web';
 const webSession=session.fromPartition(webPartition);webSession.setPermissionRequestHandler((_wc,_permission,callback)=>callback(false));webSession.setPermissionCheckHandler(()=>false);
 const internal=session.fromPartition('movie-night-internal');internal.setPermissionRequestHandler((wc,permission,callback)=>callback(wc===rtc?.webContents&&permission==='media'));internal.setPermissionCheckHandler((wc,permission)=>wc===rtc?.webContents&&permission==='media');
 rtc=new BrowserWindow({width:WIDTH,height:HEIGHT,useContentSize:true,frame:false,show:false,webPreferences:{offscreen:true,nodeIntegration:false,contextIsolation:true,sandbox:true,preload:path.join(__dirname,'preload.cjs'),partition:'movie-night-internal',backgroundThrottling:false}});rtc.setContentSize(WIDTH,HEIGHT);rtc.webContents.setFrameRate(60);rtc.webContents.on('paint',(_e,_dirty,image)=>frame(rtc,image));rtc.webContents.setWindowOpenHandler(()=>({action:'deny'}));rtc.webContents.on('will-navigate',e=>e.preventDefault());
 ipcMain.on('movie-night-rtc',(event,value)=>{if(event.sender!==rtc.webContents||event.senderFrame!==rtc.webContents.mainFrame)return;if(value.kind==='rtc-ready'){rtcReady=true;for(const item of rtcQueue)rtcCommand(item);rtcQueue=[];rtc.webContents.stopPainting();}else send(value);});rtc.loadFile(path.join(__dirname,'rtc.html'));
 const rules=fs.readFileSync(process.env.MOVIE_NIGHT_SOLO||path.join(__dirname,'solo.js'),'utf8');solo=new SoloRuntime(rules,value=>send({kind:'solo',value}));
 server=new WebSocketServer({host:'127.0.0.1',port,maxPayload:131072,verifyClient:info=>{const provided=new URL(info.req.url,'http://localhost').searchParams.get('token')||'';return !info.origin&&provided.length===token.length&&crypto.timingSafeEqual(Buffer.from(provided),Buffer.from(token))&&!socket;}});
 server.on('connection',client=>{socket=client;send({kind:'ready'});client.on('message',(bytes,binary)=>{if(binary)return;try{const data=JSON.parse(bytes);Promise.resolve(command(data)).catch(error=>send({kind:'status',message:error.message}));}catch{send({kind:'status',message:'Invalid desktop command'});}});client.on('close',()=>app.quit());});server.on('error',()=>app.quit());
 setTimeout(()=>{if(!socket)app.quit();},30000).unref();
});
app.on('window-all-closed',()=>{});app.on('before-quit',()=>{solo?.close();server?.close();});
