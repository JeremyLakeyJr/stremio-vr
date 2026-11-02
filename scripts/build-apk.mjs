import fs from 'node:fs';
import path from 'node:path';
import { execSync, spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const rootDir = path.resolve(__dirname, '..');
const unityDir = path.join(rootDir, 'unity');
const buildsDir = path.join(unityDir, 'Builds');

console.log('\n========================================================');
console.log('   Movie Night VR • Quest APK Build Preparation Tool   ');
console.log('========================================================\n');

// 1. Ensure solo bundle is built
console.log('[1/4] Verifying solo offline rules bundle...');
try {
  execSync('npm run build:solo', { cwd: rootDir, stdio: 'inherit' });
  console.log('  ✔ StreamingAssets/solo.js is up to date.');
} catch (err) {
  console.error('  ✖ Failed to bundle solo rules:', err.message);
  process.exit(1);
}

// 2. Ensure Open Concept Home visual assets are imported
console.log('\n[2/4] Verifying Open Concept Home environment...');
const homeUlkanPath = path.join(unityDir, 'Assets', 'PrivateHome', 'Home Ulkan', 'Customizable Home.unity');
const homeZipPath = path.join(rootDir, 'Open Concept Home v1.2 - [2022.3.22f1] [COMPLETED].zip');

if (fs.existsSync(homeUlkanPath)) {
  console.log('  ✔ PrivateHome visual assets are imported.');
} else if (fs.existsSync(homeZipPath)) {
  console.log('  ℹ Importing Open Concept Home from zip...');
  try {
    execSync(`python scripts/import-private-home.py "${homeZipPath}"`, { cwd: rootDir, stdio: 'inherit' });
    console.log('  ✔ Open Concept Home successfully imported into unity/Assets/PrivateHome.');
  } catch (err) {
    console.warn('  ▲ Could not run python import-private-home.py:', err.message);
  }
} else {
  console.warn('  ▲ Open Concept Home zip not found in workspace.');
}

// 3. Locate Unity Editor executable
console.log('\n[3/4] Searching for Unity 6 Editor...');
const candidatePaths = [
  process.env.UNITY_PATH,
  'C:\\Program Files\\Unity\\Hub\\Editor\\6000.6.7f1\\Editor\\Unity.exe',
  'C:\\Program Files\\Unity\\Hub\\Editor\\6000.0.66f2\\Editor\\Unity.exe',
  'D:\\Program Files\\Unity\\Hub\\Editor\\6000.0.66f2\\Editor\\Unity.exe',
  'E:\\Program Files\\Unity\\Hub\\Editor\\6000.0.66f2\\Editor\\Unity.exe',
  'C:\\Program Files\\Unity\\Editor\\Unity.exe',
].filter(Boolean);

// Also search wildcard 6000.*
const hubEditorDir = 'C:\\Program Files\\Unity\\Hub\\Editor';
if (fs.existsSync(hubEditorDir)) {
  try {
    const entries = fs.readdirSync(hubEditorDir);
    for (const entry of entries) {
      if (entry.startsWith('6000.')) {
        candidatePaths.push(path.join(hubEditorDir, entry, 'Editor', 'Unity.exe'));
      }
    }
  } catch {}
}

let unityExe = candidatePaths.find(p => fs.existsSync(p));

if (unityExe) {
  console.log(`  ✔ Found Unity Editor at: ${unityExe}`);
  console.log('\n[4/4] Starting Unity Batchmode Build for Quest (Android / ARM64)...');
  console.log('  Log output stream: unity-build.log');
  
  if (!fs.existsSync(buildsDir)) fs.mkdirSync(buildsDir, { recursive: true });

  const buildArgs = [
    '-batchmode',
    '-quit',
    '-projectPath', unityDir,
    '-executeMethod', 'StremioVR.Editor.QuestBuild.Build',
    '-logFile', path.join(rootDir, 'unity-build.log')
  ];

  const env = {
    ...process.env,
    JAVA_TOOL_OPTIONS: '-Djava.net.preferIPv4Stack=true',
    GRADLE_OPTS: '-Djava.net.preferIPv4Stack=true'
  };
  const result = spawnSync(unityExe, buildArgs, { stdio: 'inherit', env });
  if (result.status === 0) {
    console.log('\n✔ APK Build succeeded! Output: unity/Builds/movie-night.apk');
  } else {
    console.error(`\n✖ Unity build exited with status ${result.status}. Check unity-build.log for details.`);
  }
} else {
  console.log('  ℹ Unity Editor 6000.0.x is not installed locally on this machine.');
  console.log('\n  Available Build Options:');
  console.log('  ------------------------');
  console.log('  A) Build via Unity Hub:');
  console.log('     1. Install Unity Hub (`winget install Unity.UnityHub`)');
  console.log('     2. Install Unity 6000.0.66f2 with "Android Build Support" (Android SDK & NDK, OpenJDK)');
  console.log('     3. Open the "unity" folder in Unity and click "Movie Night -> Build Quest APK"');
  console.log('');
  console.log('  B) Build via GitHub Actions (Zero Local Setup):');
  console.log('     1. Open your repository on GitHub: https://github.com/oOflyeyesOo/Media-VR');
  console.log('     2. Go to Actions -> Build Quest APK -> "Run workflow"');
  console.log('     3. Download the generated `movie-night.apk` artifact once complete.');
  console.log('');
  console.log('  C) Sideload Existing / Release APK:');
  const existingApk = path.join(buildsDir, 'movie-night-v0.1.6-alpha.1.apk');
  if (fs.existsSync(existingApk)) {
    console.log(`     Release APK is available at: ${existingApk}`);
  }
}

// 4. Check connected Quest via ADB
console.log('\n[4/4] Checking ADB & Meta Quest connection status...');
const adbCandidates = [
  path.join(process.env.APPDATA || '', 'SideQuest', 'platform-tools', 'adb.exe'),
  'C:\\Program Files\\Oculus\\Support\\oculus-runtime\\adb.exe',
  'adb'
];

let adbExe = adbCandidates.find(p => {
  try {
    return p === 'adb' || fs.existsSync(p);
  } catch { return false; }
});

if (adbExe) {
  try {
    const adbOut = execSync(`"${adbExe}" devices`, { encoding: 'utf8' }).trim();
    console.log('  ' + adbOut.split('\n').join('\n  '));
    if (adbOut.includes('\tdevice')) {
      console.log('\n  ✔ Meta Quest headset detected and ready for sideloading!');
      console.log(`  To install: "${adbExe}" install -r unity/Builds/movie-night.apk`);
    } else {
      console.log('\n  ℹ No Quest headset detected via ADB (connect via USB and enable Developer Mode).');
    }
  } catch (e) {
    console.log('  ADB check skipped:', e.message);
  }
}
