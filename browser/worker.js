import http from 'node:http';import {BrowserSession} from './session.js';import {BrowserProfiles} from './profiles.js';import {browserProxy} from './proxy.js';import {mediaURL} from '../server/rooms.js';
const profiles=new BrowserProfiles(),sessions=new Map(),proxy=browserProxy();await new Promise(r=>proxy.server.listen(0,'127.0.0.1',r));
const proxyUrl='http://127.0.0.1:'+proxy.server.address().port;
let lifecycle=Promise.resolve();
function exclusive(action){const task=lifecycle.catch(()=>{}).then(action);lifecycle=task;return task;}
const maxSessions=Number(process.env.BROWSER_MAX_SESSIONS||2);
async function stop(id,forget=false){const s=sessions.get(id);if(!s)return;sessions.delete(id);await s.browser.close();if(forget){await profiles.forget(s.secret);await import('node:fs/promises').then(fs=>fs.rm(s.profile,{recursive:true,force:true}));}else await profiles.save(s.secret,s.profile);}
async function start(id,data){
 if(!/^[a-f0-9]{12}$/.test(id))throw Error('Invalid room');profiles.key(data.profileKey);
 for(const [other,s] of sessions)if(other===id||s.secret===data.profileKey)await stop(other);
 if(sessions.size>=maxSessions)throw Error('Server browser capacity reached');
 const profile=await profiles.open(data.profileKey),browser=new BrowserSession();const s={browser,profile,secret:data.profileKey,events:[],last:Date.now(),queue:Promise.resolve()};sessions.set(id,s);
 browser.on('signal',event=>{if(s.events.length<256)s.events.push(event);});browser.on('closed',()=>{s.events.push({closed:true});});
 try{await browser.start({profile,proxy:proxyUrl});await browser.call('open',mediaURL(data.url||'https://web.stremio.com/'));return {tabs:await browser.call('tabs'),width:1920,height:1080,fps:60};}catch(error){await stop(id);throw error;}
}
async function action(s,data){
 switch(data.action){
  case 'open-tab':return s.browser.call('open',mediaURL(data.url));
  case 'select-tab':return s.browser.call('select',data.id);
  case 'close-tab':return s.browser.call('close',data.id);
  case 'navigate':return s.browser.call('input',{...data,url:mediaURL(data.url)});
  case 'pointer':if(![0,1,2,3].includes(data.phase)||![data.x,data.y].every(n=>Number.isFinite(n)&&n>=0&&n<=1))throw Error('Invalid pointer');break;
  case 'scroll':if(!Number.isFinite(data.amount)||Math.abs(data.amount)>2000)throw Error('Invalid scroll');break;
  case 'text':if(typeof data.value!=='string'||data.value.length>4096)throw Error('Invalid text');break;
  case 'reload':case 'back':case 'forward':break;
  default:throw Error('Unknown browser action');
 }
 return s.browser.call('input',data);
}
const server=http.createServer(async(req,res)=>{
 res.setHeader('Content-Type','application/json');try{
  if(req.url==='/health'){res.end(JSON.stringify({ok:true}));return;}
  const match=/^\/sessions\/([a-f0-9]{12})(?:\/(events|action|watch|signal|remove|tabs|forget|stats))?$/.exec(req.url);if(!match)throw Error('Unknown browser endpoint');const [,id,operation]=match;
  let raw='';for await(const chunk of req){raw+=chunk;if(raw.length>32768)throw Error('Browser request too large');}const data=raw?JSON.parse(raw):{};
  if(req.method==='POST'&&!operation){res.end(JSON.stringify(await exclusive(()=>start(id,data))));return;}
  if(req.method==='DELETE'){await exclusive(()=>stop(id));res.end('{}');return;}
  const s=sessions.get(id);if(!s){res.writeHead(404);res.end('{"error":"Browser session ended"}');return;}s.last=Date.now();let result={};
  if(operation!=='events'&&operation!=='tabs'&&operation!=='stats'&&req.method!=='POST')throw Error('Use POST');
  if(operation==='events'){result={events:s.events.splice(0)};}
  else if(operation==='tabs')result={tabs:await s.browser.call('tabs')};
  else if(operation==='watch')await s.browser.call('watch',data.playerId,data.iceServers||[]);
  else if(operation==='signal')await s.browser.call('signal',data.playerId,data.data);
  else if(operation==='remove')await s.browser.call('remove',data.playerId);
  else if(operation==='stats')result={stats:await s.browser.call('stats',data.playerId)};
  else if(operation==='forget'){await exclusive(()=>stop(id,true));}
  else if(operation==='action'){s.queue=s.queue.catch(()=>{}).then(()=>action(s,data));await s.queue;result=['pointer','scroll'].includes(data.action)?{}:{tabs:await s.browser.call('tabs')};}
  else throw Error('Unknown browser operation');res.end(JSON.stringify(result));
 }catch(error){res.writeHead(400);res.end(JSON.stringify({error:error.message}));}
});
server.listen(Number(process.env.PORT||9000),'0.0.0.0');
const timer=setInterval(()=>{for(const [id,s] of sessions)if(Date.now()-s.last>30000)exclusive(()=>stop(id)).catch(()=>{});},5000);timer.unref();
for(const signal of ['SIGINT','SIGTERM'])process.on(signal,async()=>{clearInterval(timer);for(const id of [...sessions.keys()])await exclusive(()=>stop(id)).catch(()=>{});await proxy.close();server.close(()=>process.exit(0));});
