#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Push garden binaries to a specific stone via SSH (bypass discovery)

.DESCRIPTION
    Direct deployment via SSH when UDP discovery isn't working.
    Transfers binaries and restarts the service.

.PARAMETER StoneIP
    IP address of the target stone

.PARAMETER SSHUser
    SSH username (default: stone)

.PARAMETER SSHPassword
    SSH password (default: stone)

.PARAMETER SkipBuild
    Skip building binaries

.EXAMPLE
    .\push-ssh-direct.ps1 -StoneIP 192.168.1.111

.EXAMPLE
    .\push-ssh-direct.ps1 -StoneIP 192.168.1.111 -SkipBuild
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$StoneIP,
    [string]$SSHUser = 'stone',
    [string]$SSHPassword = 'stone',
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

# Build if needed
if (-not $SkipBuild) {
    Write-Host "🔨 Building release binaries..." -ForegroundColor Cyan
    $buildScript = Join-Path $PSScriptRoot "dist.ps1"
    & $buildScript
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Build failed" -ForegroundColor Red
        exit 1
    }
}

# Define binary paths
$distRoot = Resolve-Path "$PSScriptRoot/../dist"
$linuxMoss = Join-Path $distRoot "linux/garden-moss"
$linuxRake = Join-Path $distRoot "linux/garden-rake"

# Validate binaries
if (-not (Test-Path $linuxMoss)) { throw "Linux moss binary not found: $linuxMoss" }
if (-not (Test-Path $linuxRake)) { throw "Linux rake binary not found: $linuxRake" }

Write-Host "`n📡 Deploying to $StoneIP via SSH..." -ForegroundColor Cyan

# Auto-accept SSH host key if not cached
Write-Host "   🔑 Ensuring SSH host key is cached..." -ForegroundColor Gray
$keyCheck = echo y | plink -ssh "${SSHUser}@${StoneIP}" -pw $SSHPassword "echo OK" 2>&1 | Out-Null

# Test SSH connectivity
Write-Host "   🔍 Testing SSH connectivity..." -ForegroundColor Gray
$testResult = & plink -batch -ssh "${SSHUser}@${StoneIP}" -pw $SSHPassword "echo OK" 2>&1
if ($testResult -notmatch "OK") {
    Write-Host "   ✗ SSH connection failed" -ForegroundColor Red
    exit 1
}
Write-Host "   ✅ SSH connection successful" -ForegroundColor Green

# Ensure staging directory exists
Write-Host "   📁 Preparing staging directory..." -ForegroundColor Gray
& plink -batch -ssh "${SSHUser}@${StoneIP}" -pw $SSHPassword "mkdir -p /home/stone/bin" 2>&1 | Out-Null

# Transfer moss
Write-Host "   [1/2] Transferring garden-moss ($('{0:N2}' -f ((Get-Item $linuxMoss).Length / 1MB)) MB)..." -ForegroundColor Gray
& pscp -batch -pw $SSHPassword "$linuxMoss" "${SSHUser}@${StoneIP}:/home/stone/bin/garden-moss.staged" 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "   ✗ Failed to transfer moss" -ForegroundColor Red
    exit 1
}
Write-Host "   ✅ Moss transferred" -ForegroundColor Green

# Transfer rake
Write-Host "   [2/2] Transferring garden-rake ($('{0:N2}' -f ((Get-Item $linuxRake).Length / 1MB)) MB)..." -ForegroundColor Gray
& pscp -batch -pw $SSHPassword "$linuxRake" "${SSHUser}@${StoneIP}:/home/stone/bin/garden-rake.staged" 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "   ✗ Failed to transfer rake" -ForegroundColor Red
    exit 1
}
Write-Host "   ✅ Rake transferred" -ForegroundColor Green

# Restart service
Write-Host "   🔄 Restarting garden-moss service..." -ForegroundColor Gray
& plink -batch -ssh "${SSHUser}@${StoneIP}" -pw $SSHPassword "sudo systemctl restart garden-moss" 2>&1 | Out-Null

# Wait and verify
Write-Host "   ⏳ Waiting for service to restart..." -ForegroundColor Gray
Start-Sleep -Seconds 5

$serviceStatus = & plink -batch -ssh "${SSHUser}@${StoneIP}" -pw $SSHPassword "systemctl is-active garden-moss 2>/dev/null" 2>&1

if ($serviceStatus -match "active") {
    Write-Host "`n✅ Deployment successful! Service is running." -ForegroundColor Green
    
    # Show binary versions
    Write-Host "`n📦 Installed binaries:" -ForegroundColor Cyan
    & plink -batch -ssh "${SSHUser}@${StoneIP}" -pw $SSHPassword "ls -lh /usr/local/bin/garden-* | grep -v backup" 2>&1
}
else {
    Write-Host "`n⚠️  Binaries transferred but service status unclear" -ForegroundColor Yellow
    Write-Host "   Service status: $serviceStatus" -ForegroundColor Yellow
}
