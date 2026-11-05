import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { writeFile, rename, rm } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';

export class TunnelManager {
  constructor(options = {}) {
    this.target = options.target || process.env.TUNNEL_TARGET || `http://127.0.0.1:${process.env.PORT || 3000}`;
    this.linkFile = options.linkFile || process.env.TUNNEL_LINK_FILE || '/share/link.json';
    this.child = null;
    this.stopping = false;
    this.writing = false;
    this.currentToken = null;
    this.status = {
      running: false,
      connected: false,
      mode: 'none',
      url: '',
      error: null,
      lastLog: ''
    };
    this.timer = null;
  }

  getBinary() {
    if (process.env.CLOUDFLARED_BIN && existsSync(process.env.CLOUDFLARED_BIN)) {
      return process.env.CLOUDFLARED_BIN;
    }
    if (existsSync('/usr/local/bin/cloudflared')) {
      return '/usr/local/bin/cloudflared';
    }
    return 'cloudflared';
  }

  async sync(config = {}) {
    const token = (config.tunnelToken || process.env.TUNNEL_TOKEN || '').trim();
    const url = (config.tunnelUrl || process.env.TUNNEL_URL || process.env.TUNNEL_HOSTNAME || '').trim();

    // If token hasn't changed and child is still running
    if (token && token === this.currentToken && this.child && !this.child.killed) {
      if (url) this.status.url = url;
      await this.publish();
      return;
    }

    await this.stop();
    this.currentToken = token;

    if (!token) {
      this.status = {
        running: false,
        connected: false,
        mode: 'none',
        url: url,
        error: null,
        lastLog: ''
      };
      await this.publish();
      return;
    }

    this.start(token, url);
  }

  start(token, customUrl = '') {
    const bin = this.getBinary();
    const args = ['tunnel', '--no-autoupdate', 'run', '--token', token];

    this.status.running = true;
    this.status.connected = false;
    this.status.mode = 'named';
    this.status.url = customUrl || 'https://configured-in-cloudflare-zero-trust';
    this.status.error = null;
    this.stopping = false;

    console.log(`[Tunnel] Launching Cloudflare Tunnel connector with token (${token.slice(0, 4)}...)...`);

    try {
      this.child = spawn(bin, args, { stdio: ['ignore', 'pipe', 'pipe'] });
    } catch (err) {
      this.status.running = false;
      this.status.error = `Failed to spawn ${bin}: ${err.message}`;
      console.error('[Tunnel]', this.status.error);
      this.publish().catch(() => {});
      return;
    }

    this.child.on('error', (err) => {
      this.status.running = false;
      this.status.connected = false;
      this.status.error = err.code === 'ENOENT' ? 'cloudflared binary not installed' : err.message;
      console.error('[Tunnel] Connector process error:', this.status.error);
      this.publish().catch(() => {});
    });

    this.child.on('close', (code) => {
      if (!this.stopping) {
        this.status.running = false;
        this.status.connected = false;
        if (code !== 0) {
          this.status.error = `cloudflared connector exited with code ${code}`;
          console.warn(`[Tunnel] cloudflared exited with code ${code}`);
        }
      }
      this.publish().catch(() => {});
    });

    const handleStream = (stream) => {
      let buffer = '';
      stream.on('data', chunk => {
        buffer += chunk;
        const lines = buffer.split('\n');
        buffer = lines.pop();
        for (const line of lines) {
          const trimmed = line.trim();
          if (!trimmed) continue;
          this.status.lastLog = trimmed;
          // Cloudflare tunnel connection registration indicators
          if (/registered tunnel connection|connection [a-f0-9-]+ registered|connected to/i.test(trimmed)) {
            this.status.connected = true;
            this.status.error = null;
            console.log('[Tunnel] Cloudflare Tunnel connected to edge successfully.');
            this.publish().catch(() => {});
          } else if (/ERR|failed to (create|connect|run)|unauthorized/i.test(trimmed)) {
            this.status.error = trimmed;
            console.error('[Tunnel]', trimmed);
            this.publish().catch(() => {});
          }
        }
      });
    };

    if (this.child.stdout) handleStream(this.child.stdout);
    if (this.child.stderr) handleStream(this.child.stderr);

    if (!this.timer) {
      this.timer = setInterval(() => this.publish().catch(() => {}), 5000);
      this.timer.unref?.();
    }

    this.publish().catch(() => {});
  }

  async publish() {
    if (this.writing || !this.linkFile) return;
    this.writing = true;
    try {
      const data = {
        url: this.status.url,
        running: this.status.running,
        connected: this.status.connected,
        mode: this.status.mode,
        error: this.status.error,
        updatedAt: Date.now()
      };
      await writeFile(this.linkFile + '.tmp', JSON.stringify(data), { mode: 0o600 });
      await rename(this.linkFile + '.tmp', this.linkFile);
    } catch {
      // Ignore write errors in dev/read-only share directory
    } finally {
      this.writing = false;
    }
  }

  getStatus() {
    return {
      running: this.status.running,
      connected: this.status.connected,
      mode: this.status.mode,
      url: this.status.url,
      error: this.status.error,
      hasToken: Boolean(this.currentToken)
    };
  }

  async stop() {
    if (this.stopping) return;
    this.stopping = true;
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
    if (this.child && !this.child.killed) {
      try {
        this.child.kill('SIGTERM');
      } catch {}
    }
    this.child = null;
    this.status.running = false;
    this.status.connected = false;
    try {
      await rm(this.linkFile, { force: true });
    } catch {}
    this.stopping = false;
  }
}

// Standalone execution support
const isMain = process.argv[1] && (
  process.argv[1] === fileURLToPath(import.meta.url) ||
  process.argv[1].endsWith('tunnel.js') ||
  process.argv[1].endsWith('tunnel.mjs')
);

if (isMain) {
  const token = process.env.TUNNEL_TOKEN?.trim();
  const namedUrl = (process.env.TUNNEL_URL || process.env.TUNNEL_HOSTNAME)?.trim();
  const manager = new TunnelManager({
    target: process.env.TUNNEL_TARGET || 'http://cinema:3000',
    linkFile: process.env.TUNNEL_LINK_FILE || '/share/link.json'
  });

  if (token) {
    manager.start(token, namedUrl);
  } else {
    // Quick TryCloudflare tunnel fallback for standalone mode
    const bin = manager.getBinary();
    const child = spawn(bin, ['tunnel', '--no-autoupdate', '--url', manager.target], { stdio: ['ignore', 'pipe', 'pipe'] });
    let url = '';
    const output = (stream) => {
      let buf = '';
      stream.on('data', chunk => {
        buf += chunk;
        const lines = buf.split('\n');
        buf = lines.pop();
        for (const line of lines) {
          const found = line.match(/https:\/\/[a-z0-9-]+\.trycloudflare\.com/);
          if (found && found[0] !== 'https://api.trycloudflare.com' && url !== found[0]) {
            url = found[0];
            manager.status.url = url;
            manager.status.running = true;
            manager.status.connected = true;
            manager.status.mode = 'quick';
            manager.publish().catch(() => {});
            console.log(`\nMovie Night is online (TryCloudflare Tunnel): ${url}\n`);
          }
        }
      });
    };
    if (child.stdout) output(child.stdout);
    if (child.stderr) output(child.stderr);
  }

  for (const signal of ['SIGTERM', 'SIGINT']) {
    process.on(signal, async () => {
      await manager.stop();
      process.exit(0);
    });
  }
}
