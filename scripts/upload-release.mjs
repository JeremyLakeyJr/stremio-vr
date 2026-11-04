import fs from 'node:fs';
import path from 'node:path';
import { execSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const rootDir = path.resolve(__dirname, '..');
const apkPath = path.join(rootDir, 'unity', 'Builds', 'movie-night.apk');
const checksumPath = path.join(rootDir, 'unity', 'Builds', 'SHA256SUMS');

if (!fs.existsSync(apkPath)) {
  console.error('APK file not found at:', apkPath);
  process.exit(1);
}

const apkSize = fs.statSync(apkPath).size;
console.log(`Preparing release assets: ${apkPath} (${(apkSize / (1024 * 1024)).toFixed(2)} MB)...`);

const token = execSync('git credential fill', { input: 'protocol=https\nhost=github.com\n\n' })
  .toString()
  .match(/password=(.*)/)[1]
  .trim();

const headers = {
  Authorization: 'Bearer ' + token,
  'User-Agent': 'MediaVR-Release-Uploader',
  Accept: 'application/vnd.github+json'
};

const RELEASE_TAG = 'v0.2.4';
const RELEASE_NAME = 'Movie Night VR v0.2.4 — 3D SBS/OU, HDR Tone-Mapping, In-VR Keyboard & Provider Hardening';

const RELEASE_BODY = `## Movie Night VR v0.2.4 • Meta Quest Release

### 🥽 Stereoscopic 3D Decoding & Cinema Display (DeoVR / Bigscreen Parity)
- **Stereoscopic 3D Modes**: Native in-shader Single-Pass Instanced XR split supporting:
  - **2D Mono**
  - **3D Side-by-Side (SBS)**
  - **3D Over-Under (OU)**
  - **Left / Right Eye Inversion**
- **In-Shader HDR Video Tone-Mapping**: Custom ACES filmic tone-mapping for Rec.2020 wide color gamut, HDR10 PQ (ST2084), and HLG broadcast — completely resolving washed-out 4K HDR playback on Meta Quest.
- **DeoVR Real-Time Picture Calibration**: Dynamic in-shader Brightness ($0.4\\times - 1.8\\times$), Contrast ($0.6\\times - 1.8\\times$), and Saturation ($0.0\\times - 2.0\\times$) tuning directly from the Cinema Controls dock.

### 🌌 Fluid & Bigscreen Environments & Spatial Comfort
- **Environment Modes**: 1-click toggle between:
  - **🛋 Cinema Apartment** (Full 3D physical lounge with dynamic ambient lighting)
  - **🌑 The Void** (Pure OLED black background for maximum contrast and zero distractions)
  - **🥽 Mixed Reality Passthrough** (Float the giant virtual cinema screen directly in your physical living room)
- **Bed Mode (Ceiling Projection)**: 1-click snap angling the screen $75^\\circ$ toward the ceiling for comfortable viewing while lying down.
- **Curved IMAX Screen Wrap**: Smooth $0^\\circ \\to 90^\\circ$ cylindrical curvature slider for an IMAX-style curved display.
- **Cinema Controls Quick Dock**: Dedicated in-playback transport bar ($\\pm 10$s, $\\pm 30$s, Play/Pause) and track selectors.

### ⌨️ In-VR Spatial QWERTY Virtual Keyboard
- Custom in-world spatial keyboard (\`VRKeyboard.cs\`) attached to all text inputs via a ⌨️ launcher button.
- Full support for laser ray beam selection and direct hand-tracking poke/pinch input.
- Includes QWERTY letters, shift, numbers, symbols, backspace, clear, space, and done buttons.

### 🎬 Multi-Provider Streaming Ecosystem & Hardening
- **Universal Provider Hub**: Full integration with **Stremio**, **Jellyfin**, **MediaStorm**, **Silo**, **lostb1t / Remux**, and **Nuvio / Mellon** sync.
- **Global Cross-Provider Search**: Unified search across all connected catalogs with provider badges and 1-click stream resolution.
- **Unified Continue Watching**: Aggregated watch history and resume positions across all backends.
- **Robustness & Bounded Timeouts**: Added 10–15s timeouts, URL validation, and async generation guards preventing stale loads from clobbering UI during rapid navigation.
- **Graceful Error & Retry UI**: Added user-facing error states, cancellation controls, and retry buttons on all provider views.

### 📋 In-App System Diagnostics & Telemetry
- Dedicated **Diagnostics Page** in Settings displaying hardware model, OS, GPU details, XR subsystem, target FPS, room status, and provider connectivity.
- Redacted circular log buffer (credential/token masked) with a 1-click **Copy Diagnostics to Clipboard** button.

### ⚡ Transcoder Bitrate Ladder & Server Webhooks
- **Transcoder Quality Ladder**: Added \`4K\`, \`1440p\`, \`1080p\`, \`720p\`, and \`480p\` quality ladder profiles to \`server/hwaccel.js\`.
- **Discord & Slack Webhooks**: Multi-service webhook notifications for room join/leave and playback events configurable directly from the server dashboard.

---

### 📦 Sideload Instructions (Meta Quest 3 / 3S / Pro / 2)
1. Download **\`movie-night.apk\`** and **\`SHA256SUMS\`** below.
2. Sideload via **SideQuest** or run:
   \`\`\`bash
   adb install -r movie-night.apk
   \`\`\`
3. Put on your Quest headset, open **App Library -> Unknown Sources**, and launch **Movie Night VR**.`;

async function uploadAsset(relId, filePath, assetName, contentType) {
  const fileStats = fs.statSync(filePath);
  console.log(`Uploading ${assetName} (${(fileStats.size / 1024).toFixed(1)} KB)...`);
  const uploadUrl = `https://uploads.github.com/repos/oOflyeyesOo/Media-VR/releases/${relId}/assets?name=${assetName}`;
  const fileStream = fs.readFileSync(filePath);

  const upRes = await fetch(uploadUrl, {
    method: 'POST',
    headers: {
      ...headers,
      'Content-Type': contentType,
      'Content-Length': fileStats.size.toString()
    },
    body: fileStream
  });

  if (!upRes.ok) {
    throw new Error(`Upload of ${assetName} failed (${upRes.status}): ${await upRes.text()}`);
  }

  const assetData = await upRes.json();
  console.log(`✔ ${assetName} uploaded successfully! Download URL: ${assetData.browser_download_url}`);
}

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

  // Delete existing assets if present
  if (rel.assets && rel.assets.length > 0) {
    for (const asset of rel.assets) {
      if (asset.name === 'movie-night.apk' || asset.name === 'SHA256SUMS') {
        console.log(`Deleting previous asset ${asset.name} (id: ${asset.id})...`);
        await fetch(`https://api.github.com/repos/oOflyeyesOo/Media-VR/releases/assets/${asset.id}`, {
          method: 'DELETE',
          headers
        });
        console.log(`Successfully deleted ${asset.name}.`);
      }
    }
  }

  // Upload movie-night.apk
  await uploadAsset(rel.id, apkPath, 'movie-night.apk', 'application/vnd.android.package-archive');

  // Upload SHA256SUMS if present
  if (fs.existsSync(checksumPath)) {
    await uploadAsset(rel.id, checksumPath, 'SHA256SUMS', 'text/plain');
  }

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
