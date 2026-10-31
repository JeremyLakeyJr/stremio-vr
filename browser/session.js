import {spawn} from 'node:child_process';
import {readFile,mkdtemp,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join,resolve} from 'node:path';
import {fileURLToPath} from 'node:url';
import {EventEmitter} from 'node:events';
import {Cdp} from './cdp.js';
const extension=fileURLToPath(new URL('./extension',import.meta.url));
const extensionId=(await readFile(new URL('./extension-id.txt',import.meta.url),'utf8')).trim();
export class BrowserSession extends EventEmitter {
 async start(options={}){
  this.profile=options.profile||await mkdtemp(join(tmpdir(),'movie-night-browser-'));this.temporary=!options.profile;
  const args=['--headless=new','--silent-debugger-extension-api','--no-sandbox','--disable-dev-shm-usage','--no-first-run','--no-default-browser-check','--disable-background-timer-throttling','--disable-renderer-backgrounding','--autoplay-policy=no-user-gesture-required','--auto-accept-this-tab-capture','--allowlisted-extension-id='+extensionId,'--disable-extensions-except='+extension,'--load-extension='+extension,'--screen-info={0,0 1920x1200}','--window-size=1920,1167','--remote-debugging-port=0','--user-data-dir='+this.profile,'about:blank'];
  if(options.proxy)args.push('--proxy-server='+options.proxy,'--proxy-bypass-list=<-loopback>');
  this.process=spawn(process.env.CHROMIUM||'chromium',args,{stdio:['ignore','ignore','pipe']});this.process.stderr.on('data',()=>{});
  this.process.on('error',error=>{this.startError=error;});this.process.on('exit',()=>this.emit('closed'));
  let port;for(let i=0;i<150;i++){if(this.startError)throw this.startError;if(this.process.exitCode!==null)throw Error('Chromium failed to start');try{port=Number((await readFile(join(this.profile,'DevToolsActivePort'),'utf8')).split('\n')[0]);break;}catch{await new Promise(r=>setTimeout(r,100));}}
  if(!port)throw Error('Chromium startup timed out');this.base='http://127.0.0.1:'+port;
  const version=await(await fetch(this.base+'/json/version')).json();this.browser=await Cdp.connect(version.webSocketDebuggerUrl);
  const {targetId}=await this.browser.call('Target.createTarget',{url:'chrome-extension://'+extensionId+'/capture.html'});
  let target;for(let i=0;i<50;i++){target=(await(await fetch(this.base+'/json')).json()).find(t=>t.id===targetId);if(target?.webSocketDebuggerUrl)break;await new Promise(r=>setTimeout(r,100));}
  this.page=await Cdp.connect(target.webSocketDebuggerUrl);await this.page.call('Runtime.enable');await this.page.call('Runtime.addBinding',{name:'movieNightSignal'});
  this.page.on('Runtime.bindingCalled',event=>{if(event.name==='movieNightSignal')try{this.emit('signal',JSON.parse(event.payload));}catch{}});
  for(let i=0;i<50;i++){if(await this.page.evaluate('!!globalThis.MovieNightCapture'))return this;await new Promise(r=>setTimeout(r,100));}throw Error('Capture extension failed to load');
 }
 call(method,...args){if(!/^[a-zA-Z]+$/.test(method))throw Error('Invalid browser action');return this.page.evaluate('MovieNightCapture.'+method+'('+args.map(a=>JSON.stringify(a)).join(',')+')');}
 async close(){
  const wait=async ms=>{if(this.process?.exitCode===null&&this.process?.signalCode===null)await Promise.race([new Promise(r=>this.process.once('exit',r)),new Promise(r=>setTimeout(r,ms))]);};
  try{await this.browser?.call('Browser.close');}catch{}await wait(5000);this.page?.close();this.browser?.close();
  if(this.process?.exitCode===null&&this.process?.signalCode===null){this.process.kill('SIGTERM');await wait(2000);}
  if(this.process?.exitCode===null&&this.process?.signalCode===null){this.process.kill('SIGKILL');await wait(2000);}
  if(this.temporary&&this.profile)await rm(this.profile,{recursive:true,force:true});
 }
}
