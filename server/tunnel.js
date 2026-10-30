import { spawn } from 'node:child_process';
import { writeFile, rename, rm } from 'node:fs/promises';
const file=process.env.TUNNEL_LINK_FILE||'/share/link.json';
let url='',stopping=false,writing=false;
await rm(file,{force:true});
async function publish(){if(!url||writing||stopping)return;writing=true;try{await writeFile(file+'.tmp',JSON.stringify({url,updatedAt:Date.now()}),{mode:0o600});await rename(file+'.tmp',file);}finally{writing=false;}}
const child=spawn('/usr/local/bin/cloudflared',['tunnel','--no-autoupdate','--url',process.env.TUNNEL_TARGET||'http://cinema:3000'],{stdio:['ignore','pipe','pipe']});
function output(stream){let buffer='';stream.on('data',chunk=>{buffer+=chunk;const lines=buffer.split('\n');buffer=lines.pop();for(const line of lines){const found=line.match(/https:\/\/[a-z0-9-]+\.trycloudflare\.com/);if(found&&found[0]!=="https://api.trycloudflare.com"&&url!==found[0]){url=found[0];console.log('\nMovie Night is online. SHARE WITH FRIENDS: '+url+'\nOpen http://localhost:3000 for the copy button.');publish().catch(()=>{});}else if(/ERR|error|failed/i.test(line))console.error(line);}});}
output(child.stdout);output(child.stderr);
const timer=setInterval(()=>publish().catch(()=>{}),5000);
async function stop(){if(stopping)return;stopping=true;clearInterval(timer);child.kill('SIGTERM');while(writing)await new Promise(resolve=>setTimeout(resolve,10));await rm(file,{force:true});}
child.on('error',async()=>{await stop();process.exit(1);});child.on('close',async code=>{await stop();process.exit(code||0);});for(const signal of ['SIGTERM','SIGINT'])process.on(signal,stop);
