import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';
export const defaultCorePath = fileURLToPath(new URL('../core/target/release/stremio-vr-core', import.meta.url));
let active = 0;
export async function runCore(request, binary = process.env.CORE_BINARY || defaultCorePath) {
  if (active >= 4) throw Object.assign(new Error('Media core is busy; try again'), { status: 429 });
  active++;
  try {
    return await new Promise((resolve, reject) => {
      const child = spawn(binary, [], { stdio: ['pipe', 'pipe', 'pipe'] });
      let output = '', settled = false;
      const finish = (error, data) => { if (settled) return; settled = true; clearTimeout(timer); error ? reject(error) : resolve(data); };
      const timer = setTimeout(() => { child.kill(); finish(Object.assign(new Error('Media core timed out'), { status: 504 })); }, 5000);
      child.on('error', () => finish(Object.assign(new Error('Build Stremio core first: npm run build:core'), { status: 503 })));
      child.stdin.on('error', () => {}); child.stderr.resume(); child.stdout.setEncoding('utf8');
      child.stdout.on('data', chunk => { output += chunk; if (output.length > 1048576) { child.kill(); finish(new Error('Media core response too large')); } });
      child.on('close', code => {
        if (settled) return;
        try { if (code !== 0) throw new Error('Media core failed'); const result = JSON.parse(output); if (!result.ok) throw Object.assign(new Error(result.error), { status: 400 }); finish(null, result.data); } catch (error) { finish(error); }
      });
      child.stdin.end(JSON.stringify(request));
    });
  } finally { active--; }
}
