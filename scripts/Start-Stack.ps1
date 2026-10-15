param(
    [switch]$Build
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $projectRoot

if (-not (Test-Path -LiteralPath ".env.tunnel")) {
    throw "Missing .env.tunnel. Add the Cloudflare Tunnel token before starting the public stack."
}

$composeFiles = @("-f", "docker-compose.yml", "-f", "docker-compose.tunnel.yml")
$upArgs = $composeFiles + @("up", "-d")
if ($Build) {
    $upArgs += "--build"
}

& docker compose @upArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

# cloudflared shares the Frontend network namespace. Recreate it after Compose
# has finished creating/updating Frontend so the connector binds to the current container.
& docker compose @composeFiles up -d --no-deps --force-recreate cloudflared
exit $LASTEXITCODE
