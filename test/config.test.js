import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

const directory = await mkdtemp(join(tmpdir(), 'movie-night-config-'));
const path = join(directory, 'config.json');
const previous = process.env.CONFIG_PATH;
process.env.CONFIG_PATH = path;
const config = await import('../server/config.js');

test('config persists updates and masks webhook credentials', async () => {
  const saved = await config.saveConfig({
    maxFps: 72,
    webhookUrl: 'https://discord.com/api/webhooks/config-test-secret'
  });
  assert.equal(saved.maxFps, 72);
  const persisted = JSON.parse(await readFile(path, 'utf8'));
  assert.equal(persisted.webhookUrl, 'https://discord.com/api/webhooks/config-test-secret');
  assert.match(config.getPublicConfig().webhookUrl, /••••/);
  assert.doesNotMatch(config.getPublicConfig().webhookUrl, /config-test-secret/);
});

test.after(async () => {
  if (previous === undefined) delete process.env.CONFIG_PATH;
  else process.env.CONFIG_PATH = previous;
  await rm(directory, { recursive: true, force: true });
});
