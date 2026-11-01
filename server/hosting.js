import { readFile } from 'node:fs/promises';
import { getConfig } from './config.js';

export async function hostingInfo(file = process.env.TUNNEL_LINK_FILE) {
  const cfg = getConfig();
  const tsUrl = cfg.tailscaleUrl || process.env.TAILSCALE_URL || (process.env.TS_HOSTNAME ? `https://${process.env.TS_HOSTNAME}` : null);
  if (tsUrl && /^https?:\/\/[a-z0-9.-]+(?::[0-9]+)?(?:\/.*)?$/i.test(tsUrl)) {
    return { url: tsUrl, online: true, mode: 'tailscale' };
  }

  const customTunnel = cfg.tunnelUrl || process.env.TUNNEL_URL;
  if (customTunnel && /^https?:\/\/[a-z0-9.-]+(?::[0-9]+)?(?:\/.*)?$/i.test(customTunnel)) {
    return { url: customTunnel, online: true, mode: 'named' };
  }

  try {
    const data = JSON.parse(await readFile(file, 'utf8'));
    if (data.url && data.url !== 'https://api.trycloudflare.com' && Number.isFinite(data.updatedAt) && Date.now() - data.updatedAt < 20000 && data.updatedAt <= Date.now() + 5000) {
      const isTryCloudflare = /^https:\/\/[a-z0-9-]+\.trycloudflare\.com$/.test(data.url);
      const isNamedTunnel = data.mode === 'named' || Boolean(cfg.tunnelToken || process.env.TUNNEL_TOKEN || customTunnel);
      const isSafeHttps = /^https:\/\/[a-z0-9.-]+(?::[0-9]+)?(?:\/.*)?$/i.test(data.url);
      if (isTryCloudflare || (isNamedTunnel && isSafeHttps)) {
        return { url: data.url, online: true, mode: isNamedTunnel ? 'named' : 'quick' };
      }
    }
  } catch {}

  return { url: null, online: false };
}

export const dashboard = `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Movie Night VR — Server Admin & Hub</title>
  <style>
    :root { color-scheme: dark; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; }
    body { margin: 0; background: #0c0a14; color: #f5f2fd; }
    main { max-width: 960px; margin: auto; padding: 24px 16px; }
    header { margin-bottom: 20px; }
    h1 { font-size: clamp(28px, 5vw, 44px); margin: 0 0 6px; font-weight: 800; background: linear-gradient(135deg, #c7b0ff 0%, #a77eff 100%); -webkit-background-clip: text; -webkit-text-fill-color: transparent; }
    p { line-height: 1.5; color: #bcb2d0; margin: 4px 0 12px; }
    
    .nav-tabs { display: flex; gap: 8px; border-bottom: 1px solid #2e2445; padding-bottom: 12px; margin-bottom: 24px; }
    .nav-btn { background: #181326; color: #bcb2d0; border: 1px solid #34264f; padding: 10px 18px; border-radius: 12px; font-weight: 600; font-size: 14px; cursor: pointer; transition: all 0.15s; }
    .nav-btn:hover { background: #261c3d; color: #f5f2fd; }
    .nav-btn.active { background: #7c4dff; color: #ffffff; border-color: #9667ff; box-shadow: 0 4px 16px rgba(124,77,255,0.3); }

    .card { background: #181326; border: 1px solid #34264f; padding: 20px; border-radius: 18px; margin-bottom: 20px; }
    .badges { display: flex; flex-wrap: wrap; gap: 8px; margin: 10px 0 16px; }
    .badge { background: #261c3d; border: 1px solid #533980; padding: 6px 12px; border-radius: 999px; font-size: 12px; color: #d8c9ff; font-weight: 500; }
    .badge.active { background: #133a2a; border-color: #21875c; color: #78e8b4; }

    .share-row { display: grid; grid-template-columns: 1fr; gap: 16px; }
    @media (min-width: 680px) { .share-row { grid-template-columns: 1fr auto; align-items: start; } }
    .qr-container { display: flex; flex-direction: column; align-items: center; justify-content: center; background: #0c0a14; padding: 14px; border-radius: 16px; border: 1px solid #34264f; min-width: 140px; text-align: center; }
    .qr-container img { width: 130px; height: 130px; border-radius: 8px; background: white; padding: 4px; }
    .qr-container small { margin-top: 8px; color: #8f83ab; font-size: 11px; }

    button.btn-primary { background: #7c4dff; color: #ffffff; border: 0; border-radius: 10px; padding: 12px 20px; font-size: 14px; font-weight: 700; cursor: pointer; transition: background 0.15s; }
    button.btn-primary:hover:not(:disabled) { background: #9667ff; }
    button.btn-primary:disabled { background: #3b3252; color: #776a91; cursor: not-allowed; }
    button.btn-secondary { background: #261c3d; color: #d8c9ff; border: 1px solid #533980; border-radius: 10px; padding: 10px 16px; font-size: 13px; font-weight: 600; cursor: pointer; }
    button.btn-secondary:hover { background: #34264f; }
    button.btn-danger { background: #4a1c24; color: #ff8595; border: 1px solid #803943; border-radius: 8px; padding: 6px 12px; font-size: 12px; cursor: pointer; }
    button.btn-danger:hover { background: #61222e; }

    input, select { box-sizing: border-box; width: 100%; background: #0c0a14; color: white; border: 1px solid #4a366e; border-radius: 10px; padding: 12px; margin: 6px 0 14px; font: inherit; font-size: 14px; }
    input:focus, select:focus { outline: none; border-color: #9667ff; box-shadow: 0 0 0 2px rgba(150,103,255,0.25); }
    label { font-size: 13px; font-weight: 600; color: #d8c9ff; display: block; }
    .field-hint { font-size: 11px; color: #8f83ab; margin-top: -10px; margin-bottom: 12px; }

    .grid-2 { display: grid; grid-template-columns: 1fr; gap: 14px; }
    @media (min-width: 600px) { .grid-2 { grid-template-columns: 1fr 1fr; } }

    .toast { display: none; padding: 12px 18px; border-radius: 10px; margin-bottom: 16px; font-size: 13px; font-weight: 600; }
    .toast.success { display: block; background: #133a2a; border: 1px solid #21875c; color: #78e8b4; }
    .toast.error { display: block; background: #4a1c24; border: 1px solid #803943; color: #ff8595; }

    a { color: #bca1ff; text-decoration: none; }
    a:hover { text-decoration: underline; }
    ol { line-height: 1.8; color: #cfc6e4; padding-left: 20px; }
  </style>
</head>
<body>
<main>
  <header>
    <h1>Movie Night VR</h1>
    <p>Multi-provider cinema server for Meta Quest with hardware transcoding and zero-config streaming.</p>
  </header>

  <nav class="nav-tabs">
    <button id="tabBtn-hub" class="nav-btn active" type="button" onclick="switchTab('hub')">🎬 VR Hub & Invite</button>
    <button id="tabBtn-settings" class="nav-btn" type="button" onclick="switchTab('settings')">⚙️ Server Settings</button>
    <button id="tabBtn-remote" class="nav-btn" type="button" onclick="switchTab('remote')">📱 Room Remote</button>
  </nav>

  <!-- TAB 1: HUB & INVITE -->
  <div id="tab-hub">
    <section class="card">
      <h2 style="margin-top:0;">Connect Headset to Room</h2>
      <div class="badges">
        <span id="transcoderBadge" class="badge">Detecting Transcoder…</span>
        <span id="tunnelBadge" class="badge">Checking Tunnel…</span>
        <span id="roomsBadge" class="badge">Active Rooms: 0</span>
      </div>

      <div class="share-row">
        <div>
          <p id="status" style="color:#78e8b4; font-weight:500;">Checking server access link…</p>
          <label for="link">Server Public URL (Headset connects to this)</label>
          <input id="link" readonly placeholder="Waiting for Cloudflare or network URL…">
          <label for="room">Room Code (optional)</label>
          <input id="room" maxlength="12" placeholder="e.g. 4-letter room code or leave blank">
          <button id="copy" class="btn-primary" disabled>Copy invite</button>
        </div>

        <div class="qr-container" id="qrWrapper" style="display:none;">
          <img id="qrImg" alt="Room QR Code">
          <small>Scan with phone or Quest browser</small>
        </div>
      </div>
    </section>

    <section class="card">
      <h3>How to Join in VR</h3>
      <ol>
        <li>Put on your <strong>Meta Quest</strong> headset and launch Movie Night.</li>
        <li>Open <strong>My Room</strong> → enter your server URL (or leave default on local network).</li>
        <li>Click <strong>Connect</strong> and sit back on the couch with your friends!</li>
      </ol>
    </section>
  </div>

  <!-- TAB 2: SERVER SETTINGS (ADMIN PANEL) -->
  <div id="tab-settings" style="display:none;">
    <div id="settingsToast" class="toast"></div>

    <!-- STREMIO ACCOUNT SYNC -->
    <section class="card">
      <h2 style="margin-top:0;">Stremio Account & Addon Sync</h2>
      <p>Log in with your Stremio account to automatically sync your catalogs, Torrentio, and Debrid configuration to this server.</p>
      
      <div id="stremioLoggedOut" style="display:none;">
        <div style="display:flex; flex-direction:column; gap:12px; align-items:flex-start;">
          <button type="button" id="startLinkBtn" class="btn-primary" onclick="startStremioLink()">🔗 Quick Link Stremio Account</button>
          <div id="linkPrompt" style="display:none; background:#0c0a14; border:1px solid #4a366e; border-radius:12px; padding:16px; width:100%; box-sizing:border-box;">
            <p style="margin:0 0 10px; font-weight:600;">1. Open the link or scan QR code on your phone/browser:</p>
            <div style="display:flex; gap:16px; align-items:center; flex-wrap:wrap;">
              <a id="linkUrlAnchor" href="#" target="_blank" rel="noopener" class="btn-secondary" style="font-weight:700;">Open Stremio Link</a>
              <span id="linkCodeBadge" class="badge active" style="font-size:16px; padding:8px 16px; font-family:monospace;">CODE</span>
              <img id="linkQrImg" style="width:100px; height:100px; border-radius:8px; background:white; padding:4px;" alt="QR Code">
            </div>
            <p style="color:#78e8b4; font-size:12px; margin:12px 0 0;">Waiting for you to click "Authorize" on Stremio…</p>
          </div>
        </div>
      </div>

      <div id="stremioLoggedIn" style="display:none;">
        <div style="display:flex; justify-content:space-between; align-items:center; background:#133a2a; border:1px solid #21875c; padding:14px; border-radius:12px;">
          <div>
            <div style="font-weight:700; color:#78e8b4;" id="stremioUserName">Signed in as Stremio User</div>
            <div style="font-size:12px; color:#a3e0c4;" id="stremioAddonSummary">Addons and catalogs synced automatically.</div>
          </div>
          <button type="button" class="btn-danger" onclick="unlinkStremio()">Disconnect Account</button>
        </div>
      </div>
    </section>

    <!-- NETWORK & TUNNELS -->
    <section class="card">
      <h2 style="margin-top:0;">Remote Access & Network Tunnels</h2>
      <p>Configure remote access to reach your Movie Night VR server from outside your local home network.</p>
      
      <label for="cfgTunnelToken">Cloudflare Named Tunnel Token (Zero Trust)</label>
      <input id="cfgTunnelToken" placeholder="eyJ... Cloudflare Zero Trust Tunnel Token">
      <div class="field-hint">Paste your Cloudflare Tunnel token here if using an Argo/Zero Trust named tunnel.</div>

      <div class="grid-2">
        <div>
          <label for="cfgTunnelUrl">Custom Cloudflare Domain / Public URL</label>
          <input id="cfgTunnelUrl" placeholder="https://cinema.yourdomain.com">
          <div class="field-hint">Public domain mapped in Cloudflare Zero Trust.</div>
        </div>
        <div>
          <label for="cfgTailscaleUrl">Tailscale MagicDNS / IP URL</label>
          <input id="cfgTailscaleUrl" placeholder="http://movie-night.your-tailnet.ts.net:3000">
          <div class="field-hint">Tailscale address if accessing via private Tailnet mesh.</div>
        </div>
      </div>
    </section>

    <!-- MEDIA COMPANION & QUALITY -->
    <section class="card">
      <h2 style="margin-top:0;">Streaming Companion & Transcoder</h2>
      
      <label for="cfgStreamingUrl">Companion Engine URL</label>
      <input id="cfgStreamingUrl" placeholder="http://media:11470 or http://stremio:11470">
      <div class="field-hint">Internal docker companion host (default: http://media:11470).</div>

      <div class="grid-2">
        <div>
          <label for="cfgMaxHeight">Maximum Video Resolution</label>
          <select id="cfgMaxHeight">
            <option value="720">720p (HD — Best for remote connections)</option>
            <option value="1080" selected>1080p (Full HD — Recommended)</option>
            <option value="2160">2160p (4K Ultra HD)</option>
          </select>
        </div>
        <div>
          <label for="cfgMaxFps">Target Framerate</label>
          <select id="cfgMaxFps">
            <option value="30">30 FPS</option>
            <option value="60" selected>60 FPS (Smooth)</option>
          </select>
        </div>
      </div>
    </section>

    <!-- EXTERNAL SERVERS -->
    <section class="card">
      <h2 style="margin-top:0;">External Servers & Subtitles (Optional)</h2>
      <label for="cfgJellyfinUrl">Jellyfin / Emby Server URL</label>
      <input id="cfgJellyfinUrl" placeholder="e.g. http://192.168.1.100:8096">

      <label for="cfgOpenSubtitlesKey">OpenSubtitles.com API Key</label>
      <input id="cfgOpenSubtitlesKey" placeholder="Optional OpenSubtitles API key for automatic subtitles">
    </section>

    <button id="saveSettingsBtn" class="btn-primary" onclick="saveSettings()" style="width:100%; padding:14px; font-size:15px;">💾 Save Server Settings</button>
  </div>

  <!-- TAB 3: ROOM REMOTE -->
  <div id="tab-remote" style="display:none;">
    <div id="remoteToast" class="toast"></div>

    <section class="card">
      <h2 style="margin-top:0;">Send Video to VR Screen</h2>
      <p>Play any direct stream, web video, or movie link directly in the headset room from your phone or browser.</p>

      <label for="remoteVideoUrl">Video Stream URL</label>
      <input id="remoteVideoUrl" placeholder="https://example.com/movie.mp4 or HLS m3u8 stream">
      
      <label for="remoteVideoTitle">Title (optional)</label>
      <input id="remoteVideoTitle" placeholder="e.g. Inception (1080p)">

      <button type="button" class="btn-primary" onclick="playInVR()" style="width:100%;">▶️ Play on VR Screen</button>
    </section>

    <section class="card">
      <h3>Active Rooms Status</h3>
      <div id="activeRoomsList">
        <p style="color:#8f83ab; font-size:13px;">Loading active rooms…</p>
      </div>
    </section>
  </div>

  <p style="text-align:center; margin-top:32px; font-size:13px; color:#8f83ab;">
    <a href="https://github.com/oOflyeyesOo/Media-VR" target="_blank" rel="noopener">Movie Night VR</a> · Open-Source Self-Hosted Cinema
  </p>
</main>

<script>
  let serverConfig = {};
  let stremioPollTimer = null;

  function switchTab(tabId) {
    document.querySelectorAll('.nav-btn').forEach(b => b.classList.remove('active'));
    document.querySelectorAll('#tab-hub, #tab-settings, #tab-remote').forEach(d => d.style.display = 'none');
    
    const panel = document.getElementById('tab-' + tabId);
    if (panel) panel.style.display = 'block';
    const btn = document.getElementById('tabBtn-' + tabId);
    if (btn) btn.classList.add('active');

    if (tabId === 'settings') loadSettings();
    if (tabId === 'remote') loadRooms();
  }

  async function loadSettings() {
    try {
      const res = await fetch('/api/config');
      if (res.ok) {
        const data = await res.json();
        serverConfig = data.config || {};
        document.getElementById('cfgStreamingUrl').value = serverConfig.streamingServiceUrl || '';
        document.getElementById('cfgTunnelToken').value = serverConfig.tunnelToken || '';
        document.getElementById('cfgTunnelUrl').value = serverConfig.tunnelUrl || '';
        document.getElementById('cfgTailscaleUrl').value = serverConfig.tailscaleUrl || '';
        document.getElementById('cfgMaxHeight').value = serverConfig.maxHeight || 1080;
        document.getElementById('cfgMaxFps').value = serverConfig.maxFps || 60;
        document.getElementById('cfgJellyfinUrl').value = serverConfig.jellyfinUrl || '';
        document.getElementById('cfgOpenSubtitlesKey').value = serverConfig.openSubtitlesApiKey || '';
        renderStremioStatus();
      }
    } catch (err) {
      console.error('Failed to load config:', err);
    }
  }

  function renderStremioStatus() {
    const loggedIn = Boolean(serverConfig.stremioAuthKey);
    document.getElementById('stremioLoggedIn').style.display = loggedIn ? 'block' : 'none';
    document.getElementById('stremioLoggedOut').style.display = loggedIn ? 'none' : 'block';
    if (loggedIn) {
      document.getElementById('stremioUserName').textContent = '✓ Connected as: ' + (serverConfig.stremioUser || 'Stremio Account');
      const count = Array.isArray(serverConfig.addons) ? serverConfig.addons.length : 0;
      document.getElementById('stremioAddonSummary').textContent = count + ' add-on(s) and catalogs synced and ready for VR.';
    }
  }

  async function startStremioLink() {
    const btn = document.getElementById('startLinkBtn');
    btn.disabled = true;
    btn.textContent = 'Requesting Link Code…';
    try {
      const res = await fetch('/api/stremio/link', { method: 'POST' });
      const data = await res.json();
      if (data.ok && data.code) {
        document.getElementById('linkPrompt').style.display = 'block';
        document.getElementById('linkCodeBadge').textContent = data.code;
        document.getElementById('linkUrlAnchor').href = data.link;
        if (data.qrcode) document.getElementById('linkQrImg').src = data.qrcode;
        
        // Start polling for authorization
        if (stremioPollTimer) clearInterval(stremioPollTimer);
        stremioPollTimer = setInterval(async () => {
          try {
            const pRes = await fetch('/api/stremio/poll?code=' + encodeURIComponent(data.code));
            const pData = await pRes.json();
            if (pData.ok && pData.linked) {
              clearInterval(stremioPollTimer);
              document.getElementById('linkPrompt').style.display = 'none';
              loadSettings();
            }
          } catch {}
        }, 3000);
      } else {
        alert(data.error || 'Failed to start Stremio link');
      }
    } catch (e) {
      alert('Error: ' + e.message);
    } finally {
      btn.disabled = false;
      btn.textContent = '🔗 Quick Link Stremio Account';
    }
  }

  async function unlinkStremio() {
    if (!confirm('Disconnect Stremio account?')) return;
    try {
      await fetch('/api/stremio/unlink', { method: 'POST' });
      loadSettings();
    } catch (e) {
      alert('Error disconnecting: ' + e.message);
    }
  }

  async function saveSettings() {
    const toast = document.getElementById('settingsToast');
    const btn = document.getElementById('saveSettingsBtn');
    btn.disabled = true;
    btn.textContent = 'Saving…';

    const payload = {
      streamingServiceUrl: document.getElementById('cfgStreamingUrl').value.trim(),
      tunnelToken: document.getElementById('cfgTunnelToken').value.trim(),
      tunnelUrl: document.getElementById('cfgTunnelUrl').value.trim(),
      tailscaleUrl: document.getElementById('cfgTailscaleUrl').value.trim(),
      maxHeight: parseInt(document.getElementById('cfgMaxHeight').value, 10),
      maxFps: parseInt(document.getElementById('cfgMaxFps').value, 10),
      jellyfinUrl: document.getElementById('cfgJellyfinUrl').value.trim(),
      openSubtitlesApiKey: document.getElementById('cfgOpenSubtitlesKey').value.trim()
    };

    try {
      const res = await fetch('/api/config', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (res.ok) {
        toast.className = 'toast success';
        toast.textContent = '✓ Server settings successfully saved to /share/config.json!';
        setTimeout(() => toast.style.display = 'none', 4000);
      } else {
        throw new Error('Save failed');
      }
    } catch (e) {
      toast.className = 'toast error';
      toast.textContent = 'Failed to save settings: ' + e.message;
    } finally {
      btn.disabled = false;
      btn.textContent = '💾 Save Server Settings';
    }
  }

  async function playInVR() {
    const toast = document.getElementById('remoteToast');
    const url = document.getElementById('remoteVideoUrl').value.trim();
    const title = document.getElementById('remoteVideoTitle').value.trim();
    if (!url) { alert('Please enter a video URL'); return; }

    try {
      const res = await fetch('/api/room/play', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ url, title })
      });
      const data = await res.json();
      if (res.ok) {
        toast.className = 'toast success';
        toast.textContent = '▶️ Now playing "' + (data.title || 'video') + '" in room ' + (data.room || '') + '!';
        document.getElementById('remoteVideoUrl').value = '';
        setTimeout(() => toast.style.display = 'none', 4000);
      } else {
        toast.className = 'toast error';
        toast.textContent = data.error || 'Failed to play in room';
      }
    } catch (e) {
      toast.className = 'toast error';
      toast.textContent = 'Error: ' + e.message;
    }
  }

  async function loadRooms() {
    const listEl = document.getElementById('activeRoomsList');
    try {
      const res = await fetch('/health');
      if (res.ok) {
        const h = await res.json();
        const count = h.rooms || 0;
        if (count === 0) {
          listEl.innerHTML = '<p style="color:#8f83ab; font-size:13px;">No active VR rooms right now. Launch the app in Quest to join!</p>';
        } else {
          listEl.innerHTML = '<p style="color:#78e8b4; font-size:13px; font-weight:600;">● ' + count + ' active room(s) connected.</p>';
        }
      }
    } catch {}
  }

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
    const invite = 'Join me in Movie Night VR!\\nServer: ' + link.value + (room ? '\\nRoom: ' + room : '');
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
