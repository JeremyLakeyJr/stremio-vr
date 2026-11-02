import { readFile } from 'node:fs/promises';
import { getConfig } from './config.js';

export async function hostingInfo(file = process.env.TUNNEL_LINK_FILE, tunnelMgr = null) {
  const cfg = getConfig();
  const tsUrl = cfg.tailscaleUrl || process.env.TAILSCALE_URL || (process.env.TS_HOSTNAME ? `https://${process.env.TS_HOSTNAME}` : null);
  if (tsUrl && /^https?:\/\/[a-z0-9.-]+(?::[0-9]+)?(?:\/.*)?$/i.test(tsUrl)) {
    return { url: tsUrl, online: true, mode: 'tailscale' };
  }

  if (tunnelMgr) {
    const tStatus = tunnelMgr.getStatus();
    if (tStatus.hasToken) {
      const activeUrl = cfg.tunnelUrl || tStatus.url || '';
      if (tStatus.error) {
        return { url: activeUrl, online: false, mode: 'named', error: tStatus.error };
      }
      if (tStatus.connected) {
        return { url: activeUrl, online: true, mode: 'named', connected: true };
      }
      if (tStatus.running) {
        return { url: activeUrl, online: false, starting: true, mode: 'named', message: 'Connecting to Cloudflare edge...' };
      }
      return { url: activeUrl, online: false, mode: 'named', message: 'Tunnel process idle' };
    }
  }

  try {
    const data = JSON.parse(await readFile(file, 'utf8'));
    if (data.url && data.url !== 'https://api.trycloudflare.com') {
      const isFresh = Number.isFinite(data.updatedAt) && Date.now() - data.updatedAt < 20000 && data.updatedAt <= Date.now() + 5000;
      const isTryCloudflare = /^https:\/\/[a-z0-9-]+\.trycloudflare\.com$/.test(data.url);
      const isSafeHttps = /^https:\/\/[a-z0-9.-]+(?::[0-9]+)?(?:\/.*)?$/i.test(data.url);
      const isNamedTunnel = data.mode === 'named' || Boolean(cfg.tunnelToken || process.env.TUNNEL_TOKEN);

      if (data.connected || isFresh) {
        if (isTryCloudflare || (isNamedTunnel && isSafeHttps)) {
          const isOnline = data.running !== false && (Boolean(data.connected) || isFresh);
          return {
            url: cfg.tunnelUrl || data.url,
            online: isOnline,
            mode: isNamedTunnel ? 'named' : 'quick',
            connected: Boolean(data.connected),
            error: data.error || null
          };
        }
      }
    }
  } catch {}

  const customTunnel = cfg.tunnelUrl || process.env.TUNNEL_URL;
  if (customTunnel && /^https?:\/\/[a-z0-9.-]+(?::[0-9]+)?(?:\/.*)?$/i.test(customTunnel)) {
    if (!cfg.tunnelToken && !process.env.TUNNEL_TOKEN) {
      return { url: customTunnel, online: true, mode: 'named' };
    }
  }

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

    .card { background: #181326; border: 1px solid #34264f; padding: 22px; border-radius: 18px; margin-bottom: 22px; }
    .card h2 { margin: 0 0 6px; font-size: 19px; color: #f5f2fd; }
    .card p.card-desc { margin: 0 0 16px; color: #a99ebd; font-size: 13px; line-height: 1.5; }
    .badges { display: flex; flex-wrap: wrap; gap: 8px; margin: 10px 0 16px; }
    .badge { background: #261c3d; border: 1px solid #533980; padding: 6px 12px; border-radius: 999px; font-size: 12px; color: #d8c9ff; font-weight: 500; }
    .badge.active { background: #133a2a; border-color: #21875c; color: #78e8b4; }
    .badge.offline { background: #351a24; border-color: #7a2b3d; color: #ff94a4; }

    .guide-box { background: #100b1d; border: 1px solid #2e2445; border-left: 3px solid #7c4dff; padding: 14px 16px; border-radius: 10px; margin-bottom: 16px; font-size: 13px; color: #c8bee0; line-height: 1.6; }
    .guide-box strong { color: #ffffff; }
    .guide-box ol, .guide-box ul { margin: 6px 0 0; padding-left: 18px; }
    .guide-box code { background: #1c1430; padding: 2px 6px; border-radius: 4px; font-size: 12px; color: #d8c9ff; }

    .server-status-pill { display: inline-flex; align-items: center; gap: 6px; font-size: 11px; font-weight: 700; padding: 4px 10px; border-radius: 999px; text-transform: uppercase; letter-spacing: 0.5px; }
    .server-status-pill.online { background: #0f3624; color: #4ade80; border: 1px solid #166534; }
    .server-status-pill.offline { background: #3b1219; color: #f87171; border: 1px solid #991b1b; }
    .server-status-pill.unconfigured { background: #1a1727; color: #9488b0; border: 1px solid #342a4d; }
    .server-status-pill.checking { background: #2d2212; color: #facc15; border: 1px solid #854d0e; }

    .server-card { background: linear-gradient(180deg, #181329 0%, #120e20 100%); border: 1px solid #34264f; border-radius: 14px; padding: 18px 20px; margin-bottom: 16px; transition: border-color 0.2s, box-shadow 0.2s; }
    .server-card:hover { border-color: #553e80; }
    .server-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px; flex-wrap: wrap; gap: 8px; }
    .server-title-group { display: flex; align-items: center; gap: 10px; }
    .server-icon-badge { width: 32px; height: 32px; border-radius: 8px; display: flex; align-items: center; justify-content: center; font-size: 16px; }

    .input-with-action { display: flex; gap: 8px; align-items: center; margin-top: 6px; margin-bottom: 14px; }
    .input-with-action input { margin: 0; flex: 1; }
    .btn-test { background: #251c3d; border: 1px solid #4d3678; color: #d8c9ff; border-radius: 10px; padding: 12px 14px; font-size: 12px; font-weight: 600; cursor: pointer; white-space: nowrap; transition: all 0.15s; }
    .btn-test:hover { background: #39285c; border-color: #7c4dff; color: #ffffff; }

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

    <!-- 1. ALL-IN-ONE MEDIA BACKENDS & SERVERS -->
    <section class="card">
      <div style="display:flex; justify-content:space-between; align-items:center; flex-wrap:wrap; margin-bottom:6px;">
        <h2 style="margin:0;">🎬 Media Servers & Backends</h2>
        <button type="button" class="btn-secondary" onclick="checkProviderStatuses()" style="padding:6px 12px; font-size:12px;">🔄 Check Status</button>
      </div>
      <p class="card-desc">Configure your media backends. All video streams are hardware-transcoded into unified VR audio/video channels.</p>

      <div class="guide-box">
        <strong>Supported Media Servers:</strong>
        <p style="margin:4px 0 0;">Movie Night connects to your existing home lab servers. Enter the LAN or Tailscale address and credentials below. The companion transcoder automatically proxies and synchronizes video playback to everyone in the room.</p>
      </div>

      <!-- STREMIO & ADDON SYNC -->
      <div class="server-card">
        <div class="server-header">
          <div class="server-title-group">
            <span class="server-icon-badge" style="background:#2e1b4d; border:1px solid #5a3699;">🟣</span>
            <div>
              <h3 style="margin:0; font-size:16px; color:#f0ebfa;">Stremio & Debrid Catalogs</h3>
              <div style="font-size:11px; color:#a99ebd;">Torrentio, Real-Debrid, Cinemeta, and custom add-on sync</div>
            </div>
          </div>
          <span id="badge-stremio" class="server-status-pill unconfigured">Checking…</span>
        </div>

        <div id="stremioLoggedOut" style="display:none; margin-top:12px;">
          <div style="display:flex; align-items:center; gap:12px; flex-wrap:wrap; margin-bottom:10px;">
            <button type="button" id="startLinkBtn" class="btn-primary" onclick="startStremioLink()" style="font-size:13px; padding:10px 18px;">🔗 Quick Link Stremio Account</button>
            <label style="display:flex; align-items:center; gap:8px; font-size:12px; color:#dcd2ee; cursor:pointer;">
              <input type="checkbox" id="stremioEphemeralCheckbox" checked style="accent-color:#7958ff; width:16px; height:16px;">
              <span><strong>Ephemeral Session</strong> (zero server disk storage; automatically purged when host leaves)</span>
            </label>
          </div>
          <div id="linkPrompt" style="display:none; background:#0c0a14; border:1px solid #4a366e; border-radius:12px; padding:16px; margin-top:12px; width:100%; box-sizing:border-box;">
            <p style="margin:0 0 10px; font-weight:600; font-size:13px;">1. Open the link or scan QR code on your phone/browser:</p>
            <div style="display:flex; gap:16px; align-items:center; flex-wrap:wrap;">
              <a id="linkUrlAnchor" href="#" target="_blank" rel="noopener" class="btn-secondary" style="font-weight:700;">Open Stremio Link</a>
              <span id="linkCodeBadge" class="badge active" style="font-size:16px; padding:8px 16px; font-family:monospace;">CODE</span>
              <img id="linkQrImg" style="width:100px; height:100px; border-radius:8px; background:white; padding:4px;" alt="QR Code">
            </div>
            <p style="color:#78e8b4; font-size:12px; margin:12px 0 0;">Waiting for you to click "Authorize" on Stremio…</p>
          </div>
        </div>

        <div id="stremioLoggedIn" style="display:none; margin-top:12px;">
          <div style="display:flex; justify-content:space-between; align-items:center; background:#133a2a; border:1px solid #21875c; padding:12px 16px; border-radius:10px;">
            <div>
              <div style="font-weight:700; color:#78e8b4;" id="stremioUserName">Signed in as Stremio User</div>
              <div style="font-size:12px; color:#a3e0c4;" id="stremioAddonSummary">Addons and catalogs synced automatically.</div>
              <div id="stremioSessionTypeBadge" style="font-size:11px; margin-top:4px; color:#ffd166;"></div>
            </div>
            <button type="button" class="btn-danger" onclick="unlinkStremio()">Disconnect / Forget</button>
          </div>
        </div>
      </div>

      <!-- MEDIASTORM -->
      <div class="server-card">
        <div class="server-header">
          <div class="server-title-group">
            <span class="server-icon-badge" style="background:#3b2a12; border:1px solid #855b1a;">⚡</span>
            <div>
              <h3 style="margin:0; font-size:16px; color:#f0ebfa;">MediaStorm</h3>
              <div style="font-size:11px; color:#a99ebd;">Debrid & Usenet streaming backend (godver3/mediastorm)</div>
            </div>
          </div>
          <span id="badge-mediastorm" class="server-status-pill unconfigured">Not Configured</span>
        </div>
        <div class="grid-2" style="margin-top:10px;">
          <div>
            <label for="cfgMediastormUrl">MediaStorm URL</label>
            <div class="input-with-action">
              <input id="cfgMediastormUrl" placeholder="e.g. https://media.mightysprite.dev">
              <button type="button" class="btn-test" onclick="testProvider('mediastorm', 'cfgMediastormUrl', 'cfgMediastormKey')">Ping</button>
            </div>
            <div class="field-hint">Public domain, Tailscale address, or <code>http://host.docker.internal:7777</code>.</div>
          </div>
          <div>
            <label for="cfgMediastormKey">MediaStorm API Key (Optional)</label>
            <input id="cfgMediastormKey" placeholder="Leave blank if unauthenticated">
            <div class="field-hint">Only required if your MediaStorm instance sits behind an authenticated proxy.</div>
          </div>
        </div>
      </div>

      <!-- JELLYFIN / EMBY -->
      <div class="server-card">
        <div class="server-header">
          <div class="server-title-group">
            <span class="server-icon-badge" style="background:#152b47; border:1px solid #23548f;">🔵</span>
            <div>
              <h3 style="margin:0; font-size:16px; color:#f0ebfa;">Jellyfin / Emby Media Server</h3>
              <div style="font-size:11px; color:#a99ebd;">Personal self-hosted home media library</div>
            </div>
          </div>
          <span id="badge-jellyfin" class="server-status-pill unconfigured">Not Configured</span>
        </div>
        <div style="margin-top:10px;">
          <label for="cfgJellyfinUrl">Jellyfin Server URL</label>
          <div class="input-with-action">
            <input id="cfgJellyfinUrl" placeholder="e.g. http://192.168.1.100:8096 or https://jellyfin.yourdomain.com">
            <button type="button" class="btn-test" onclick="testProvider('jellyfin', 'cfgJellyfinUrl')">Ping</button>
          </div>
          <div class="field-hint">LAN IP, Tailscale domain, or public URL. Streams and metadata resolve natively.</div>
        </div>
      </div>

      <!-- SILO -->
      <div class="server-card">
        <div class="server-header">
          <div class="server-title-group">
            <span class="server-icon-badge" style="background:#133329; border:1px solid #206b52;">🟢</span>
            <div>
              <h3 style="margin:0; font-size:16px; color:#f0ebfa;">Silo Streaming Backend</h3>
              <div style="font-size:11px; color:#a99ebd;">High-performance Go debrid & torrent streaming server</div>
            </div>
          </div>
          <span id="badge-silo" class="server-status-pill unconfigured">Not Configured</span>
        </div>
        <div class="grid-2" style="margin-top:10px;">
          <div>
            <label for="cfgSiloUrl">Silo Backend URL</label>
            <div class="input-with-action">
              <input id="cfgSiloUrl" placeholder="e.g. http://192.168.1.100:8082">
              <button type="button" class="btn-test" onclick="testProvider('silo', 'cfgSiloUrl', 'cfgSiloToken')">Ping</button>
            </div>
            <div class="field-hint">Internal or LAN address running Silo debrid bridge.</div>
          </div>
          <div>
            <label for="cfgSiloToken">Silo Auth Token (Optional)</label>
            <input id="cfgSiloToken" placeholder="Bearer auth token">
            <div class="field-hint">Bearer token if your Silo server requires authentication.</div>
          </div>
        </div>
      </div>

      <!-- REMUX -->
      <div class="server-card">
        <div class="server-header">
          <div class="server-title-group">
            <span class="server-icon-badge" style="background:#3b1e11; border:1px solid #733c20;">🟠</span>
            <div>
              <h3 style="margin:0; font-size:16px; color:#f0ebfa;">Remux Service</h3>
              <div style="font-size:11px; color:#a99ebd;">Lossless direct video remuxing daemon (lostb1t/remux)</div>
            </div>
          </div>
          <span id="badge-remux" class="server-status-pill unconfigured">Not Configured</span>
        </div>
        <div style="margin-top:10px;">
          <label for="cfgRemuxUrl">Remux Service URL</label>
          <div class="input-with-action">
            <input id="cfgRemuxUrl" placeholder="e.g. http://remux:8096 or http://192.168.1.100:8096">
            <button type="button" class="btn-test" onclick="testProvider('remux', 'cfgRemuxUrl')">Ping</button>
          </div>
          <div class="field-hint">Zero-transcoding remux gateway for high-fidelity instant streaming.</div>
        </div>
      </div>
    </section>

    <!-- 2. TRANSCODER LIMITS ACROSS ALL BACKENDS -->
    <section class="card">
      <h2>⚡ Hardware Transcoder & Video Quality</h2>
      <p class="card-desc">Applies universally to Stremio, Jellyfin, MediaStorm, Silo, Remux, and direct stream URLs.</p>
      
      <div class="grid-2">
        <div>
          <label for="cfgMaxHeight">Maximum Video Resolution</label>
          <select id="cfgMaxHeight">
            <option value="720">720p (HD — Best for slower remote connections)</option>
            <option value="1080" selected>1080p (Full HD — Recommended for Meta Quest 2 / 3 / Pro)</option>
            <option value="1440">1440p (2K Quad HD)</option>
            <option value="2160">2160p (4K Ultra HD — High LAN bandwidth required)</option>
          </select>
          <div class="field-hint">Streams exceeding this resolution are automatically transcoded down to preserve smooth VR frame rates.</div>
        </div>
        <div>
          <label for="cfgMaxFps">Target Framerate</label>
          <select id="cfgMaxFps">
            <option value="24">24 FPS (Cinematic standard)</option>
            <option value="30">30 FPS (TV standard)</option>
            <option value="60" selected>60 FPS (Super smooth VR playback)</option>
          </select>
          <div class="field-hint">Matches Meta Quest native display refresh rate for jitter-free viewing.</div>
        </div>
      </div>

      <label for="cfgStreamingUrl">Internal Companion Engine URL</label>
      <input id="cfgStreamingUrl" placeholder="http://media:11470">
      <div class="field-hint">Internal docker companion engine (default: <code>http://media:11470</code>).</div>
    </section>

    <!-- 3. REMOTE ACCESS & NETWORK (CLOUDFLARE & TAILSCALE) -->
    <section class="card">
      <h2>🌐 Remote Access: Cloudflare Tunnel & Tailscale Mesh</h2>
      <p class="card-desc">Access your VR cinema with friends outside your house without opening router ports or exposing your home IP.</p>

      <!-- CLOUDFLARE INSTRUCTIONS & CONFIG -->
      <div class="guide-box">
        <strong>☁️ Cloudflare Named Tunnel Setup Guide:</strong>
        <ol>
          <li>Open your <strong>Cloudflare Zero Trust Dashboard</strong> (<code>one.dash.cloudflare.com</code>) → <strong>Networks</strong> → <strong>Tunnels</strong>.</li>
          <li>Click <strong>Create a Tunnel</strong> → choose <strong>Cloudflared</strong> → name it <code>movie-night</code>.</li>
          <li>Copy the <strong>Tunnel Token</strong> (starts with <code>eyJh...</code>) and paste it into the <strong>Cloudflare Named Tunnel Token</strong> field below.</li>
          <li>In Cloudflare Tunnel's <strong>Public Hostname</strong> tab, add your domain (e.g. <code>vr.mightysprite.dev</code>) pointing to <strong>Service:</strong> <code>HTTP://localhost:3000</code> (or <code>HTTP://movie-night:3000</code>).</li>
          <li>Enter your custom domain in the <strong>Custom Cloudflare Domain</strong> field below and click <strong>Save Server Settings</strong>. Movie Night will immediately start the connector and activate the tunnel!</li>
        </ol>
        <div class="field-hint" style="margin-top: 8px; color: #a3b8cc;">
          💡 <em>Note:</em> Movie Night runs the <code>cloudflared</code> connector right inside the container. Both <code>HTTP://localhost:3000</code> and <code>HTTP://movie-night:3000</code> route directly to Movie Night without port forwarding.
        </div>
      </div>

      <label for="cfgTunnelToken">Cloudflare Named Tunnel Token (Zero Trust)</label>
      <input id="cfgTunnelToken" placeholder="eyJ... Cloudflare Zero Trust Tunnel Token">
      <div class="field-hint">Paste your Cloudflare Tunnel token here if using an Argo/Zero Trust named tunnel.</div>

      <label for="cfgTunnelUrl">Custom Cloudflare Domain / Public URL</label>
      <input id="cfgTunnelUrl" placeholder="https://cinema.yourdomain.com">
      <div class="field-hint">Your public domain mapped in Cloudflare Zero Trust. Used to generate Quest join QR codes.</div>

      <!-- TAILSCALE INSTRUCTIONS & CONFIG -->
      <div class="guide-box" style="border-left-color: #00d084; margin-top:20px;">
        <strong>🔒 Tailscale Private Mesh Setup Guide:</strong>
        <ol>
          <li>In <strong>DockTun</strong> (or your host server), connect the host to Tailscale via <code>tailscale up</code> or DockTun's Tailscale integration.</li>
          <li>Open your <strong>Tailscale Admin Console</strong> (<code>login.tailscale.com/admin/machines</code>) to view your server's <strong>MagicDNS</strong> name (e.g. <code>server.tailnet.ts.net</code>) or <code>100.x.y.z</code> IP.</li>
          <li>On your <strong>Meta Quest</strong>, install the Tailscale APK (via SideQuest or Quest developer mode) and log into the same Tailnet.</li>
          <li>Enter your Tailscale URL below (e.g. <code>http://movie-night.tailnet.ts.net:3000</code>). Headsets on your Tailnet can connect securely with zero internet exposure!</li>
        </ol>
      </div>

      <label for="cfgTailscaleUrl">Tailscale MagicDNS / IP URL</label>
      <input id="cfgTailscaleUrl" placeholder="http://movie-night.your-tailnet.ts.net:3000">
      <div class="field-hint">Tailscale address for private, end-to-end encrypted mesh networking.</div>
    </section>

    <!-- 4. SUBTITLES & MISC -->
    <section class="card">
      <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:8px;">
        <h2 style="margin:0;">💬 Direct Video Subtitles (Optional Fallback)</h2>
        <span class="badge" style="font-size:11px;">Optional Fallback</span>
      </div>
      <p class="card-desc" style="margin-bottom:12px;">
        <strong>Note:</strong> Media backends like Stremio, Jellyfin, and MediaStorm automatically provide their own built-in subtitle streams. An OpenSubtitles.com API key is <em>only required as a fallback</em> when playing raw video files or direct links pasted into the Room Remote.
      </p>
      <label for="cfgOpenSubtitlesKey">OpenSubtitles.com REST API Key</label>
      <input id="cfgOpenSubtitlesKey" placeholder="Optional OpenSubtitles.com REST API Key">
      <div class="field-hint">Free accounts at <code>opensubtitles.com/consumers</code> provide 20 subtitle downloads/day for direct video URLs.</div>
    </section>

    <button id="saveSettingsBtn" class="btn-primary" onclick="saveSettings()" style="width:100%; padding:16px; font-size:16px;">💾 Save All Server Settings</button>
  </div>

  <!-- TAB 3: ROOM REMOTE -->
  <div id="tab-remote" style="display:none;">
    <div id="remoteToast" class="toast"></div>

    <section class="card">
      <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:8px;">
        <h2 style="margin:0;">🎬 Send Direct Video to VR Screen</h2>
        <span class="badge active" style="font-size:11px;">Quick Play</span>
      </div>
      <p class="card-desc">Play any direct stream, web video, or movie link directly in the headset room from your phone or browser.</p>

      <label for="remoteVideoUrl">Video Stream URL (MP4, MKV, HLS .m3u8)</label>
      <input id="remoteVideoUrl" placeholder="https://example.com/movie.mp4 or HLS m3u8 stream">
      
      <div class="grid-2">
        <div>
          <label for="remoteVideoTitle">Title (Optional)</label>
          <input id="remoteVideoTitle" placeholder="e.g. Inception (1080p)">
        </div>
        <div>
          <label for="remoteSubtitlesUrl">Subtitle Track URL (Optional, .vtt / .srt)</label>
          <input id="remoteSubtitlesUrl" placeholder="https://example.com/subtitles.vtt">
        </div>
      </div>

      <button type="button" class="btn-primary" onclick="playInVR()" style="width:100%; padding:14px; font-size:15px; margin-top:8px;">▶️ Play on VR Screen</button>
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
        document.getElementById('cfgJellyfinUrl').value = serverConfig.jellyfinUrl || '';
        document.getElementById('cfgMediastormUrl').value = serverConfig.mediastormUrl || '';
        document.getElementById('cfgMediastormKey').value = serverConfig.mediastormKey || '';
        document.getElementById('cfgSiloUrl').value = serverConfig.siloUrl || '';
        document.getElementById('cfgSiloToken').value = serverConfig.siloToken || '';
        document.getElementById('cfgRemuxUrl').value = serverConfig.remuxUrl || '';
        document.getElementById('cfgTunnelToken').value = serverConfig.tunnelToken || '';
        document.getElementById('cfgTunnelUrl').value = serverConfig.tunnelUrl || '';
        document.getElementById('cfgTailscaleUrl').value = serverConfig.tailscaleUrl || '';
        document.getElementById('cfgMaxHeight').value = serverConfig.maxHeight || 1080;
        document.getElementById('cfgMaxFps').value = serverConfig.maxFps || 60;
        document.getElementById('cfgOpenSubtitlesKey').value = serverConfig.openSubtitlesApiKey || '';
        try {
          const rRes = await fetch('/api/room/addons');
          if (rRes.ok) {
            const rData = await rRes.json();
            if (rData.ephemeral) activeEphemeralSession = rData;
          }
        } catch {}
        renderStremioStatus();
        checkProviderStatuses();
      }
    } catch (err) {
      console.error('Failed to load config:', err);
    }
  }

  async function checkProviderStatuses() {
    try {
      const res = await fetch('/api/providers');
      if (res.ok) {
        const data = await res.json();
        const p = data.providers || {};
        updatePill('badge-stremio', p.stremio?.online, p.stremio?.configured ? (p.stremio.online ? 'Online' : 'Offline') : 'Standby');
        updatePill('badge-jellyfin', p.jellyfin?.online, p.jellyfin?.configured ? (p.jellyfin.online ? 'Online' : 'Offline') : 'Not Configured');
        updatePill('badge-mediastorm', p.mediastorm?.online, p.mediastorm?.configured ? (p.mediastorm.online ? 'Online' : 'Offline') : 'Not Configured');
        updatePill('badge-silo', p.silo?.online, p.silo?.configured ? (p.silo.online ? 'Online' : 'Offline') : 'Not Configured');
        updatePill('badge-remux', p.remux?.online, p.remux?.configured ? (p.remux.online ? 'Online' : 'Offline') : 'Not Configured');
      }
    } catch {}
  }

  function updatePill(id, online, text) {
    const el = document.getElementById(id);
    if (!el) return;
    el.textContent = text;
    if (text === 'Not Configured' || text === 'Standby') {
      el.className = 'server-status-pill unconfigured';
    } else {
      el.className = 'server-status-pill ' + (online ? 'online' : 'offline');
    }
  }

  async function testProvider(type, urlId, keyId) {
    const url = document.getElementById(urlId)?.value.trim();
    const key = keyId ? document.getElementById(keyId)?.value.trim() : '';
    const badge = document.getElementById('badge-' + type);
    if (!url) {
      if (badge) {
        badge.textContent = 'Not Configured';
        badge.className = 'server-status-pill unconfigured';
      }
      showToast('settingsToast', 'Please enter a URL first', 'error');
      return;
    }
    if (badge) {
      badge.textContent = 'Pinging…';
      badge.className = 'server-status-pill checking';
    }
    const updates = {};
    if (type === 'mediastorm') { updates.mediastormUrl = url; if (key) updates.mediastormKey = key; }
    if (type === 'jellyfin') updates.jellyfinUrl = url;
    if (type === 'silo') { updates.siloUrl = url; if (key) updates.siloToken = key; }
    if (type === 'remux') updates.remuxUrl = url;
    try {
      await fetch('/api/config', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(updates)
      });
      await checkProviderStatuses();
      showToast('settingsToast', 'Tested ' + type + ' server connection', 'success');
    } catch (e) {
      if (badge) {
        badge.textContent = 'Error';
        badge.className = 'server-status-pill offline';
      }
      showToast('settingsToast', 'Ping failed: ' + e.message, 'error');
    }
  }

  let activeEphemeralSession = null;
  function renderStremioStatus() {
    const loggedIn = Boolean(serverConfig.stremioAuthKey) || Boolean(activeEphemeralSession?.ephemeral);
    document.getElementById('stremioLoggedIn').style.display = loggedIn ? 'block' : 'none';
    document.getElementById('stremioLoggedOut').style.display = loggedIn ? 'none' : 'block';
    if (loggedIn) {
      const user = activeEphemeralSession?.user || serverConfig.stremioUser || 'Stremio Account';
      const count = activeEphemeralSession?.addons?.length ?? (Array.isArray(serverConfig.addons) ? serverConfig.addons.length : 0);
      document.getElementById('stremioUserName').textContent = '✓ Connected as: ' + user;
      document.getElementById('stremioAddonSummary').textContent = count + ' add-on(s) and catalogs synced and ready for VR.';
      const badge = document.getElementById('stremioSessionTypeBadge');
      if (badge) {
        if (activeEphemeralSession?.ephemeral) {
          badge.textContent = '🛡️ Ephemeral Room Session (RAM only — server forgets immediately when room finishes)';
          badge.style.color = '#78e8b4';
        } else {
          badge.textContent = '💾 Saved permanently to server configuration';
          badge.style.color = '#a99ebd';
        }
      }
    }
  }

  async function startStremioLink() {
    const btn = document.getElementById('startLinkBtn');
    const isEphemeral = document.getElementById('stremioEphemeralCheckbox')?.checked ?? true;
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
        const pollUrl = '/api/stremio/poll?code=' + encodeURIComponent(data.code) + (isEphemeral ? '&ephemeral=true' : '');
        stremioPollTimer = setInterval(async () => {
          try {
            const pRes = await fetch(pollUrl);
            const pData = await pRes.json();
            if (pData.ok && pData.linked) {
              clearInterval(stremioPollTimer);
              document.getElementById('linkPrompt').style.display = 'none';
              if (pData.ephemeral) {
                activeEphemeralSession = { ephemeral: true, user: pData.user, addons: new Array(pData.addonCount || 0) };
              }
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
    if (!confirm('Disconnect Stremio account and forget session?')) return;
    try {
      activeEphemeralSession = null;
      await fetch('/api/stremio/unlink', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({}) });
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
      jellyfinUrl: document.getElementById('cfgJellyfinUrl').value.trim(),
      mediastormUrl: document.getElementById('cfgMediastormUrl').value.trim(),
      mediastormKey: document.getElementById('cfgMediastormKey').value.trim(),
      siloUrl: document.getElementById('cfgSiloUrl').value.trim(),
      siloToken: document.getElementById('cfgSiloToken').value.trim(),
      remuxUrl: document.getElementById('cfgRemuxUrl').value.trim(),
      tunnelToken: document.getElementById('cfgTunnelToken').value.trim(),
      tunnelUrl: document.getElementById('cfgTunnelUrl').value.trim(),
      tailscaleUrl: document.getElementById('cfgTailscaleUrl').value.trim(),
      maxHeight: parseInt(document.getElementById('cfgMaxHeight').value, 10),
      maxFps: parseInt(document.getElementById('cfgMaxFps').value, 10),
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
    const subtitlesUrl = document.getElementById('remoteSubtitlesUrl')?.value.trim();
    if (!url) { alert('Please enter a video URL'); return; }

    try {
      const res = await fetch('/api/room/play', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ url, title, subtitlesUrl })
      });
      const data = await res.json();
      if (res.ok) {
        toast.className = 'toast success';
        toast.textContent = '▶️ Now playing "' + (data.title || 'video') + '" in room ' + (data.room || '') + '!';
        document.getElementById('remoteVideoUrl').value = '';
        if (document.getElementById('remoteSubtitlesUrl')) document.getElementById('remoteSubtitlesUrl').value = '';
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
          tunnelBadge.textContent = '🛡️ Named Cloudflare Tunnel (Active)';
        } else {
          tunnelBadge.textContent = '☁️ TryCloudflare Quick Tunnel';
        }
        tunnelBadge.className = 'badge active';
        qrImg.src = 'https://api.qrserver.com/v1/create-qr-code/?size=180x180&data=' + encodeURIComponent(data.url);
        qrWrapper.style.display = 'flex';
      } else if (data.starting) {
        copyBtn.disabled = true;
        qrWrapper.style.display = 'none';
        tunnelBadge.textContent = '⏳ Connecting to Cloudflare…';
        tunnelBadge.className = 'badge';
        statusEl.textContent = 'Connecting cloudflared to Cloudflare edge… Check Cloudflare Zero Trust dashboard.';
      } else if (data.error) {
        copyBtn.disabled = true;
        qrWrapper.style.display = 'none';
        tunnelBadge.textContent = '⚠️ Tunnel Error';
        tunnelBadge.className = 'badge';
        statusEl.textContent = 'Tunnel error: ' + data.error;
      } else {
        copyBtn.disabled = true;
        qrWrapper.style.display = 'none';
        tunnelBadge.textContent = 'Tunnel Inactive';
        tunnelBadge.className = 'badge';
        statusEl.textContent = 'Enter your Cloudflare Tunnel token or Tailscale URL in Server Settings.';
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
