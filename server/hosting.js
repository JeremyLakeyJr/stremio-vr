import { readFile } from 'node:fs/promises';

export async function hostingInfo(file = process.env.TUNNEL_LINK_FILE) {
  const tsUrl = process.env.TAILSCALE_URL || (process.env.TS_HOSTNAME ? `https://${process.env.TS_HOSTNAME}` : null);
  if (tsUrl && /^https?:\/\/[a-z0-9.-]+(?::[0-9]+)?(?:\/.*)?$/i.test(tsUrl)) {
    return { url: tsUrl, online: true, mode: 'tailscale' };
  }

  try {
    const data = JSON.parse(await readFile(file, 'utf8'));
    if (data.url && data.url !== 'https://api.trycloudflare.com' && Number.isFinite(data.updatedAt) && Date.now() - data.updatedAt < 20000 && data.updatedAt <= Date.now() + 5000) {
      const isTryCloudflare = /^https:\/\/[a-z0-9-]+\.trycloudflare\.com$/.test(data.url);
      const isNamedTunnel = data.mode === 'named' || Boolean(process.env.TUNNEL_TOKEN || process.env.TUNNEL_URL);
      const isSafeHttps = /^https:\/\/[a-z0-9.-]+(?::[0-9]+)?(?:\/.*)?$/i.test(data.url);
      if (isTryCloudflare || (isNamedTunnel && isSafeHttps)) {
        return { url: data.url, online: true, mode: isNamedTunnel ? 'named' : 'quick' };
      }
    }
  } catch {}

  if (process.env.TUNNEL_URL && /^https?:\/\/[a-z0-9.-]+(?::[0-9]+)?(?:\/.*)?$/i.test(process.env.TUNNEL_URL)) {
    return { url: process.env.TUNNEL_URL, online: true, mode: 'named' };
  }

  return { url: null, online: false };
}

export const dashboard = `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Movie Night VR — Multi-Provider Cinema</title>
  <style>
    :root { color-scheme: dark; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; }
    body { margin: 0; background: #0c0a14; color: #f5f2fd; }
    main { max-width: 960px; margin: auto; padding: 32px 20px; }
    header { margin-bottom: 24px; }
    h1 { font-size: clamp(32px, 6vw, 56px); margin: 0 0 8px; font-weight: 800; background: linear-gradient(135deg, #c7b0ff 0%, #a77eff 100%); -webkit-background-clip: text; -webkit-text-fill-color: transparent; }
    p { line-height: 1.6; color: #bcb2d0; }
    .hero { width: 100%; border-radius: 20px; max-height: 400px; object-fit: cover; box-shadow: 0 16px 48px rgba(0,0,0,0.4); border: 1px solid #2e2445; }
    .card { background: #181326; border: 1px solid #34264f; padding: 24px; border-radius: 20px; margin: 24px 0; }
    .badges { display: flex; flex-wrap: wrap; gap: 8px; margin: 12px 0 20px; }
    .badge { background: #261c3d; border: 1px solid #533980; padding: 6px 12px; border-radius: 999px; font-size: 13px; color: #d8c9ff; font-weight: 500; }
    .badge.active { background: #133a2a; border-color: #21875c; color: #78e8b4; }
    .share-row { display: grid; grid-template-columns: 1fr; gap: 20px; }
    @media (min-width: 680px) { .share-row { grid-template-columns: 1fr auto; align-items: start; } }
    .qr-container { display: flex; flex-direction: column; align-items: center; justify-content: center; background: #0c0a14; padding: 14px; border-radius: 16px; border: 1px solid #34264f; min-width: 150px; text-align: center; }
    .qr-container img { width: 140px; height: 140px; border-radius: 8px; background: white; padding: 4px; }
    .qr-container small { margin-top: 8px; color: #8f83ab; font-size: 11px; }
    button { background: #9667ff; color: #0c0a14; border: 0; border-radius: 12px; padding: 14px 24px; font-size: 15px; font-weight: 700; cursor: pointer; transition: transform 0.1s, background 0.15s; }
    button:hover:not(:disabled) { background: #af87ff; transform: translateY(-1px); }
    button:disabled { background: #3b3252; color: #776a91; cursor: not-allowed; }
    input { box-sizing: border-box; width: 100%; background: #0c0a14; color: white; border: 1px solid #4a366e; border-radius: 10px; padding: 14px; margin: 8px 0 16px; font: inherit; }
    input:focus { outline: none; border-color: #9667ff; box-shadow: 0 0 0 2px rgba(150,103,255,0.25); }
    small { color: #9c8fb8; }
    a { color: #bca1ff; text-decoration: none; }
    a:hover { text-decoration: underline; }
    ol { line-height: 1.8; color: #cfc6e4; padding-left: 20px; }
    #status { color: #78e8b4; font-weight: 500; }
    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 16px; margin-top: 16px; }
    .box { background: #130e20; border: 1px solid #2b1f42; padding: 16px; border-radius: 14px; }
    .box h4 { margin: 0 0 6px; color: #e5ddfa; font-size: 15px; }
    .box p { margin: 0; font-size: 13px; color: #9d90ba; }
  </style>
</head>
<body>
<main>
  <header>
    <h1>Media-VR Cinema</h1>
    <p>Multi-provider VR movie theater for Meta Quest with hardware transcoding, Jellyfin, Remux, MediaStorm, Silo, and Stremio.</p>
  </header>

  <img class="hero" src="/screenshots/apartment-lounge.png" alt="Movie Night VR Lounge">

  <section class="card">
    <h2>Invite Friends to Your Room</h2>
    <div class="badges">
      <span id="transcoderBadge" class="badge">Detecting Transcoder…</span>
      <span id="tunnelBadge" class="badge">Checking Tunnel…</span>
      <span id="roomsBadge" class="badge">Rooms: 0</span>
    </div>

    <div class="share-row">
      <div>
        <p id="status" role="status">Checking your share link…</p>
        <label for="link">Server Public URL</label>
        <input id="link" readonly placeholder="Waiting for Cloudflare tunnel…">
        <label for="room">Room Code (optional)</label>
        <input id="room" maxlength="12" placeholder="e.g. 4-letter code from VR headset">
        <button id="copy" disabled>Copy invite</button>
      </div>

      <div class="qr-container" id="qrWrapper" style="display:none;">
        <img id="qrImg" alt="Room QR Code">
        <small>Scan with phone or Quest browser</small>
      </div>
    </div>
  </section>

  <section class="card">
    <h3>Integrated Media Backends</h3>
    <div class="grid">
      <div class="box">
        <h4>🍿 Stremio + Add-ons</h4>
        <p>AIOStreams, Torrentio, Comet, MediaFusion & Debrid streaming.</p>
      </div>
      <div class="box">
        <h4>⚡ Jellyfin & Remux</h4>
        <p>Native Jellyfin REST browsing & instant WebDAV/Debrid playback.</p>
      </div>
      <div class="box">
        <h4>🌪️ MediaStorm</h4>
        <p>Debrid/Usenet streaming server with direct HLS delivery.</p>
      </div>
      <div class="box">
        <h4>🏰 Silo & Nuvio</h4>
        <p>Self-hosted Go server libraries and Mellon profile sync.</p>
      </div>
    </div>
  </section>

  <h2>How to Join in VR</h2>
  <ol>
    <li>Install <strong>Movie Night</strong> on your Meta Quest headset.</li>
    <li>Open <strong>My Room</strong>, paste your server URL, and enter your room code.</li>
    <li>Browse <strong>Discover</strong> or <strong>Accounts & Servers</strong> to choose movies or TV shows.</li>
  </ol>
  <p><a href="https://github.com/oOflyeyesOo/Media-VR" target="_blank" rel="noopener">GitHub Repository</a> · Multi-Provider VR Cinema</p>
</main>

<script>
  const link = document.querySelector('#link');
  const statusEl = document.querySelector('#status');
  const copyBtn = document.querySelector('#copy');
  const qrWrapper = document.querySelector('#qrWrapper');
  const qrImg = document.querySelector('#qrImg');
  const transcoderBadge = document.querySelector('#transcoderBadge');
  const tunnelBadge = document.querySelector('#tunnelBadge');
  const roomsBadge = document.querySelector('#roomsBadge');

  async function updateHealth() {
    try {
      const res = await fetch('/health', { cache: 'no-store' });
      if (res.ok) {
        const h = await res.json();
        roomsBadge.textContent = 'Active Rooms: ' + (h.rooms || 0);
        if (h.transcoder) {
          if (h.transcoder.hwAccel) {
            transcoderBadge.textContent = '⚡ GPU Active: ' + h.transcoder.encoder + ' (' + (h.transcoder.maxHeight || 1080) + 'p @ ' + (h.transcoder.maxFps || 60) + 'fps)';
            transcoderBadge.className = 'badge active';
          } else {
            transcoderBadge.textContent = 'Smart Remux & CPU Fallback (720p)';
            transcoderBadge.className = 'badge';
          }
        }
      }
    } catch {}
  }

  async function poll() {
    try {
      const res = await fetch('/api/hosting', { cache: 'no-store' });
      const data = await res.json();
      if (data.online && data.url) {
        link.value = data.url;
        copyBtn.disabled = false;
        statusEl.textContent = 'Ready — share this link with your friends or scan with Quest!';
        if (data.mode === 'tailscale') {
          tunnelBadge.textContent = '🔒 Tailscale MagicDNS / Tailnet';
        } else if (data.mode === 'named') {
          tunnelBadge.textContent = '🛡️ Named Cloudflare Tunnel';
        } else {
          tunnelBadge.textContent = '☁️ TryCloudflare Quick Tunnel';
        }
        tunnelBadge.className = 'badge active';
        qrImg.src = 'https://api.qrserver.com/v1/create-qr-code/?size=180x180&data=' + encodeURIComponent(data.url);
        qrWrapper.style.display = 'flex';
      } else {
        copyBtn.disabled = true;
        qrWrapper.style.display = 'none';
        tunnelBadge.textContent = 'Tunnel Starting…';
        tunnelBadge.className = 'badge';
        statusEl.textContent = 'Starting tunnel… If using Docker, keep containers running.';
      }
    } catch {
      statusEl.textContent = 'Waiting for server…';
      copyBtn.disabled = true;
      qrWrapper.style.display = 'none';
    }
  }

  copyBtn.onclick = async () => {
    const room = document.querySelector('#room').value.trim();
    const invite = 'Join me in Movie Night VR!\nServer: ' + link.value + (room ? '\\nRoom: ' + room : '');
    try {
      await navigator.clipboard.writeText(invite);
      statusEl.textContent = 'Invite link copied to clipboard!';
    } catch {
      link.select();
      statusEl.textContent = 'Select and copy the URL above.';
    }
  };

  poll();
  updateHealth();
  setInterval(poll, 4000);
  setInterval(updateHealth, 5000);
</script>
</body>
</html>`;
