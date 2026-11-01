import { spawn } from 'node:child_process';
import { writeFile, rename, rm } from 'node:fs/promises';

const file = process.env.TUNNEL_LINK_FILE || '/share/link.json';
const token = process.env.TUNNEL_TOKEN?.trim();
const namedUrl = (process.env.TUNNEL_URL || process.env.TUNNEL_HOSTNAME)?.trim();
const target = process.env.TUNNEL_TARGET || 'http://cinema:3000';

let url = '', stopping = false, writing = false;
await rm(file, { force: true });

async function publish() {
  if (!url || writing || stopping) return;
  writing = true;
  try {
    await writeFile(file + '.tmp', JSON.stringify({ url, updatedAt: Date.now(), mode: token ? 'named' : 'quick' }), { mode: 0o600 });
    await rename(file + '.tmp', file);
  } finally {
    writing = false;
  }
}

const args = token
  ? ['tunnel', '--no-autoupdate', 'run', '--token', token]
  : ['tunnel', '--no-autoupdate', '--url', target];

const child = spawn('/usr/local/bin/cloudflared', args, { stdio: ['ignore', 'pipe', 'pipe'] });

if (token) {
  url = namedUrl || 'https://configured-in-cloudflare-zero-trust';
  console.log(`\nMovie Night is online via Named/Paid Cloudflare Tunnel: ${url}\nOpen http://localhost:3000 for the copy button.`);
  publish().catch(() => {});
}

function output(stream) {
  let buffer = '';
  stream.on('data', chunk => {
    buffer += chunk;
    const lines = buffer.split('\n');
    buffer = lines.pop();
    for (const line of lines) {
      if (!token) {
        const found = line.match(/https:\/\/[a-z0-9-]+\.trycloudflare\.com/);
        if (found && found[0] !== 'https://api.trycloudflare.com' && url !== found[0]) {
          url = found[0];
          console.log('\nMovie Night is online (Free TryCloudflare Tunnel). SHARE WITH FRIENDS: ' + url + '\nOpen http://localhost:3000 for the copy button.');
          publish().catch(() => {});
        }
      }
      if (/ERR|error|failed/i.test(line)) {
        console.error(line);
      }
    }
  });
}

output(child.stdout);
output(child.stderr);

const timer = setInterval(() => publish().catch(() => {}), 5000);

async function stop() {
  if (stopping) return;
  stopping = true;
  clearInterval(timer);
  child.kill('SIGTERM');
  while (writing) await new Promise(resolve => setTimeout(resolve, 10));
  await rm(file, { force: true });
}

child.on('error', async () => { await stop(); process.exit(1); });
child.on('close', async code => { await stop(); process.exit(code || 0); });
for (const signal of ['SIGTERM', 'SIGINT']) process.on(signal, stop);
