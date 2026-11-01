#!/bin/sh
set -eu
cd "$(dirname "$0")"

if [ ! -f .env ] && [ -f .env.example ]; then
  echo "[Movie Night] Creating default .env configuration file..."
  cp .env.example .env
fi

COMPOSE_FILES="-f compose.yaml"
if command -v nvidia-smi >/dev/null 2>&1 || [ -d /dev/dri ]; then
  echo "[Movie Night] GPU detected — enabling hardware transcoding override."
  COMPOSE_FILES="-f compose.yaml -f compose.gpu.yaml"
fi

docker compose $COMPOSE_FILES up -d --build
printf '\nMovie Night is starting. Open http://localhost:3000 and click Copy invite.\nKeep this computer running while you watch.\n'
