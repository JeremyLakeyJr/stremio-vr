#!/usr/bin/env node

import http from 'node:http';
import https from 'node:https';
import { detectMediaCapabilities } from '../server/hwaccel.js';

const c = {
  reset: '\x1b[0m',
  bold: '\x1b[1m',
  dim: '\x1b[2m',
  green: '\x1b[32m',
  yellow: '\x1b[33m',
  blue: '\x1b[34m',
  magenta: '\x1b[35m',
  cyan: '\x1b[36m',
  red: '\x1b[31m',
};

async function pingUrl(urlStr, timeoutMs = 2500, headers = {}) {
  if (!urlStr) return { configured: false, online: false };
  try {
    const url = new URL(urlStr);
    const client = url.protocol === 'https:' ? https : http;
    return new Promise((resolve) => {
      const req = client.get(url, { headers, timeout: timeoutMs }, (res) => {
        resolve({ configured: true, online: res.statusCode < 500, status: res.statusCode });
      });
      req.on('error', () => resolve({ configured: true, online: false }));
      req.on('timeout', () => { req.destroy(); resolve({ configured: true, online: false }); });
    });
  } catch {
    return { configured: true, online: false };
  }
}

async function run() {
  console.log(`\n${c.bold}${c.magenta}╔═══════════════════════════════════════════════════════════╗${c.reset}`);
  console.log(`${c.bold}${c.magenta}║       Movie Night VR • System & Provider Diagnostics      ║${c.reset}`);
  console.log(`${c.bold}${c.magenta}╚═══════════════════════════════════════════════════════════╝${c.reset}\n`);

  // 1. Transcoding & GPU Capabilities
  const nodeMajor = parseInt(process.versions.node.split('.')[0], 10);
  if (nodeMajor < 22) {
    console.log(`  ${c.yellow}▲ Node.js ${process.version} detected (Node.js 22+ recommended for Movie Night VR)${c.reset}\n`);
  }
  process.stdout.write(`${c.bold}[1/4] Hardware Transcoding & Smart Remux:${c.reset}\n`);
  try {
    const caps = await detectMediaCapabilities(process.env.FFMPEG_BINARY || 'ffmpeg');
    if (caps.hwAccel) {
      console.log(`  ${c.green}✔ GPU Acceleration Active:${c.reset} ${caps.backend?.name?.toUpperCase()} (${caps.backend?.encoder})`);
      console.log(`    Preset: max ${caps.maxHeight}p @ ${caps.maxFps}fps | Bitrate: ${caps.videoBitrate}`);
    } else {
      console.log(`  ${c.yellow}ℹ Hardware acceleration not detected, using CPU fallback:${c.reset} libx264 (720p/30fps default cap)`);
    }
    const remuxDisabled = process.env.MEDIA_DISABLE_REMUX === 'true';
    if (!remuxDisabled) {
      console.log(`  ${c.green}✔ Smart Remux Active:${c.reset} H.264 streams bypass transcoding (-c:v copy) for zero CPU/GPU load.`);
    } else {
      console.log(`  ${c.yellow}ℹ Smart Remux disabled via MEDIA_DISABLE_REMUX${c.reset}`);
    }
  } catch (err) {
    console.log(`  ${c.red}✖ Capability check failed:${c.reset} ${err.message}`);
  }

  // 2. Network & Remote Exposure Configuration
  console.log(`\n${c.bold}[2/4] Network & Remote Exposure:${c.reset}`);
  const tsUrl = process.env.TAILSCALE_URL || (process.env.TS_HOSTNAME ? `https://${process.env.TS_HOSTNAME}` : null);
  const tunnelToken = process.env.TUNNEL_TOKEN;
  const tunnelUrl = process.env.TUNNEL_URL || process.env.TUNNEL_HOSTNAME;
  if (tsUrl) {
    console.log(`  ${c.green}✔ Tailscale MagicDNS / Tailnet:${c.reset} ${c.cyan}${tsUrl}${c.reset}`);
  } else if (tunnelToken) {
    console.log(`  ${c.green}✔ Named / Paid Cloudflare Zero Trust Tunnel:${c.reset} Token configured`);
    if (tunnelUrl) console.log(`    Published Domain: ${c.cyan}${tunnelUrl}${c.reset}`);
  } else {
    console.log(`  ${c.blue}ℹ TryCloudflare Quick Tunnel:${c.reset} Free temporary tunnel (no account required).`);
    console.log(`    (To use Tailscale, run with ${c.bold}-f compose.tailscale.yaml${c.reset}; for custom domain, set ${c.bold}TUNNEL_TOKEN${c.reset})`);
  }
  const allowPrivate = process.env.ALLOW_PRIVATE_NETWORKS !== 'false';
  if (allowPrivate) {
    console.log(`  ${c.green}✔ LAN Access:${c.reset} Private IP networks permitted (ALLOW_PRIVATE_NETWORKS=true)`);
  } else {
    console.log(`  ${c.yellow}▲ LAN Access Restricted:${c.reset} Private networks blocked; ensure upstream hosts are in MEDIA_ALLOWED_HOSTS`);
  }

  // 3. Media Backends & Providers
  console.log(`\n${c.bold}[3/4] Media Backends & Providers:${c.reset}`);
  const msHeaders = process.env.MEDIASTORM_KEY ? { 'X-MediaStorm-Key': process.env.MEDIASTORM_KEY } : {};
  const siloHeaders = process.env.SILO_TOKEN ? { 'Authorization': `Bearer ${process.env.SILO_TOKEN}` } : {};
  const providers = [
    { name: 'Stremio Streaming Service', url: process.env.STREAMING_SERVICE_URL || 'http://stremio:11470' },
    { name: 'Jellyfin', url: process.env.JELLYFIN_URL },
    { name: 'Remux (lostb1t/remux)', url: process.env.REMUX_URL },
    { name: 'MediaStorm (Debrid/Usenet)', url: process.env.MEDIASTORM_URL, headers: msHeaders },
    { name: 'Silo (Go Backend)', url: process.env.SILO_URL, headers: siloHeaders }
  ];

  for (const prov of providers) {
    if (!prov.url) {
      console.log(`  ${c.dim}○ ${prov.name}: Not configured${c.reset}`);
      continue;
    }
    const res = await pingUrl(prov.url, 2500, prov.headers || {});
    if (res.online) {
      let extra = '';
      if (prov.name.includes('MediaStorm') && process.env.MEDIASTORM_KEY) extra = ' (API Key Authenticated)';
      if (prov.name.includes('Silo') && process.env.SILO_TOKEN) extra = ' (Token Authenticated)';
      console.log(`  ${c.green}✔ ${prov.name}:${c.reset} Online at ${prov.url}${extra} (HTTP ${res.status})`);
    } else {
      console.log(`  ${c.yellow}▲ ${prov.name}:${c.reset} Configured at ${prov.url} but unreachable`);
    }
  }

  // 4. Subtitles & Webhooks
  console.log(`\n${c.bold}[4/4] Subtitles & Notifications:${c.reset}`);
  const osKey = process.env.OPENSUBTITLES_API_KEY;
  if (osKey) {
    console.log(`  ${c.green}✔ OpenSubtitles REST API:${c.reset} Active (search endpoint ready)`);
  } else {
    console.log(`  ${c.dim}○ OpenSubtitles API Key: Not set (direct VTT/SRT URLs supported)${c.reset}`);
  }

  const discord = process.env.DISCORD_WEBHOOK_URL;
  const slack = process.env.SLACK_WEBHOOK_URL;
  const apprise = process.env.APPRISE_URL;
  const customHook = process.env.NOTIFICATION_WEBHOOK_URL;

  if (discord) console.log(`  ${c.green}✔ Discord Webhook:${c.reset} Configured`);
  if (slack) console.log(`  ${c.green}✔ Slack Webhook:${c.reset} Configured`);
  if (apprise) console.log(`  ${c.green}✔ Apprise / Generic:${c.reset} Configured`);
  if (customHook) console.log(`  ${c.green}✔ Custom Webhook:${c.reset} Configured`);
  if (!discord && !slack && !apprise && !customHook) {
    console.log(`  ${c.dim}○ No notification webhooks configured (optional).${c.reset}`);
  }

  console.log(`\n${c.green}${c.bold}Diagnostic scan complete.${c.reset}\n`);
}

run().catch(console.error);
