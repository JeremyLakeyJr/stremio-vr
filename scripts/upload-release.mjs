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
console.log(`Uploading ${apkPath} (${(apkSize / (1024 * 1024)).toFixed(2)} MB)...`);

const token = execSync('git credential fill', { input: 'protocol=https\nhost=github.com\n\n' })
  .toString()
  .match(/password=(.*)/)[1]
  .trim();

const headers = {
  Authorization: 'Bearer ' + token,
  'User-Agent': 'MediaVR-Release-Uploader',
  Accept: 'application/vnd.github+json'
};

const RELEASE_TAG = 'v0.2.3';
const RELEASE_NAME = 'Movie Night VR v0.2.3 — Configurable Menu Toggle, Native Multi-Provider Hub & 4K Engine';

const RELEASE_BODY = `## Movie Night VR v0.2.3 • Meta Quest Release

### 🎮 Configurable Menu Toggle & Controller Sleep Resilience
- **Single-Option Menu Activation**: Menu activation is now strictly single-option with zero button conflicts. Choose between:
  - **Left Menu Button** (Default standard VR convention)
  - **Left Y Button**
  - **Left X Button**
  - **Right A Button**
  - **Right B Button**
  - **Right A Long-Press (0.4s)**
  - **Left Stick Click**
- **Quest 3 Sleep/Wake & Hand-Tracking Fallback**: Replaced fragile single-frame device queries with dynamic XR device caching and characteristic scanning. Headset sleep/wake cycles or hand-tracking swaps no longer lock users out of the menu.
- **Sprint / Movement Isolation**: Left stick click sprinting is guarded against accidental sprint triggering when stick click is mapped to menu toggle.

### 🎬 Native Multi-Provider Discover & Streaming Catalog
- **Integrated Catalog Browser**: Unified browse interface in Library with filter pills:
  - **All Sources**
  - **Stremio & Nuvio**
  - **Jellyfin**
  - **MediaStorm**
  - **Silo**
- **Asynchronous Pagination & Direct ExoPlayer Loading**: Streams load directly with upstream auth tokens preserved in ExoPlayer HTTP headers without broadcasting credentials to multiplayer room peers.
- **Multiplayer Multi-Channel Audio**: Added room server audio cycling (\`NextServerAudioTrack\`) to easily switch transcoded audio tracks on the fly.

### ⚡ 4K Virtual Display Engine & Snapdragon XR2 Guard
- **Adaptive Frame Throttling**: 4K web cinema and desktop display emulation dynamically throttles to 24fps on ultra-high resolutions, preventing Snapdragon XR2 Gen 2 JNI queue congestion and maintaining silky-smooth 90/120Hz head tracking.
- **Password Security Masking**: Upgraded all credentials and token input fields (Jellyfin, MediaStorm, Silo, Mellon, Stremio) with native bullet masking (\`InputField.ContentType.Password\`).
- **First-Run Onboarding Wizard**: Guided 3-step setup flow for new solo users.

### 📦 Sideload Instructions (Meta Quest 3 / 2 / Pro)
1. Download **\`movie-night.apk\`** below.
2. Sideload via **SideQuest** or run:
   \`\`\`bash
   adb install -r movie-night.apk
   \`\`\`
3. Put on your Quest headset, open **App Library -> Unknown Sources**, and launch **Movie Night VR**.`;

async function run() {
  console.log(`Checking release for ${RELEASE_TAG}...`);
  let relRes = await fetch(`https://api.github.com/repos/oOflyeyesOo/Media-VR/releases/tags/${RELEASE_TAG}`, { headers });
  let rel;

  if (relRes.status === 404) {
    console.log(`Release ${RELEASE_TAG} does not exist. Creating new release...`);
    const createRes = await fetch('https://api.github.com/repos/oOflyeyesOo/Media-VR/releases', {
      method: 'POST',
      headers,
      body: JSON.stringify({
        tag_name: RELEASE_TAG,
        target_commitish: 'master',
        name: RELEASE_NAME,
        body: RELEASE_BODY,
        draft: false,
        prerelease: false
      })
    });
    if (!createRes.ok) {
      throw new Error(`Failed to create release (${createRes.status}): ${await createRes.text()}`);
    }
    rel = await createRes.json();
    console.log(`Created release ${rel.tag_name} (id: ${rel.id})`);
  } else if (!relRes.ok) {
    throw new Error('Failed to get release: ' + relRes.statusText);
  } else {
    rel = await relRes.json();
    console.log(`Found existing release ${rel.tag_name} (id: ${rel.id})`);
  }

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
  const patchRes = await fetch(`https://api.github.com/repos/oOflyeyesOo/Media-VR/releases/${rel.id}`, {
    method: 'PATCH',
    headers,
    body: JSON.stringify({
      name: RELEASE_NAME,
      body: RELEASE_BODY
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
