#!/bin/sh
set -eu
cd "$(dirname "$0")"
docker compose up -d --build
printf '\nMovie Night is starting. Open http://localhost:3000 and click Copy invite.\nKeep this computer running while you watch.\n'
