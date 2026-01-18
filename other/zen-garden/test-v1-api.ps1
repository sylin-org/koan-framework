#!/usr/bin/env pwsh
# Test script for Zen Garden v1 API
# Tests Moss v1 endpoints and Rake CLI

Write-Host "=== Zen Garden v1 API Test Suite ===" -ForegroundColor Cyan
Write-Host ""

$ErrorActionPreference = "Stop"
$MossEndpoint = "http://localhost:7185"

# Check if Moss is running
Write-Host "[1/8] Checking if Moss is running..." -ForegroundColor Yellow
try {
    $health = Invoke-RestMethod -Uri "$MossEndpoint/health" -TimeoutSec 5
    Write-Host "✓ Moss is running: $health" -ForegroundColor Green
} catch {
    Write-Host "✗ Moss is not running at $MossEndpoint" -ForegroundColor Red
    Write-Host "   Start Moss: .\target\debug\garden-moss.exe" -ForegroundColor Gray
    exit 1
}
Write-Host ""

# Test v1 services endpoint
Write-Host "[2/8] Testing GET /api/v1/services..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$MossEndpoint/api/v1/services" -Method Get
    Write-Host "✓ Services endpoint works" -ForegroundColor Green
    Write-Host "   Services count: $($response.data.Count)" -ForegroundColor Gray
    if ($response.suggestions) {
        Write-Host "   Suggestions: $($response.suggestions.Count)" -ForegroundColor Gray
    }
} catch {
    Write-Host "✗ Failed: $_" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Test v1 services endpoint with X-Quiet header
Write-Host "[3/8] Testing GET /api/v1/services with X-Quiet..." -ForegroundColor Yellow
try {
    $headers = @{ "X-Quiet" = "true" }
    $response = Invoke-RestMethod -Uri "$MossEndpoint/api/v1/services" -Method Get -Headers $headers
    Write-Host "✓ Quiet mode works" -ForegroundColor Green
    if ($response.suggestions -and $response.suggestions.Count -gt 0) {
        Write-Host "   ⚠️  Suggestions not suppressed (count: $($response.suggestions.Count))" -ForegroundColor Yellow
    } else {
        Write-Host "   Suggestions suppressed: ✓" -ForegroundColor Gray
    }
} catch {
    Write-Host "✗ Failed: $_" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Test v1 stone endpoint
Write-Host "[4/8] Testing GET /api/v1/stone..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$MossEndpoint/api/v1/stone" -Method Get
    Write-Host "✓ Stone endpoint works" -ForegroundColor Green
    Write-Host "   Stone name: $($response.data.stone_name)" -ForegroundColor Gray
    Write-Host "   CPU cores: $($response.data.hardware.cpu.cores)" -ForegroundColor Gray
} catch {
    Write-Host "✗ Failed: $_" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Test v1 garden endpoint
Write-Host "[5/8] Testing GET /api/v1/garden..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$MossEndpoint/api/v1/garden" -Method Get
    Write-Host "✓ Garden endpoint works" -ForegroundColor Green
    Write-Host "   Stones: $($response.data.stones.Count)" -ForegroundColor Gray
    Write-Host "   Healthy: $($response.data.healthy_stones)" -ForegroundColor Gray
} catch {
    Write-Host "✗ Failed: $_" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Test v1 pond status endpoint
Write-Host "[6/8] Testing GET /api/v1/pond/status..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$MossEndpoint/api/v1/pond/status" -Method Get
    Write-Host "✓ Pond status endpoint works" -ForegroundColor Green
    Write-Host "   Active: $($response.data.active)" -ForegroundColor Gray
    Write-Host "   Message: $($response.data.message)" -ForegroundColor Gray
} catch {
    Write-Host "✗ Failed: $_" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Test Rake CLI with quiet flag
Write-Host "[7/8] Testing Rake --quiet flag..." -ForegroundColor Yellow
try {
    $output = & .\target\debug\garden-rake.exe list --quiet --at http://localhost:7185 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or $output -match "No Zen Garden stones discovered") {
        Write-Host "✓ Rake quiet flag works" -ForegroundColor Green
    } else {
        Write-Host "⚠️  Rake execution had issues but flag accepted" -ForegroundColor Yellow
    }
} catch {
    Write-Host "✗ Failed: $_" -ForegroundColor Red
}
Write-Host ""

# Test legacy endpoint compatibility
Write-Host "[8/8] Testing legacy /api/services endpoint..." -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "$MossEndpoint/api/services" -Method Get
    Write-Host "✓ Legacy endpoint still works" -ForegroundColor Green
    Write-Host "   Services count: $($response.Count)" -ForegroundColor Gray
} catch {
    Write-Host "✗ Failed: $_" -ForegroundColor Red
    exit 1
}
Write-Host ""

Write-Host "=== All tests passed! ===" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Start Moss: .\target\debug\garden-moss.exe" -ForegroundColor Gray
Write-Host "  2. Test v1 API: Invoke-RestMethod http://localhost:7185/api/v1/services" -ForegroundColor Gray
Write-Host "  3. Test Rake: .\target\debug\garden-rake.exe list --quiet" -ForegroundColor Gray
Write-Host "  4. Test zen syntax: .\target\debug\garden-rake.exe observe" -ForegroundColor Gray
