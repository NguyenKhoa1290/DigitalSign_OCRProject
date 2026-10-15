#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd -- "$(dirname -- "$0")/.." && pwd)"
cd "$project_root"

if [[ ! -f .env.tunnel ]]; then
    echo "Missing .env.tunnel. Add the Cloudflare Tunnel token before starting the public stack." >&2
    exit 1
fi

if [[ $# -gt 0 && "$1" == "--build" ]]; then
    docker compose -f docker-compose.yml -f docker-compose.tunnel.yml up -d --build
else
    docker compose -f docker-compose.yml -f docker-compose.tunnel.yml up -d
fi

# cloudflared shares the Frontend network namespace. Recreate it after Compose
# has finished creating/updating Frontend so the connector binds to the current container.
docker compose -f docker-compose.yml -f docker-compose.tunnel.yml up -d --no-deps --force-recreate cloudflared
