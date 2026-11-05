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

const RELEASE_TAG = 'v0.2.5';
const RELEASE_NAME = 'Movie Night VR v0.2.5 — Visual Parabolic Teleport, Web Audio Persistence, Void Safeguards & Provider URL Resilience';

const RELEASE_BODY = `## Movie Night VR v0.2.5 • Meta Quest Release

### 🎯 Locomotion & Visual Parabolic Teleportation ("Teleport Stick")
- **Dual Locomotion & Dedicated Teleport Stick**: Teleport can now be triggered in *any* locomotion mode by flicking/pushing the **Right Thumbstick Forward** (\`turn.y > 0.55\`), preserving smooth joystick walking on the left stick without forcing users into a teleport-only mode.
- **Surface Normal Validation**: Parabolic raycasts strictly enforce surface steepness checks ($\\mathbf{n} \\cdot \\mathbf{u} > 0.55$, slope $\\le 56^\\circ$). Raycasts hitting vertical walls, ceilings, or obstacles are rejected.
- **Directional & Dynamic Color Feedback**:
  - **Valid Ground Target**: Arc and circular pad glow vibrant emerald/cyan (\`#26D973\`), showing a directional arrow indicating your post-teleport facing angle.
  - **Invalid Target (Walls/Ceilings/Void)**: Arc glows crimson red (\`#F24033\`), the target marker is hidden, and releasing the stick safely cancels without relocating the player.
- **Right-Hand Ray Aiming**: Automatically routes aiming through \`RightHand\` when the right thumbstick is pushed, matching native VR pointer standards.

### 🌌 Void & Passthrough Endless Falling Safeguards
- **Zero-Gravity Drift & Falling Loop Protection**: In **The Void** or **MR Passthrough** modes, virtual room colliders are intentionally hidden. Added a firm ground clamp enforcing $y \\ge 0$ and zeroing vertical velocity, eliminating the infinite falling/respawn loop.

### 🔊 Web Browser Wrapper Audio Controls
- **Persistent Media Volume Injection**: Fixed volume controls in the web browser wrapper (Stremio Web, Jellyfin Web, YouTube).
- **Prototype Interception & MutationObserver**: Intercepts \`HTMLMediaElement.prototype.play\` and monitors DOM mutations, immediately binding volume and mute states to dynamically spawned video tags and embedded player \`<iframe>\` frames.
- **State Re-application**: Volume is re-applied automatically on page finish, tab switching, and web screen toggle.
- **Desktop Runtime Volume Bridge**: Wired local browser volume to \`desktop.Browser("volume", val)\` on PC VR.

### 💬 Subtitle Lifecycle & Format Hardening
- **Lazy TextMesh Creation**: Fixed \`CinemaSubtitles.cs\` where late screen attachment resulted in null subtitle objects.
- **Single-Digit Hour Normalization**: Fixed \`server/subtitles.js\` regex to normalize irregular SubRip cues (\`0:01:05,200\` $\\to$ \`00:01:05.200\`).
- **Culture Invariant Parsing**: Enforced \`CultureInfo.InvariantCulture\` across all timecode split conversions.

### 🌐 Media Provider Resilience
- **Automatic Protocol Scheme Detection**: Server URLs entered without a scheme (e.g. \`192.168.1.100:8096\` or local domain names) automatically prepend \`http://\`, preventing silent \`Uri.TryCreate\` failures across **Jellyfin**, **MediaStorm**, and **Silo**.

### ♟ Tabletop Games
- **Hand Controller Grabbing in Solo**: Enabled direct hand-controller piece grabbing and physical table interaction in offline solo mode.

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
