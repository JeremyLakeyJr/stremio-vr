import http from 'node:http';
import https from 'node:https';
import { lookup } from 'node:dns/promises';
import { randomBytes } from 'node:crypto';
import { spawn } from 'node:child_process';
import { mkdtemp, readFile, readdir, stat, rm } from 'node:fs/promises';
import { createReadStream } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { once } from 'node:events';
import { pipeline } from 'node:stream/promises';
import ipaddr from 'ipaddr.js';
import { mediaURL } from './rooms.js';

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
export async function destination(value, allowed = [], resolve = lookup) {
  const url = new URL(mediaURL(value));
  const host = url.hostname.replace(/^\[|\]$/g, '');
  const addresses = ipaddr.isValid(host) ? [{ address: host, family: ipaddr.parse(host).kind() === 'ipv6' ? 6 : 4 }] : await resolve(host, { all: true });
  if (!addresses.length || (!allowed.includes(url.host) && addresses.some(({ address }) => ipaddr.process(address).range() !== 'unicast'))) throw new Error('Private media source blocked; configure MEDIA_ALLOWED_HOSTS for your trusted streaming service');
  return { url, address: addresses[0] };
}
async function upstream(value, allowed, range, signal, redirects = 0) {
  if (redirects > 5) throw new Error('Too many media redirects');
  const { url, address } = await destination(value, allowed);
  const response = await new Promise((resolve, reject) => {
    const req = (url.protocol === 'https:' ? https : http).get(url, {
      agent: false, signal, headers: { 'Accept-Encoding': 'identity', ...(range ? { Range: range } : {}) },
      lookup: (_host, options, cb) => options.all ? cb(null, [address]) : cb(null, address.address, address.family),
    }, resolve);
    req.setTimeout(30000, () => req.destroy(new Error('Media source timed out'))); req.on('error', reject);
  });
  if ([301, 302, 303, 307, 308].includes(response.statusCode)) {
    const location = response.headers.location; response.destroy();
    if (!location) throw new Error('Invalid media redirect');
    return upstream(new URL(location, url).href, allowed, range, signal, redirects + 1);
  }
  if (![200, 206].includes(response.statusCode)) { response.destroy(); throw new Error('Media source unavailable'); }
  return { response, url };
}
export function rewritePlaylist(text, base, register) {
  if (!text.trimStart().startsWith('#EXTM3U')) throw new Error('Invalid HLS playlist');
  if (/#EXT-X-(?:SESSION-)?KEY:.*METHOD=(?!NONE)/.test(text)) throw new Error('Encrypted HLS requires a supported licensed player');
  return text.split(/\r?\n/).map(line => {
    if (!line.trim()) return line;
    if (!line.startsWith('#')) return register(new URL(line.trim(), base).href);
    return line.replace(/URI="([^"]*)"/g, (_, uri) => `URI="${register(new URL(uri, base).href)}"`);
  }).join('\n');
}

// FFmpeg never receives an upstream URL. A loopback-only gateway pins DNS and
// validates each redirect and each nested HLS URI before opening a connection.
export class MediaServer {
  constructor(options = {}) {
    this.allowed = options.allowedHosts ?? (process.env.MEDIA_ALLOWED_HOSTS || '').split(',').map(s => s.trim()).filter(Boolean);
    this.maxJobs = options.maxJobs ?? Number(process.env.MEDIA_MAX_JOBS || 2);
    this.maxBytes = options.maxBytes ?? Number(process.env.MEDIA_MAX_BYTES || 8 * 1024 ** 3);
    this.binary = options.binary || process.env.FFMPEG_BINARY || 'ffmpeg';
    this.jobs = new Map(); this.closed = false;
    this.gateway = http.createServer((req, res) => this.ingest(req, res).catch(() => { if (!res.headersSent) res.writeHead(502); res.end(); }));
    this.gateway.listen(0, '127.0.0.1'); this.ready = once(this.gateway, 'listening');
  }
  register(job, value) {
    mediaURL(value);
    let index = job.sources.indexOf(value);
    if (index < 0) { if (job.sources.length >= 20000) throw new Error('Too many playlist entries'); index = job.sources.push(value) - 1; }
    const extension = /\.(m3u8|ts|aac|mp3|mp4|m4s|mkv|webm)$/i.exec(new URL(value).pathname)?.[1] || 'ts';
    return `http://127.0.0.1:${this.gateway.address().port}/${job.id}/${index}/media.${extension}`;
  }
  async ingest(req, res) {
    const match = /^\/([a-f0-9]{48})\/(\d+)\/media\.[a-z0-9]+$/i.exec(req.url);
    const job = match && this.jobs.get(match[1]); const source = job?.sources[Number(match?.[2])];
    if (!source || !['GET', 'HEAD'].includes(req.method)) { res.writeHead(404); res.end(); return; }
    const cancel = new AbortController(); job.requests.add(cancel);
    res.on('close', () => cancel.abort());
    try {
      const { response, url } = await upstream(source, this.allowed, req.headers.range, cancel.signal);
      const iterator = response[Symbol.asyncIterator](); const first = await iterator.next();
      const chunk = first.value || Buffer.alloc(0);
      const playlist = /mpegurl/i.test(response.headers['content-type'] || '') || /\.m3u8$/i.test(url.pathname) || chunk.toString('utf8', 0, 256).trimStart().startsWith('#EXTM3U');
      if (playlist) {
        let parts = [chunk], size = chunk.length;
        while (true) { const part = await iterator.next(); if (part.done) break; size += part.value.length; if (size > 2 * 1024 ** 2) throw new Error('Playlist too large'); parts.push(part.value); }
        const body = rewritePlaylist(Buffer.concat(parts).toString('utf8'), url, value => this.register(job, value));
        res.writeHead(200, { 'Content-Type': 'application/vnd.apple.mpegurl' }); res.end(req.method === 'HEAD' ? undefined : body);
      } else {
        const headers = { 'Content-Type': response.headers['content-type'] || 'application/octet-stream' };
        for (const key of ['content-length', 'content-range', 'accept-ranges']) if (response.headers[key]) headers[key] = response.headers[key];
        res.writeHead(response.statusCode, headers);
        if (req.method === 'HEAD') { response.destroy(); res.end(); return; }
        const write = async data => { job.inputBytes += data.length; if (job.inputBytes > this.maxBytes * 4) throw new Error('Source transfer limit exceeded'); if (!res.write(data)) await once(res, 'drain', { signal: cancel.signal }); };
        await write(chunk);
        while (true) { const part = await iterator.next(); if (part.done) break; await write(part.value); }
        res.end();
      }
    } finally { cancel.abort(); job.requests.delete(cancel); }
  }
  async start(source, offset = 0, created = () => {}) {
    if (this.closed || this.jobs.size >= this.maxJobs) throw new Error('Streaming server is busy; try again');
    const id = randomBytes(24).toString('hex');
    const job = { id, source: mediaURL(source), offset, sources: [], requests: new Set(), inputBytes: 0, stopped: false, failed: null, finished: false };
    this.jobs.set(id, job); created(job);
    try {
      await this.ready;
      await destination(source, this.allowed);
      if (job.stopped || this.closed) throw new Error('Stream cancelled');
      job.dir = await mkdtemp(join(tmpdir(), 'movie-night-media-'));
      if (job.stopped || this.closed) throw new Error('Stream cancelled');
      const input = this.register(job, source);
      const args = ['-nostdin', '-hide_banner', '-loglevel', 'error', '-threads', '2', '-protocol_whitelist', 'http,tcp', '-format_whitelist', 'mov,matroska,webm,hls,mpegts,aac,mp3', '-ss', String(offset), '-i', input,
        '-map', '0:v:0', '-map', '0:a:0?', '-sn', '-dn', '-vf', "scale=w='min(1280,iw)':h='min(720,ih)':force_original_aspect_ratio=decrease:force_divisible_by=2", '-r', '30', '-c:v', 'libx264', '-preset', 'veryfast', '-threads', '2', '-pix_fmt', 'yuv420p', '-crf', '21', '-maxrate', '5M', '-bufsize', '10M', '-g', '60', '-keyint_min', '60', '-sc_threshold', '0', '-c:a', 'aac', '-ac', '2', '-ar', '48000', '-b:a', '192k',
        '-f', 'hls', '-hls_time', '2', '-hls_list_size', '0', '-hls_playlist_type', 'event', '-hls_flags', 'independent_segments+temp_file', '-hls_segment_filename', join(job.dir, 'segment%06d.ts'), join(job.dir, 'index.m3u8')];
      job.child = spawn(this.binary, args, { stdio: ['ignore', 'ignore', 'pipe'] });
      job.child.stderr.resume(); // FFmpeg errors can contain signed upstream URLs. Never publish them.
      job.child.on('error', () => { job.failed = 'FFmpeg unavailable; install ffmpeg on the server'; });
      job.child.on('close', async code => {
        job.finished = true;
        if (code !== 0 && !job.stopped) job.failed ||= 'Server could not transcode this source. Try another stream.';
        if (code === 0) try {
          const playlist = await readFile(join(job.dir, 'index.m3u8'), 'utf8');
          job.duration = offset + [...playlist.matchAll(/#EXTINF:([\d.]+)/g)].reduce((total, match) => total + Number(match[1]), 0);
        } catch { /* Job may have been replaced. */ }
      });
      job.timer = setInterval(async () => {
        try { const files = await readdir(job.dir); let bytes = 0; for (const file of files) bytes += (await stat(join(job.dir, file))).size;
          if (bytes > this.maxBytes) { job.failed = 'Server media cache limit reached'; job.child.kill('SIGKILL'); }
        } catch { /* Cleanup may race the quota check. */ }
      }, 5000); job.timer.unref();
      job.deadline = setTimeout(() => { job.failed = 'Stream expired after twelve hours'; job.child.kill('SIGKILL'); }, 12 * 3600000); job.deadline.unref();
      const until = Date.now() + 45000;
      while (Date.now() < until) {
        if (job.stopped || this.closed) throw new Error('Stream cancelled');
        if (job.failed) throw new Error(job.failed);
        try { const playlist = await readFile(join(job.dir, 'index.m3u8'), 'utf8'); if (playlist.includes('.ts')) { job.url = `/media/${id}/index.m3u8`; return job; } } catch (error) { if (error.code !== 'ENOENT') throw error; }
        await sleep(100);
      }
      throw new Error('Server transcoding timed out while preparing the video');
    } catch (error) { await this.stop(job); if (job.dir) await rm(job.dir, { recursive: true, force: true }); throw error; }
  }
  async serve(req, res) {
    const match = /^\/media\/([a-f0-9]{48})\/(index\.m3u8|segment\d+\.ts)$/.exec(req.url);
    if (!req.url.startsWith('/media/')) return false;
    const job = match && this.jobs.get(match[1]);
    if (!job?.dir || job.stopped || !['GET', 'HEAD'].includes(req.method)) { res.writeHead(404); res.end(); return true; }
    try {
      const path = join(job.dir, match[2]);
      if (match[2] === 'index.m3u8') {
        const body = await readFile(path);
        res.writeHead(200, { 'Content-Type': 'application/vnd.apple.mpegurl', 'Content-Length': body.length, 'Cache-Control': 'no-store' });
        res.end(req.method === 'HEAD' ? undefined : body); return true;
      }
      const info = await stat(path);
      res.writeHead(200, { 'Content-Type': match[2].endsWith('.m3u8') ? 'application/vnd.apple.mpegurl' : 'video/mp2t', 'Content-Length': info.size, 'Cache-Control': 'no-store' });
      if (req.method === 'HEAD') res.end(); else await pipeline(createReadStream(path), res);
    } catch { if (!res.headersSent) res.writeHead(404); res.end(); }
    return true;
  }
  async stop(job) {
    if (!job || job.stopped) return;
    job.stopped = true; this.jobs.delete(job.id); clearInterval(job.timer); clearTimeout(job.deadline);
    for (const request of job.requests) request.abort();
    if (job.child && !job.finished) { const closed = once(job.child, 'close').catch(() => {}); job.child.kill('SIGKILL'); await closed; }
    if (job.dir) await rm(job.dir, { recursive: true, force: true });
  }
  async close() { this.closed = true; await Promise.all([...this.jobs.values()].map(job => this.stop(job))); await this.ready; await new Promise(resolve => this.gateway.close(resolve)); }
}
