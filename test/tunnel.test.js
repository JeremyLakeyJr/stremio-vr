import test from 'node:test';
import assert from 'node:assert/strict';
import { TunnelManager } from '../server/tunnel.js';
import { hostingInfo } from '../server/hosting.js';
import { mkdtemp, rm } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';

test('tunnel: TunnelManager starts in idle state with clean status', () => {
  const mgr = new TunnelManager({ target: 'http://127.0.0.1:3000' });
  const status = mgr.getStatus();
  assert.equal(status.running, false);
  assert.equal(status.connected, false);
  assert.equal(status.hasToken, false);
  assert.equal(status.mode, 'none');
});

test('tunnel: TunnelManager sync with empty token stops any active process', async () => {
  const mgr = new TunnelManager({ target: 'http://127.0.0.1:3000' });
  await mgr.sync({ tunnelToken: '', tunnelUrl: '' });
  const status = mgr.getStatus();
  assert.equal(status.running, false);
  assert.equal(status.hasToken, false);
});

test('tunnel: hostingInfo reflects TunnelManager starting and connected states', async (t) => {
  const dir = await mkdtemp(join(tmpdir(), 'mn-tunnel-test-'));
  t.after(() => rm(dir, { recursive: true, force: true }));
  const linkFile = join(dir, 'link.json');

  const mgr = new TunnelManager({ target: 'http://127.0.0.1:3000', linkFile });

  // Simulate starting state with token
  mgr.currentToken = 'eyJhTestToken';
  mgr.status.running = true;
  mgr.status.connected = false;
  mgr.status.mode = 'named';
  mgr.status.url = 'https://vr.mightysprite.dev';

  let info = await hostingInfo(linkFile, mgr);
  assert.equal(info.online, false);
  assert.equal(info.starting, true);
  assert.equal(info.mode, 'named');
  assert.equal(info.url, 'https://vr.mightysprite.dev');

  // Simulate edge connected state
  mgr.status.connected = true;
  info = await hostingInfo(linkFile, mgr);
  assert.equal(info.online, true);
  assert.equal(info.connected, true);
  assert.equal(info.mode, 'named');
  assert.equal(info.url, 'https://vr.mightysprite.dev');

  // Simulate error state
  mgr.status.connected = false;
  mgr.status.error = 'token expired or unauthorized';
  info = await hostingInfo(linkFile, mgr);
  assert.equal(info.online, false);
  assert.equal(info.error, 'token expired or unauthorized');

  await mgr.stop();
});
