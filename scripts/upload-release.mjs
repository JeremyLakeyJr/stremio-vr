import fs from 'node:fs';
import path from 'node:path';
import { execSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const rootDir = path.resolve(__dirname, '..');
const apkPath = path.join(rootDir, 'unity', 'Builds', 'movie-night.apk');

if (!fs.existsSync(apkPath)) {
  console.error('APK file not found at:', apkPath);
  process.exit(1);
}

const apkSize = fs.statSync(apkPath).size;
console.log(`Uploading ${apkPath} (${apkSize} bytes)...`);

const token = execSync('git credential fill', { input: 'protocol=https\nhost=github.com\n\n' })
  .toString()
  .match(/password=(.*)/)[1]
  .trim();

const headers = {
  Authorization: 'Bearer ' + token,
  'User-Agent': 'MediaVR-Release-Uploader',
  Accept: 'application/vnd.github+json'
};

async function run() {
  const relRes = await fetch('https://api.github.com/repos/oOflyeyesOo/Media-VR/releases/tags/v0.2.2', { headers });
  if (!relRes.ok) throw new Error('Failed to get release: ' + relRes.statusText);
  const rel = await relRes.json();
  console.log(`Found release ${rel.tag_name} (id: ${rel.id})`);

  // Delete existing movie-night.apk asset if present
  for (const asset of rel.assets) {
    if (asset.name === 'movie-night.apk') {
      console.log(`Deleting previous asset ${asset.name} (id: ${asset.id})...`);
      const delRes = await fetch(`https://api.github.com/repos/oOflyeyesOo/Media-VR/releases/assets/${asset.id}`, {
        method: 'DELETE',
        headers
      });
      if (!delRes.ok && delRes.status !== 204) {
        console.warn('Failed to delete asset:', delRes.status, await delRes.text());
      } else {
        console.log('Successfully deleted old asset.');
      }
    }
  }

  // Upload new asset
  console.log('Uploading new movie-night.apk to release...');
  const uploadUrl = `https://uploads.github.com/repos/oOflyeyesOo/Media-VR/releases/${rel.id}/assets?name=movie-night.apk`;
  const fileStream = fs.readFileSync(apkPath);

  const upRes = await fetch(uploadUrl, {
    method: 'POST',
    headers: {
      ...headers,
      'Content-Type': 'application/vnd.android.package-archive',
      'Content-Length': apkSize.toString()
    },
    body: fileStream
  });

  if (!upRes.ok) {
    throw new Error(`Upload failed (${upRes.status}): ${await upRes.text()}`);
  }

  const assetData = await upRes.json();
  console.log('✔ Asset uploaded successfully! ID:', assetData.id, 'Download URL:', assetData.browser_download_url);

  // Update release description
  const body = `## Movie Night VR v0.2.2 • Quest Sideload Release

### 🎮 Player Locomotion, Avatar Mechanics & Axis Fixes
- **Hand & Wrist IK Articulation**: Full rotational 6DOF tracking for controller-to-avatar wrist mechanics. Hands now rotate realistically with Quest controllers instead of remaining rigid.
- **Dynamic Avatar Height & Eye Level Adaptation**: Replaced forced 1.65m normalization with natural VRM scaling and automatic player eye-level tracking. Headset tracking space and character controller bounds adjust dynamically to whatever avatar you select.
- **Unconstrained Locomotion**: Thumbstick movement and smooth turning remain fully enabled while menus are open, eliminating movement lockouts.

### 🌐 Unified Servers & Streaming Hub
- **1-Click Web Streamers (720p HD Virtual Display)**:
  - 🌌 **Nuvio Web**: Direct access to Nuvio streaming frontend with instant Stremio addon compatibility.
  - 🎬 **Stremio Web**: Stream via your Stremio library and addons in solo or multiplayer theater.
  - 🍿 **Jellyfin Web**: Connect to your self-hosted Jellyfin server with native authentication.
  - ⚡ **MediaStorm & Silo**: Direct backend integration for Debrid, Usenet, and high-performance streaming.
- **Nuvio Profile Addon Sync**: Automatically sync addons and catalogs from your Nuvio profile into your in-VR library.
- **Hardware-Accelerated Web Engine**: Upgraded virtual display to 720p HD (1280x720) with desktop Chromium emulation for full web player functionality.

### 📦 Sideload Instructions (Meta Quest 3 / 2 / Pro)
1. Download **\`movie-night.apk\`** below.
2. Install via **SideQuest** (drag & drop APK) or ADB:
   \`\`\`bash
   adb install -r movie-night.apk
   \`\`\`
3. Put on your Quest headset, navigate to **App Library -> Unknown Sources**, and launch **Movie Night VR**.`;

  const patchRes = await fetch(`https://api.github.com/repos/oOflyeyesOo/Media-VR/releases/${rel.id}`, {
    method: 'PATCH',
    headers,
    body: JSON.stringify({
      name: 'Movie Night VR v0.2.2 - Locomotion, Wrist IK & Streaming Hub',
      body
    })
  });

  if (patchRes.ok) {
    console.log('✔ Release notes updated successfully!');
  } else {
    console.warn('Failed to update release notes:', patchRes.status, await patchRes.text());
  }
}

run().catch(err => {
  console.error('Error:', err);
  process.exit(1);
});
