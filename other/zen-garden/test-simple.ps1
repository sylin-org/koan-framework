#!/usr/bin/env pwsh
# Simple v1 API test

$ErrorActionPreference = "Continue"

Write-Host "`n=== Testing Zen Garden v1 API ===" -ForegroundColor Cyan

# Test 1: v1 services without quiet
Write-Host "`n[Test 1] GET /api/v1/services (with suggestions)" -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "http://localhost:7185/api/v1/services" -Method Get -ContentType "application/json"
    Write-Host "✓ Response received" -ForegroundColor Green
    Write-Host "  Data type: $($response.data.GetType().Name)"
    Write-Host "  Services: $($response.data.Count)"
    if ($response.suggestions) {
        Write-Host "  Suggestions count: $($response.suggestions.Count)" -ForegroundColor Cyan
        $response.suggestions | ForEach-Object { Write-Host "    - $_" -ForegroundColor Gray }
    }
} catch {
    Write-Host "✗ Error: $_" -ForegroundColor Red
}

# Test 2: v1 services with X-Quiet header
Write-Host "`n[Test 2] GET /api/v1/services (X-Quiet: true)" -ForegroundColor Yellow
try {
    $headers = @{ "X-Quiet" = "true" }
    $response = Invoke-RestMethod -Uri "http://localhost:7185/api/v1/services" -Method Get -Headers $headers -ContentType "application/json"
    Write-Host "✓ Response received" -ForegroundColor Green
    if ($response.suggestions -and $response.suggestions.Count -gt 0) {
        Write-Host "  ⚠️ Suggestions NOT suppressed: $($response.suggestions.Count)" -ForegroundColor Yellow
    } else {
        Write-Host "  ✓ Suggestions suppressed" -ForegroundColor Green
    }
} catch {
    Write-Host "✗ Error: $_" -ForegroundColor Red
}

# Test 3: v1 stone endpoint
Write-Host "`n[Test 3] GET /api/v1/stone" -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "http://localhost:7185/api/v1/stone" -Method Get -ContentType "application/json"
    Write-Host "✓ Response received" -ForegroundColor Green
    Write-Host "  Stone: $($response.data.stone_name)"
    Write-Host "  CPU: $($response.data.hardware.cpu.cores) cores"
    Write-Host "  Memory: $($response.data.hardware.memory.total_mb) MB"
} catch {
    Write-Host "✗ Error: $_" -ForegroundColor Red
}

# Test 4: v1 garden endpoint
Write-Host "`n[Test 4] GET /api/v1/garden" -ForegroundColor Yellow
try {
    $response = Invoke-RestMethod -Uri "http://localhost:7185/api/v1/garden" -Method Get -ContentType "application/json"
    Write-Host "✓ Response received" -ForegroundColor Green
    Write-Host "  Stones: $($response.data.stones.Count)"
    Write-Host "  Healthy: $($response.data.healthy_stones)"
} catch {
    Write-Host "✗ Error: $_" -ForegroundColor Red
}

# Test 5: Zen syntax with Rake
Write-Host "`n[Test 5] Rake zen syntax: observe quietly" -ForegroundColor Yellow
try {
    $env:RUST_LOG = "warn"
    $output = & .\target\debug\garden-rake.exe observe quietly 2>&1 | Out-String
    if ($output -match "GARDEN OVERVIEW") {
        Write-Host "✓ Zen observe command works" -ForegroundColor Green
    } else {
        Write-Host "⚠️ Unexpected output" -ForegroundColor Yellow
    }
} catch {
    Write-Host "✗ Error: $_" -ForegroundColor Red
}

Write-Host "`n=== Tests Complete ===" -ForegroundColor Cyan
Write-Host ""
