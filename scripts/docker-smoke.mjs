#!/usr/bin/env node

import { existsSync, readFileSync } from 'node:fs';
import { execSync } from 'node:child_process';
import net from 'node:net';

const c = {
  reset: '\x1b[0m',
  bold: '\x1b[1m',
  green: '\x1b[32m',
  yellow: '\x1b[33m',
  cyan: '\x1b[36m',
  red: '\x1b[31m',
};

console.log(`\n${c.bold}${c.cyan}╔═══════════════════════════════════════════════════════════╗${c.reset}`);
console.log(`${c.bold}${c.cyan}║      Movie Night VR • Docker Deployment Pre-flight Smoke   ║${c.reset}`);
console.log(`${c.bold}${c.cyan}╚═══════════════════════════════════════════════════════════╝${c.reset}\n`);

let passed = true;

// 1. Compose and Dockerfile presence
process.stdout.write(`${c.bold}[1/5] Checking Docker Compose files:${c.reset}\n`);
const requiredFiles = ['compose.yaml', 'compose.gpu.yaml', 'compose.tailscale.yaml', 'Dockerfile', 'Dockerfile.tunnel', 'Dockerfile.browser'];
for (const file of requiredFiles) {
  if (existsSync(file)) {
    console.log(`  ${c.green}✔${c.reset} ${file} is present`);
  } else {
    console.log(`  ${c.red}✖ Missing required file: ${file}${c.reset}`);
    passed = false;
  }
}

// 2. Environment variables configuration
console.log(`\n${c.bold}[2/5] Checking environment configuration:${c.reset}`);
const hasEnv = existsSync('.env');
if (hasEnv) {
  console.log(`  ${c.green}✔${c.reset} .env file is present`);
} else {
  console.log(`  ${c.yellow}ℹ .env not found; checking .env.example fallback${c.reset}`);
  if (existsSync('.env.example')) {
    console.log(`  ${c.green}✔${c.reset} .env.example is present (ready to copy: cp .env.example .env)`);
  } else {
    console.log(`  ${c.red}✖ Neither .env nor .env.example found${c.reset}`);
    passed = false;
  }
}

// 3. Port availability check (Port 3000)
console.log(`\n${c.bold}[3/5] Testing host port availability (3000):${c.reset}`);
async function checkPort(port) {
  return new Promise((resolve) => {
    const server = net.createServer();
    server.once('error', (err) => {
      resolve(err.code === 'EADDRINUSE' ? false : true);
    });
    server.once('listening', () => {
      server.close();
      resolve(true);
    });
    server.listen(port, '0.0.0.0');
  });
}

const port3000Free = await checkPort(3000);
if (port3000Free) {
  console.log(`  ${c.green}✔${c.reset} Port 3000 is free and available for Movie Night VR`);
} else {
  console.log(`  ${c.yellow}▲ Port 3000 is currently in use (will bind to running container or requires PORT override)${c.reset}`);
}

// 4. Docker CLI and Daemon accessibility
console.log(`\n${c.bold}[4/5] Checking Docker daemon status:${c.reset}`);
try {
  const version = execSync('docker --version', { stdio: 'pipe' }).toString().trim();
  console.log(`  ${c.green}✔${c.reset} Docker CLI found: ${version}`);
  try {
    execSync('docker info', { stdio: 'pipe' });
    console.log(`  ${c.green}✔${c.reset} Docker daemon is active and responding`);
  } catch {
    console.log(`  ${c.yellow}▲ Docker CLI installed, but daemon is currently not running or inaccessible${c.reset}`);
  }
} catch {
  console.log(`  ${c.yellow}ℹ Docker CLI not found in PATH (verify Docker Desktop or Docker CE is installed)${c.reset}`);
}

// 5. GPU hardware device scan
console.log(`\n${c.bold}[5/5] Checking GPU hardware device mounts:${c.reset}`);
let gpuFound = false;

// Check NVIDIA
try {
  execSync('nvidia-smi', { stdio: 'pipe' });
  console.log(`  ${c.green}✔${c.reset} NVIDIA GPU detected via nvidia-smi (use: docker compose -f compose.yaml -f compose.gpu.yaml up)`);
  gpuFound = true;
} catch {}

// Check Linux DRM /dev/dri
if (existsSync('/dev/dri/renderD128') || existsSync('/dev/dri')) {
  console.log(`  ${c.green}✔${c.reset} Linux VAAPI / QSV device node found at /dev/dri`);
  gpuFound = true;
}

if (!gpuFound) {
  console.log(`  ${c.yellow}ℹ Dedicated GPU not directly detected in host path; software CPU fallback active (compose.yaml)${c.reset}`);
}

console.log(`\n${passed ? c.green + c.bold + '✔ Docker pre-flight checks passed.' : c.red + '✖ Some pre-flight checks failed.'}${c.reset}\n`);
