import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { hostingInfo } from '../server/hosting.js';
test('share page accepts only fresh Cloudflare tunnel URLs',async t=>{
 const dir=await mkdtemp(join(tmpdir(),'movie-night-hosting-'));t.after(()=>rm(dir,{recursive:true,force:true}));const path=join(dir,'link.json');
 for(const data of [{url:'https://api.trycloudflare.com',updatedAt:Date.now()},{url:'https://example.com',updatedAt:Date.now()},{url:'https://valid-name.trycloudflare.com',updatedAt:Date.now()-30000},{url:'https://valid-name.trycloudflare.com',connected:true,updatedAt:Date.now()-30000},{url:'javascript:alert(1)',updatedAt:Date.now()}]){await writeFile(path,JSON.stringify(data));assert.equal((await hostingInfo(path)).online,false);}
 await writeFile(path,JSON.stringify({url:'https://valid-name.trycloudflare.com',updatedAt:Date.now()}));assert.equal((await hostingInfo(path)).url,'https://valid-name.trycloudflare.com');
});

test('Docker external tunnel status remains visible when the internal manager is idle', async t => {
 const dir=await mkdtemp(join(tmpdir(),'movie-night-external-tunnel-'));
 const previous=process.env.TUNNEL_EXTERNAL_LINK_FILE;
 t.after(async()=>{if(previous===undefined)delete process.env.TUNNEL_EXTERNAL_LINK_FILE;else process.env.TUNNEL_EXTERNAL_LINK_FILE=previous;await rm(dir,{recursive:true,force:true});});
 const external=join(dir,'external.json'),internal=join(dir,'internal.json');
 await writeFile(external,JSON.stringify({url:'https://docker-invite.trycloudflare.com',updatedAt:Date.now(),connected:true}));
 await writeFile(internal,JSON.stringify({url:'',running:false}));
 process.env.TUNNEL_EXTERNAL_LINK_FILE=external;
 assert.equal((await hostingInfo(internal,{getStatus:()=>({hasToken:false})})).url,'https://docker-invite.trycloudflare.com');
});
