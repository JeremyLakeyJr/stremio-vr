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

async function pingUrl(urlStr, timeoutMs = 2500) {
  if (!urlStr) return { configured: false, online: false };
  try {
    const url = new URL(urlStr);
    const client = url.protocol === 'https:' ? https : http;
    return new Promise((resolve) => {
      const req = client.get(url, { timeout: timeoutMs }, (res) => {
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

  // 2. Cloudflare Tunnel Configuration
  console.log(`\n${c.bold}[2/4] Network & Cloudflare Tunnel:${c.reset}`);
  const tunnelToken = process.env.TUNNEL_TOKEN;
  const tunnelUrl = process.env.TUNNEL_URL;
  if (tunnelToken) {
    console.log(`  ${c.green}✔ Named / Paid Cloudflare Zero Trust Tunnel:${c.reset} Token configured`);
    if (tunnelUrl) console.log(`    Published Domain: ${c.cyan}${tunnelUrl}${c.reset}`);
  } else {
    console.log(`  ${c.blue}ℹ TryCloudflare Quick Tunnel:${c.reset} Free temporary tunnel (no account required).`);
    console.log(`    (To use a custom domain, set ${c.bold}TUNNEL_TOKEN${c.reset} in .env)`);
  }

  // 3. Media Backends & Providers
  console.log(`\n${c.bold}[3/4] Media Backends & Providers:${c.reset}`);
  const providers = [
    { name: 'Stremio Streaming Service', url: process.env.STREAMING_SERVICE_URL || 'http://stremio:11470' },
    { name: 'Jellyfin / Remux', url: process.env.JELLYFIN_URL },
    { name: 'MediaStorm (Debrid/Usenet)', url: process.env.MEDIASTORM_URL },
    { name: 'Silo (Go Backend)', url: process.env.SILO_URL }
  ];

  for (const prov of providers) {
    if (!prov.url) {
      console.log(`  ${c.dim}○ ${prov.name}: Not configured${c.reset}`);
      continue;
    }
    const res = await pingUrl(prov.url);
    if (res.online) {
      console.log(`  ${c.green}✔ ${prov.name}:${c.reset} Online at ${prov.url} (HTTP ${res.status})`);
    } else {
      console.log(`  ${c.yellow}▲ ${prov.name}:${c.reset} Configured at ${prov.url} but unreachable`);
    }
  }

  // 4. Notifications & Webhooks
  console.log(`\n${c.bold}[4/4] Webhooks & Room Notifications:${c.reset}`);
  const discord = process.env.DISCORD_WEBHOOK_URL;
  const slack = process.env.SLACK_WEBHOOK_URL;
  const apprise = process.env.APPRISE_URL;

  if (discord) console.log(`  ${c.green}✔ Discord Webhook:${c.reset} Configured`);
  if (slack) console.log(`  ${c.green}✔ Slack Webhook:${c.reset} Configured`);
  if (apprise) console.log(`  ${c.green}✔ Apprise / Generic:${c.reset} Configured`);
  if (!discord && !slack && !apprise) {
    console.log(`  ${c.dim}○ No notification webhooks configured (optional).${c.reset}`);
  }

  console.log(`\n${c.green}${c.bold}Diagnostic scan complete.${c.reset}\n`);
}

run().catch(console.error);
