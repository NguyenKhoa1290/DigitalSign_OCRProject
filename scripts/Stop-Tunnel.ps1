$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot

if (-not (Test-Path -LiteralPath ".env.tunnel")) {
    throw "Missing .env.tunnel. Compose needs this file to load the tunnel service configuration."
}

& docker compose -f docker-compose.yml -f docker-compose.tunnel.yml stop cloudflared
exit $LASTEXITCODE
