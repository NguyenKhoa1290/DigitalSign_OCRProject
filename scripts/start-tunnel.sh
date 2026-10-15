#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$project_root"

if [[ ! -f .env.tunnel ]]; then
  echo "Missing .env.tunnel. Add the Cloudflare Tunnel token before starting the tunnel." >&2
  exit 1
fi

compose=(docker compose -f docker-compose.yml -f docker-compose.tunnel.yml)
"${compose[@]}" up -d frontend
"${compose[@]}" up -d --no-deps --force-recreate cloudflared
