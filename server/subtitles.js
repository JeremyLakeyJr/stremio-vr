import http from 'node:http';
import https from 'node:https';
import { destination } from './media.js';

// In-memory cache for parsed subtitles to avoid re-fetching across room peers
const subtitleCache = new Map();
const CACHE_TTL_MS = 10 * 60 * 1000; // 10 minutes
const MAX_CACHE_ENTRIES = 50;

export function clearSubtitleCache() {
  subtitleCache.clear();
}

/**
 * Converts SubRip (.srt) subtitles to standard WebVTT (.vtt).
 */
export function srtToVtt(srtContent) {
  if (!srtContent || typeof srtContent !== 'string') return 'WEBVTT\n\n';
  if (srtContent.trimStart().startsWith('WEBVTT')) return srtContent;

  const normalized = srtContent.replace(/\r\n|\r/g, '\n');
  const vttCues = normalized
    // Convert 00:00:00,000 --> 00:00:00,000 to 00:00:00.000 --> 00:00:00.000
    .replace(/(\d{2}:\d{2}:\d{2}),(\d{3})/g, '$1.$2')
    // Handle short timecodes 00:00,000
    .replace(/(\d{2}:\d{2}),(\d{3})/g, '00:$1.$2');

  return `WEBVTT\n\n${vttCues.trim()}\n`;
}

function parseTimestampMs(str) {
  const parts = str.trim().split(':');
  if (parts.length === 3) {
    const hours = parseInt(parts[0], 10);
    const mins = parseInt(parts[1], 10);
    const secParts = parts[2].split('.');
    const secs = parseInt(secParts[0], 10);
    const ms = parseInt(secParts[1] || '0', 10);
    return (hours * 3600 + mins * 60 + secs) * 1000 + ms;
  } else if (parts.length === 2) {
    const mins = parseInt(parts[0], 10);
    const secParts = parts[1].split('.');
    const secs = parseInt(secParts[0], 10);
    const ms = parseInt(secParts[1] || '0', 10);
    return (mins * 60 + secs) * 1000 + ms;
  }
  return 0;
}

function formatTimestampMs(totalMs) {
  const clamped = Math.max(0, Math.floor(totalMs));
  const ms = clamped % 1000;
  const totalSecs = Math.floor(clamped / 1000);
  const secs = totalSecs % 60;
  const totalMins = Math.floor(totalSecs / 60);
  const mins = totalMins % 60;
  const hours = Math.floor(totalMins / 60);

  const pad2 = n => String(n).padStart(2, '0');
  const pad3 = n => String(n).padStart(3, '0');

  return `${pad2(hours)}:${pad2(mins)}:${pad2(secs)}.${pad3(ms)}`;
}

/**
 * Adjusts WebVTT cue timestamps forward or backward by offsetSeconds.
 * Never produces negative timestamps (clamps to 00:00:00.000).
 */
export function adjustVttOffset(vttContent, offsetSeconds = 0) {
  if (!vttContent || typeof vttContent !== 'string') return 'WEBVTT\n\n';
  const offset = typeof offsetSeconds === 'number' ? offsetSeconds : parseFloat(offsetSeconds);
  if (!offset || Number.isNaN(offset)) return vttContent;

  const offsetMs = offset * 1000;
  const cueRegex = /^((?:\d{2}:)?\d{2}:\d{2}\.\d{3})\s*-->\s*((?:\d{2}:)?\d{2}:\d{2}\.\d{3})(.*)$/;

  const lines = vttContent.split(/\r?\n/);
  const result = lines.map(line => {
    const match = line.match(cueRegex);
    if (!match) return line;

    const startMs = parseTimestampMs(match[1]) + offsetMs;
    const endMs = parseTimestampMs(match[2]) + offsetMs;
    const settings = match[3] || '';

    return `${formatTimestampMs(startMs)} --> ${formatTimestampMs(endMs)}${settings}`;
  });

  return result.join('\n');
}

/**
 * Safely fetches remote subtitles and ensures output is valid WebVTT.
 * Includes in-memory caching, redirect following, and optional timestamp offset adjustment.
 */
export async function fetchSubtitles(urlStr, allowedHosts = [], options = {}, redirects = 0) {
  if (redirects > 3) throw new Error('Too many subtitle redirects');
  const offset = typeof options.offset === 'number' ? options.offset : parseFloat(options.offset || '0');
  const now = Date.now();

  for (const [key, entry] of subtitleCache.entries()) {
    if (now - entry.timestamp >= CACHE_TTL_MS) subtitleCache.delete(key);
  }

  const cached = subtitleCache.get(urlStr);
  if (cached && (now - cached.timestamp < CACHE_TTL_MS)) {
    return offset ? adjustVttOffset(cached.vtt, offset) : cached.vtt;
  }

  const { url, address } = await destination(urlStr, allowedHosts);

  return new Promise((resolve, reject) => {
    const req = (url.protocol === 'https:' ? https : http).get(url, {
      agent: false,
      timeout: 10000,
      lookup: (_host, lookupOpts, cb) => lookupOpts.all ? cb(null, [address]) : cb(null, address.address, address.family)
    }, res => {
      if ([301, 302, 307, 308].includes(res.statusCode) && res.headers.location) {
        res.destroy();
        const nextUrl = new URL(res.headers.location, url).href;
        return fetchSubtitles(nextUrl, allowedHosts, options, redirects + 1).then(resolve, reject);
      }
      if (res.statusCode !== 200) {
        res.destroy();
        return reject(new Error(`Subtitle source returned HTTP ${res.statusCode}`));
      }

      let data = '';
      res.setEncoding('utf8');
      res.on('data', chunk => {
        data += chunk;
        if (data.length > 2 * 1024 * 1024) { // 2MB max subtitle file
          res.destroy();
          reject(new Error('Subtitle file exceeds maximum size'));
        }
      });

      res.on('end', () => {
        const vtt = srtToVtt(data);
        if (subtitleCache.size >= MAX_CACHE_ENTRIES) {
          const oldest = subtitleCache.keys().next().value;
          subtitleCache.delete(oldest);
        }
        subtitleCache.set(urlStr, { vtt, timestamp: Date.now() });

        resolve(offset ? adjustVttOffset(vtt, offset) : vtt);
      });
    });

    req.on('error', reject);
    req.on('timeout', () => req.destroy(new Error('Subtitle request timed out')));
  });
}

/**
 * Searches subtitles via OpenSubtitles REST API (v1).
 * Requires OPENSUBTITLES_API_KEY environment variable.
 */
export async function searchOpenSubtitles({ query, imdbId, type, season, episode, languages = 'en' } = {}) {
  const apiKey = process.env.OPENSUBTITLES_API_KEY?.trim();
  if (!apiKey) return { configured: false, results: [] };

  const url = new URL('https://api.opensubtitles.com/api/v1/subtitles');
  if (query) url.searchParams.set('query', query);
  if (imdbId) {
    const id = String(imdbId).replace(/^tt/, '');
    url.searchParams.set('imdb_id', id);
  }
  if (type) url.searchParams.set('type', type);
  if (season) url.searchParams.set('season_number', String(season));
  if (episode) url.searchParams.set('episode_number', String(episode));
  url.searchParams.set('languages', languages);

  return new Promise((resolve) => {
    const req = https.get(url, {
      headers: {
        'Api-Key': apiKey,
        'User-Agent': 'MovieNightVR v1.0.0'
      },
      timeout: 6000
    }, res => {
      if (res.statusCode !== 200) {
        res.resume();
        return resolve({ configured: true, results: [], error: `OpenSubtitles HTTP ${res.statusCode}` });
      }
      let raw = '';
      res.setEncoding('utf8');
      res.on('data', chunk => { raw += chunk; });
      res.on('end', () => {
        try {
          const data = JSON.parse(raw);
          const results = (data.data || []).map(item => {
            const file = item.attributes?.files?.[0];
            return {
              id: item.id,
              language: item.attributes?.language,
              format: item.attributes?.format,
              downloadCount: item.attributes?.download_count,
              hearingImpaired: item.attributes?.hearing_impaired,
              fileId: file?.file_id,
              fileName: file?.file_name
            };
          });
          resolve({ configured: true, results });
        } catch {
          resolve({ configured: true, results: [], error: 'Failed to parse OpenSubtitles response' });
        }
      });
    });
    req.on('error', err => resolve({ configured: true, results: [], error: err.message }));
    req.on('timeout', () => { req.destroy(); resolve({ configured: true, results: [], error: 'OpenSubtitles timed out' }); });
  });
}
