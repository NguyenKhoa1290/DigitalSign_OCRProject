$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot

if (-not (Test-Path -LiteralPath ".env.tunnel")) {
    throw "Missing .env.tunnel. Add the Cloudflare Tunnel token before starting the tunnel."
}

$composeFiles = @("-f", "docker-compose.yml", "-f", "docker-compose.tunnel.yml")
& docker compose @composeFiles up -d frontend
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& docker compose @composeFiles up -d --no-deps --force-recreate cloudflared
exit $LASTEXITCODE
