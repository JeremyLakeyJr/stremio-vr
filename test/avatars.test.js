import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import {profile,Rooms} from '../server/rooms.js';
const ids=JSON.parse(fs.readFileSync(new URL('../server/avatar-catalog.json',import.meta.url)));
const catalog=JSON.parse(fs.readFileSync(new URL('../unity/Assets/Resources/Avatars/OpenSourceCatalog.json',import.meta.url)));
test('server and headset catalog identities and attribution agree',()=>{
 assert.deepEqual(ids,catalog.entries.map(a=>a.id));assert.equal(new Set(ids).size,ids.length);assert.ok(ids.length>300);
 for(const a of catalog.entries){assert.ok(a.creator&&a.name&&a.source);assert.ok(['CC0','CC-BY'].includes(a.license));assert.equal(new URL(a.url).protocol,'https:');assert.ok(a.source.includes(catalog.revision));}
});
test('profile preserves known avatars but never accepts model URLs or unknown IDs',()=>{
 assert.equal(profile({avatarId:ids[0]}).avatarId,ids[0]);
 for(const avatarId of ['https://127.0.0.1/private','../bad','unknown',{},null])assert.equal(profile({avatarId}).avatarId,undefined);
 assert.equal(profile({avatarUrl:'https://example.com/model.vrm'}).avatarUrl,undefined);
});
test('selected avatar syncs to guests and can switch back to R6',()=>{
 const rooms=new Rooms();const client=()=>({readyState:1,messages:[],send(data){this.messages.push(JSON.parse(data));}});const host=client(),guest=client();
 rooms.handle(host,{type:'create',profile:{avatarId:ids[0]}});rooms.handle(guest,{type:'join',roomId:host.roomId});
 assert.equal(guest.messages.filter(m=>m.type==='state').at(-1).players[0].profile.avatarId,ids[0]);
 rooms.handle(host,{type:'profile',profile:{name:'R6 again'}});
 assert.equal(guest.messages.filter(m=>m.type==='state').at(-1).players[0].profile.avatarId,undefined);
});
