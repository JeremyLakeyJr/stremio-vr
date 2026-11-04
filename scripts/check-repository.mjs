#!/usr/bin/env node

import { execFileSync } from 'node:child_process';

const tracked = execFileSync('git', ['ls-files', '-z'], { encoding: 'utf8' })
  .split('\0')
  .filter(Boolean);

const forbidden = [
  /^node_modules\//,
  /^unity\/(?:Library|Temp|Obj|Logs|UserSettings|Builds|\.vs|\.utmp)\//,
  /(?:^|\/)\.env(?:\..*)?$/,
  /\.(?:apk|aab|keystore)$/,
  /^assets\/worlds\/home\//,
  /^unity\/Assets\/PrivateHome(?:\.meta|\/)/,
  /^test\/data_config\.json$/,
];

const violations = tracked.filter(file =>
  file !== '.env.example' && forbidden.some(pattern => pattern.test(file))
);
if (violations.length) {
  console.error('Generated, private, or local-only files are tracked:');
  for (const file of violations) console.error(`- ${file}`);
  process.exitCode = 1;
} else {
  console.log(`Repository hygiene check passed (${tracked.length} tracked files).`);
}
