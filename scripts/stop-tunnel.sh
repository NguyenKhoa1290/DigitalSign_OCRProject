#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$project_root"

if [[ ! -f .env.tunnel ]]; then
  echo "Missing .env.tunnel. Compose needs this file to load the tunnel service configuration." >&2
  exit 1
fi

docker compose -f docker-compose.yml -f docker-compose.tunnel.yml stop cloudflared
