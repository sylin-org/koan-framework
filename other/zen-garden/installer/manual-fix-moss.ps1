#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Manually fix moss binary on a stone when HTTP upload fails

.DESCRIPTION
    This script uses SCP to upload the fixed moss binary directly,
    bypassing the HTTP body limit issue.

.PARAMETER StoneIP
    IP address of the stone (default: 192.168.1.107)

.PARAMETER User
    SSH username (default: stone)

.EXAMPLE
    .\manual-fix-moss.ps1 -StoneIP 192.168.1.107
#>

param(
    [string]$StoneIP = "192.168.1.107",
    [string]$User = "stone"
)

$ErrorActionPreference = "Stop"

$distRoot = Resolve-Path "$PSScriptRoot/../dist"
$mossBinary = Join-Path $distRoot "linux/garden-moss"
$rakeBinary = Join-Path $distRoot "linux/garden-rake"

if (-not (Test-Path $mossBinary)) {
    Write-Host "❌ Moss binary not found: $mossBinary" -ForegroundColor Red
    exit 1
}

Write-Host "`n🔧 Manual Moss Fix via SCP" -ForegroundColor Cyan
Write-Host "================================" -ForegroundColor Cyan
Write-Host "Stone: $User@$StoneIP" -ForegroundColor White
Write-Host ""

# Upload moss
Write-Host "📤 Uploading moss binary via SCP..." -ForegroundColor Yellow
scp $mossBinary "${User}@${StoneIP}:/tmp/garden-moss-new"
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Failed to upload moss" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Moss uploaded to /tmp/garden-moss-new" -ForegroundColor Green

# Upload rake
Write-Host "📤 Uploading rake binary via SCP..." -ForegroundColor Yellow
scp $rakeBinary "${User}@${StoneIP}:/tmp/garden-rake-new"
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Failed to upload rake" -ForegroundColor Red
    exit 1
}
Write-Host "✅ Rake uploaded to /tmp/garden-rake-new" -ForegroundColor Green

# Install and restart
Write-Host "`n🔄 Installing binaries and restarting moss..." -ForegroundColor Yellow
$installScript = @'
sudo cp /tmp/garden-moss-new /usr/local/bin/garden-moss &&
sudo cp /tmp/garden-rake-new /usr/local/bin/garden-rake &&
sudo chmod +x /usr/local/bin/garden-moss /usr/local/bin/garden-rake &&
sudo systemctl restart garden-moss &&
echo "✅ Installation complete"
'@

ssh "${User}@${StoneIP}" $installScript
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Failed to install binaries" -ForegroundColor Red
    exit 1
}

Write-Host "`n⏳ Waiting for moss to restart..." -ForegroundColor Yellow
Start-Sleep -Seconds 5

# Verify
Write-Host "🔍 Verifying moss is online..." -ForegroundColor Yellow
$healthUrl = "http://${StoneIP}:7185/health"
try {
    $health = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 5
    Write-Host "✅ Moss is online and healthy!" -ForegroundColor Green
    Write-Host "   Status: $($health.status)" -ForegroundColor White
}
catch {
    Write-Host "⚠️  Could not verify health (may still be starting): $_" -ForegroundColor Yellow
}

Write-Host "`n✅ Manual fix complete!" -ForegroundColor Green
Write-Host "You can now use push2all.ps1 for future updates." -ForegroundColor White
Write-Host ""
