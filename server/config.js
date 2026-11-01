import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { dirname } from 'node:path';

const CONFIG_PATH = process.env.CONFIG_PATH || (process.env.NODE_ENV === 'test' ? './test/data_config.json' : '/share/config.json');

const defaultConfig = {
  streamingServiceUrl: process.env.STREAMING_SERVICE_URL || 'http://media:11470',
  mediaAllowedHosts: process.env.MEDIA_ALLOWED_HOSTS || 'media:11470,stremio:11470',
  allowPrivateNetworks: process.env.ALLOW_PRIVATE_NETWORKS !== 'false',
  jellyfinUrl: process.env.JELLYFIN_URL || '',
  mediastormUrl: process.env.MEDIASTORM_URL || '',
  mediastormKey: process.env.MEDIASTORM_KEY || '',
  siloUrl: process.env.SILO_URL || '',
  siloToken: process.env.SILO_TOKEN || '',
  remuxUrl: process.env.REMUX_URL || '',
  openSubtitlesApiKey: process.env.OPENSUBTITLES_API_KEY || '',
  maxHeight: parseInt(process.env.MEDIA_MAX_HEIGHT || '1080', 10),
  maxFps: parseInt(process.env.MEDIA_MAX_FPS || '60', 10),
  hwAccelEnabled: process.env.MEDIA_DISABLE_REMUX !== 'true',
  addons: [
    'https://v3-cinemeta.strem.io/manifest.json'
  ]
};

let currentConfig = { ...defaultConfig };
let loaded = false;

export async function loadConfig() {
  if (loaded) return currentConfig;
  try {
    const raw = await readFile(CONFIG_PATH, 'utf8');
    const parsed = JSON.parse(raw);
    currentConfig = { ...defaultConfig, ...parsed };
  } catch {
    currentConfig = { ...defaultConfig };
  }
  loaded = true;
  return currentConfig;
}

export async function saveConfig(updates = {}) {
  await loadConfig();
  for (const [key, val] of Object.entries(updates)) {
    // If incoming value is masked (e.g. from UI placeholder/masked response), keep the existing stored value
    if (typeof val === 'string' && val.includes('••••')) {
      continue;
    }
    currentConfig[key] = val;
  }
  try {
    await mkdir(dirname(CONFIG_PATH), { recursive: true });
    await writeFile(CONFIG_PATH, JSON.stringify(currentConfig, null, 2), 'utf8');
  } catch (err) {
    console.error('[Config] Could not write config to', CONFIG_PATH, err.message);
  }
  return currentConfig;
}

export function getConfig() {
  return currentConfig;
}

export function getPublicConfig() {
  const cfg = { ...currentConfig };
  if (cfg.mediastormKey) cfg.mediastormKey = maskToken(cfg.mediastormKey);
  if (cfg.siloToken) cfg.siloToken = maskToken(cfg.siloToken);
  if (cfg.openSubtitlesApiKey) cfg.openSubtitlesApiKey = maskToken(cfg.openSubtitlesApiKey);
  return cfg;
}

function maskToken(token) {
  if (!token || token.length < 8) return '****';
  return token.slice(0, 3) + '••••' + token.slice(-3);
}
