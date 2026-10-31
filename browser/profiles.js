import {createCipheriv,createDecipheriv,createHash,randomBytes} from 'node:crypto';
import {mkdir,mkdtemp,readFile,writeFile,rename,rm,stat} from 'node:fs/promises';
import {join} from 'node:path';import {tmpdir} from 'node:os';import {spawn} from 'node:child_process';
async function tar(args){await new Promise((resolve,reject)=>{const p=spawn('tar',args,{stdio:'ignore'});p.once('error',reject);p.once('exit',code=>code===0?resolve():reject(Error('Browser profile archive failed')));});}
export class BrowserProfiles {
 constructor(folder=process.env.BROWSER_PROFILE_DIR||'/data/profiles'){this.folder=folder;}
 key(secret){if(!/^[a-f0-9]{64}$/.test(secret||''))throw Error('Invalid browser profile key');return Buffer.from(secret,'hex');}
 id(secret){return createHash('sha256').update(this.key(secret)).digest('hex');}
 async open(secret){
  const key=this.key(secret);await mkdir(this.folder,{recursive:true,mode:0o700});const profile=await mkdtemp(join(tmpdir(),'movie-night-profile-'));
  try{
   const path=join(this.folder,this.id(secret)+'.enc');let data;try{const info=await stat(path);if(info.size>256*1024**2)throw Error('Browser profile too large');data=await readFile(path);}catch(error){if(error.code==='ENOENT')return profile;throw error;}
   const cipher=createDecipheriv('aes-256-gcm',key,data.subarray(0,12));cipher.setAuthTag(data.subarray(12,28));const body=Buffer.concat([cipher.update(data.subarray(28)),cipher.final()]);
   const archive=profile+'.tgz';await writeFile(archive,body,{mode:0o600});try{await tar(['-xzf',archive,'--no-same-owner','-C',profile]);}finally{await rm(archive,{force:true});}return profile;
  }catch(error){await rm(profile,{recursive:true,force:true});throw error;}
 }
 async save(secret,profile){
  const archive=profile+'.tgz';
  try{
   await tar(['-czf',archive,'--exclude=Singleton*','--exclude=DevToolsActivePort','--exclude=Cache','--exclude=Code Cache','--exclude=GPUCache','-C',profile,'.']);
   const info=await stat(archive);if(info.size>256*1024**2)throw Error('Browser profile too large');
   const key=this.key(secret),iv=randomBytes(12),cipher=createCipheriv('aes-256-gcm',key,iv);const ciphertext=Buffer.concat([cipher.update(await readFile(archive)),cipher.final()]);
   const path=join(this.folder,this.id(secret)+'.enc');await writeFile(path+'.tmp',Buffer.concat([iv,cipher.getAuthTag(),ciphertext]),{mode:0o600});await rename(path+'.tmp',path);
  }finally{await rm(archive,{force:true});await rm(profile,{recursive:true,force:true});}
 }
 async forget(secret){await rm(join(this.folder,this.id(secret)+'.enc'),{force:true});}
}
