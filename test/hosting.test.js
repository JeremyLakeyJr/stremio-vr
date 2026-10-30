import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { hostingInfo } from '../server/hosting.js';
test('share page accepts only fresh Cloudflare tunnel URLs',async t=>{
 const dir=await mkdtemp(join(tmpdir(),'movie-night-hosting-'));t.after(()=>rm(dir,{recursive:true,force:true}));const path=join(dir,'link.json');
 for(const data of [{url:'https://api.trycloudflare.com',updatedAt:Date.now()},{url:'https://example.com',updatedAt:Date.now()},{url:'https://valid-name.trycloudflare.com',updatedAt:Date.now()-30000},{url:'javascript:alert(1)',updatedAt:Date.now()}]){await writeFile(path,JSON.stringify(data));assert.equal((await hostingInfo(path)).online,false);}
 await writeFile(path,JSON.stringify({url:'https://valid-name.trycloudflare.com',updatedAt:Date.now()}));assert.equal((await hostingInfo(path)).url,'https://valid-name.trycloudflare.com');
});
