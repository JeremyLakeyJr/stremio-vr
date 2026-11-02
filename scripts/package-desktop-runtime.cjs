// Bundle a pinned Electron binary and only the runtime's production code/dependencies.
const fs=require('node:fs/promises'),path=require('node:path');
const localRequire=require('node:module').createRequire(path.resolve('desktop/package.json'));const {downloadArtifact}=localRequire('@electron/get');const {extract}=localRequire('@electron-internal/extract-zip');
(async()=>{const platform=process.argv[2],destination=path.resolve(process.argv[3]||'');if(!['win32','linux'].includes(platform)||!process.argv[3])throw Error('Usage: node scripts/package-desktop-runtime.cjs win32|linux OUTPUT_DIRECTORY');
const pkg=require('../desktop/node_modules/electron/package.json');const zip=await downloadArtifact({version:pkg.version,artifactName:'electron',platform,arch:'x64',checksums:require('../desktop/node_modules/electron/checksums.json')});
await fs.mkdir(destination,{recursive:true});await extract(zip,{dir:destination});const app=path.join(destination,'resources','app');await fs.mkdir(path.join(app,'node_modules'),{recursive:true});
for(const file of ['main.cjs','preload.cjs','rtc.html','rtc.js','solo.cjs','package.json'])await fs.copyFile(path.join('desktop',file),path.join(app,file));await fs.copyFile('unity/Assets/StreamingAssets/solo.js',path.join(app,'solo.js'));await fs.cp('desktop/node_modules/ws',path.join(app,'node_modules/ws'),{recursive:true});await fs.copyFile('LICENSE',path.join(app,'LICENSE'));
if(platform==='linux'){await fs.chmod(path.join(destination,'electron'),0o755);await fs.chmod(path.join(destination,'chrome-sandbox'),0o755);await fs.chmod(path.join(destination,'chrome_crashpad_handler'),0o755);}console.log(`Packaged Electron ${pkg.version} for ${platform}: ${destination}`);
})().catch(error=>{console.error(error);process.exitCode=1;});
