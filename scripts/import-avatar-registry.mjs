// Regenerate the pinned, compact catalog from a checkout of ToxSam/open-source-avatars.
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
const root=process.argv[2];
if(!root)throw Error('Usage: node scripts/import-avatar-registry.mjs /path/to/open-source-avatars');
const revision=execFileSync('git',['-C',root,'rev-parse','HEAD'],{encoding:'utf8'}).trim();
const projects=JSON.parse(fs.readFileSync(path.join(root,'data/projects.json')));
const entries=[];
for(const project of projects){
 if(!project.is_public||!['CC0','CC-BY'].includes(project.license))continue;
 if(!/^avatars\/[\w-]+\.json$/.test(project.avatar_data_file))throw Error('Unexpected collection path');
 for(const avatar of JSON.parse(fs.readFileSync(path.join(root,'data',project.avatar_data_file)))){
  if(!avatar.is_public||avatar.is_draft||avatar.format!=='VRM')continue;
  const url=new URL(avatar.model_file_url);if(url.protocol!=='https:'||url.username||url.password)continue;
  entries.push({id:project.id+':'+avatar.id,name:avatar.name,collection:project.name,creator:project.creator_id,license:project.license,url:url.href,thumbnail:avatar.thumbnail_url||'',source:`https://github.com/ToxSam/open-source-avatars/blob/${revision}/data/${project.avatar_data_file}`});
 }
}
entries.sort((a,b)=>a.collection.localeCompare(b.collection)||a.name.localeCompare(b.name));
if(new Set(entries.map(a=>a.id)).size!==entries.length)throw Error('Duplicate avatar IDs');
const result=JSON.stringify({revision,entries},null,2)+'\n';
fs.mkdirSync('unity/Assets/Resources/Avatars',{recursive:true});
fs.writeFileSync('unity/Assets/Resources/Avatars/OpenSourceCatalog.json',result);
fs.writeFileSync('server/avatar-catalog.json',JSON.stringify(entries.map(a=>a.id))+'\n');
fs.copyFileSync(path.join(root,'LICENSE'),'third_party/open-source-avatars/LICENSE');
fs.writeFileSync('third_party/open-source-avatars/README.md',`# Open Source Avatars\n\nRegistry: https://github.com/ToxSam/open-source-avatars\n\nPinned revision: ${revision}\n\nMetadata is CC0. Individual avatar licenses are retained per collection in the generated catalog. Creator, license and source are displayed in Avatar studio. Models are downloaded directly from the registry URLs, not bundled or relicensed under AGPL.\n\nRefresh with \`node scripts/import-avatar-registry.mjs /path/to/registry-checkout\`. Review license changes and run tests before shipping the updated catalog and server together.\n`);
console.log(`Imported ${entries.length} avatars at ${revision}`);
