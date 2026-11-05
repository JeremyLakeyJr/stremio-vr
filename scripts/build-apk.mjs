import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export function editorCandidates(version, platform = process.platform, home = os.homedir(), env = process.env) {
  if (env.UNITY_PATH) return [env.UNITY_PATH];
  if (platform === 'win32') return [path.win32.join(env.ProgramFiles || 'C:\\Program Files', 'Unity', 'Hub', 'Editor', version, 'Editor', 'Unity.exe')];
  if (platform === 'darwin') return [`/Applications/Unity/Hub/Editor/${version}/Unity.app/Contents/MacOS/Unity`];
  return [path.join(home, 'Unity/Hub/Editor', version, 'Editor/Unity'), path.join('/opt/unity/Editor', version, 'Editor/Unity')];
}
function run(binary, args) {
  const result = spawnSync(binary, args, { cwd: root, stdio: 'inherit' });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`${path.basename(binary)} failed (${result.signal || result.status})`);
}
export function main() {
  const version = fs.readFileSync(path.join(root, 'unity/ProjectSettings/ProjectVersion.txt'), 'utf8').match(/^m_EditorVersion: (.+)$/m)?.[1].trim();
  if (!version) throw new Error('Missing Unity editor version');
  const editor = editorCandidates(version).find(file => fs.existsSync(file));
  if (!editor) throw new Error(`Install Unity ${version} with Android Build Support, or set UNITY_PATH to its executable.`);
  run(process.execPath, ['scripts/build-solo.mjs']);
  const homeScene = path.join(root, 'unity/Assets/PrivateHome/Home Ulkan/Customizable Home.unity');
  if (!fs.existsSync(homeScene)) run(process.env.PYTHON || (process.platform === 'win32' ? 'python' : 'python3'), ['scripts/import-private-home.py']);
  const log = path.join(root, 'unity-build.log');
  console.log(`Building with Unity ${version}. Log: ${log}`);
  run(editor, ['-batchmode', '-nographics', '-quit', '-projectPath', path.join(root, 'unity'), '-buildTarget', 'Android', '-executeMethod', 'StremioVR.Editor.QuestBuild.BuildRelease', '-logFile', log]);
  if (!fs.existsSync(path.join(root, 'unity/Builds/movie-night.apk'))) throw new Error('Unity exited without producing movie-night.apk');
  console.log('Built unity/Builds/movie-night.apk');
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { main(); } catch (error) { console.error(error.message); process.exitCode = 1; }
}
