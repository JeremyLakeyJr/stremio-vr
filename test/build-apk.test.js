import test from 'node:test';
import assert from 'node:assert/strict';
import { editorCandidates } from '../scripts/build-apk.mjs';

test('APK build discovers the pinned editor on each supported host', () => {
  const version = '6000.0.66f2';
  assert.equal(editorCandidates(version, 'linux', '/home/test', {})[0], '/home/test/Unity/Hub/Editor/6000.0.66f2/Editor/Unity');
  assert.equal(editorCandidates(version, 'darwin', '/Users/test', {})[0], '/Applications/Unity/Hub/Editor/6000.0.66f2/Unity.app/Contents/MacOS/Unity');
  assert.equal(editorCandidates(version, 'win32', '', {})[0], 'C:\\Program Files\\Unity\\Hub\\Editor\\6000.0.66f2\\Editor\\Unity.exe');
  assert.deepEqual(editorCandidates(version, 'linux', '/home/test', {UNITY_PATH: '/custom/Unity'}), ['/custom/Unity']);
});
