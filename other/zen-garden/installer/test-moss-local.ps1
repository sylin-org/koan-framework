# Test Moss Locally in Docker
#
# This script:
# 1. Rebuilds both moss and garden-rake binaries
# 2. Starts moss in a Debian container with Docker socket
# 3. Tests offering mongodb service from manifests
# 4. Verifies the service list and container status

param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$REPO_ROOT = "F:\Replica\NAS\Files\repo\github\koan-framework\other\zen-garden"

Write-Host "`n╔═══════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Moss Local Docker Test                     ║" -ForegroundColor Cyan
Write-Host "╚═══════════════════════════════════════════════╝`n" -ForegroundColor Cyan

if (-not $SkipBuild) {
    Write-Host "Building binaries..." -ForegroundColor Yellow
    docker run --rm `
        -v "/f/Replica/NAS/Files/repo/github/koan-framework/other/zen-garden:/build" `
        -w /build `
        zen-garden-builder:latest `
        bash -c "cargo build --bin garden-moss --bin garden-rake 2>&1 | tail -3"
    
    Copy-Item "$REPO_ROOT\target\debug\garden-moss" "$REPO_ROOT\bin\garden-moss" -Force
    Copy-Item "$REPO_ROOT\target\debug\garden-rake" "$REPO_ROOT\bin\garden-rake" -Force
    Write-Host "✓ Binaries updated`n" -ForegroundColor Green
}

# Clean up any existing test container
docker rm -f moss-test 2>$null | Out-Null

Write-Host "Starting moss container..." -ForegroundColor Yellow
docker run --rm -d --name moss-test `
    -v "/f/Replica/NAS/Files/repo/github/koan-framework/other/zen-garden/bin:/usr/local/bin:ro" `
    -v "/var/run/docker.sock:/var/run/docker.sock" `
    -e STONE_NAME=test-stone `
    -e RUST_LOG=info `
    -e MOSS_API_PORT=3001 `
    -p 3001:3001 `
    debian:bookworm-slim `
    bash -c "apt-get update -qq && apt-get install -y -qq libssl3 ca-certificates > /dev/null 2>&1 && /usr/local/bin/garden-moss" | Out-Null

Start-Sleep -Seconds 4

Write-Host "`n1. Health Check:" -ForegroundColor Cyan
curl -s http://localhost:3001/health

Write-Host "`n`n2. Stone Info:" -ForegroundColor Cyan
curl -s http://localhost:3001/info | ConvertFrom-Json | Select-Object name, moss_version, health | Format-List

Write-Host "3. Offer MongoDB:" -ForegroundColor Cyan
$offerResult = curl -s -X POST http://localhost:3001/api/operations/offer/mongodb 2>$null
$offerResult | ConvertFrom-Json | ConvertTo-Json -Depth 2

Write-Host "`n4. Wait for container start..." -ForegroundColor DarkGray
Start-Sleep -Seconds 10

Write-Host "`n5. Service List:" -ForegroundColor Cyan
curl -s http://localhost:3001/services | ConvertFrom-Json | Format-Table name, status, offering, port -AutoSize

Write-Host "`n6. Moss Logs:" -ForegroundColor Cyan
docker logs moss-test 2>&1 | Select-Object -Last 15

Write-Host "`n`nCleanup: docker rm -f moss-test" -ForegroundColor DarkGray
