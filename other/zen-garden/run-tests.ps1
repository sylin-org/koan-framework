#!/usr/bin/env pwsh
# Automated Integration Tests for Zen Garden v1
# Tests zen syntax, v1 API, and quiet mode functionality

param(
    [switch]$Verbose,
    [switch]$StopOnError
)

$ErrorActionPreference = if ($StopOnError) { "Stop" } else { "Continue" }
$script:PassedTests = 0
$script:FailedTests = 0
$script:SkippedTests = 0

function Write-TestHeader($message) {
    Write-Host "`n$message" -ForegroundColor Cyan
    Write-Host ("=" * $message.Length) -ForegroundColor Cyan
}

function Write-TestResult($name, $passed, $message = "") {
    if ($passed) {
        $script:PassedTests++
        Write-Host "  ✓ $name" -ForegroundColor Green
        if ($message -and $Verbose) {
            Write-Host "    $message" -ForegroundColor Gray
        }
    } else {
        $script:FailedTests++
        Write-Host "  ✗ $name" -ForegroundColor Red
        if ($message) {
            Write-Host "    $message" -ForegroundColor Yellow
        }
    }
}

function Write-TestSkipped($name, $reason) {
    $script:SkippedTests++
    Write-Host "  ⊘ $name" -ForegroundColor Yellow
    Write-Host "    $reason" -ForegroundColor Gray
}

function Test-MossRunning {
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:7185/health" -UseBasicParsing -TimeoutSec 2
        return $response.StatusCode -eq 200
    } catch {
        return $false
    }
}

function Start-MossForTesting {
    Write-Host "`nStarting Moss in Docker for testing..." -ForegroundColor Yellow
    
    # Use existing Dockerfile.build to build and run
    docker build -t garden-build -f Dockerfile.build . 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Failed to build" -ForegroundColor Red
        return $null
    }
    
    # Run container with Moss daemon
    $containerId = docker run -d -p 7185:7185 --name garden-moss-test garden-build bash -c "cargo build --bin garden-moss && ./target/debug/garden-moss --stone-name test-stone"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Failed to start container" -ForegroundColor Red
        return $null
    }
    
    # Wait for Moss to start (max 30 seconds for build + startup)
    $attempts = 0
    while ($attempts -lt 60) {
        Start-Sleep -Milliseconds 500
        if (Test-MossRunning) {
            Write-Host "✓ Moss started successfully (Container: $containerId)" -ForegroundColor Green
            return $containerId
        }
        $attempts++
    }
    
    Write-Host "✗ Failed to start Moss" -ForegroundColor Red
    docker logs garden-moss-test 2>&1 | Select-Object -Last 20
    return $null
}

function Stop-MossForTesting($containerId) {
    if ($containerId) {
        Write-Host "Stopping Docker container..." -ForegroundColor Yellow
        docker stop garden-moss-test -t 2 2>&1 | Out-Null
        docker rm garden-moss-test 2>&1 | Out-Null
    }
}

# ============================================================================
# Main Test Suite
# ============================================================================

Write-Host "`n╔════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║     Zen Garden v1 - Automated Integration Test Suite        ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan

# Check prerequisites
Write-TestHeader "Prerequisites"

# Check Docker
try {
    $dockerVersion = docker --version 2>&1
    $dockerRunning = $LASTEXITCODE -eq 0
    Write-TestResult "Docker available" $dockerRunning $dockerVersion
} catch {
    Write-TestResult "Docker available" $false "Docker not found"
    Write-Host "`n✗ Docker is required for testing. Please install Docker Desktop." -ForegroundColor Red
    exit 1
}

# Check Rake binary (Windows version for CLI tests)
$rakeExists = Test-Path ".\target\debug\garden-rake.exe"
Write-TestResult "Rake binary exists" $rakeExists ".\target\debug\garden-rake.exe"

if (-not $rakeExists) {
    Write-Host "`n✗ Rake binary not found. Run: cargo build" -ForegroundColor Red
    exit 1
}

# Start Moss in Docker
$mossWasRunning = Test-MossRunning
$mossContainer = $null

if (-not $mossWasRunning) {
    # Clean up any existing test container
    docker stop garden-moss-test 2>&1 | Out-Null
    docker rm garden-moss-test 2>&1 | Out-Null
    
    $mossContainer = Start-MossForTesting
    if (-not $mossContainer) {
        Write-Host "`n✗ Cannot start Moss in Docker. Tests aborted." -ForegroundColor Red
        exit 1
    }
    Start-Sleep -Seconds 3  # Give Moss time to initialize
}

try {
    # ========================================================================
    # API v1 Tests
    # ========================================================================
    Write-TestHeader "API v1 Endpoints"

    # Test: /api/v1/stone
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:7185/api/v1/stone" -UseBasicParsing
        $json = $response.Content | ConvertFrom-Json
        $hasStone = $json.stone_name -ne $null
        $hasSuggestions = $json.suggestions -ne $null -and $json.suggestions.Count -gt 0
        Write-TestResult "GET /api/v1/stone returns data" $hasStone "Stone: $($json.stone_name)"
        Write-TestResult "GET /api/v1/stone includes suggestions" $hasSuggestions "Count: $($json.suggestions.Count)"
    } catch {
        Write-TestResult "GET /api/v1/stone" $false $_.Exception.Message
    }

    # Test: /api/v1/stone with X-Quiet
    try {
        $headers = @{"X-Quiet" = "true"}
        $response = Invoke-WebRequest -Uri "http://localhost:7185/api/v1/stone" -Headers $headers -UseBasicParsing
        $json = $response.Content | ConvertFrom-Json
        $noSuggestions = $json.suggestions -eq $null -or $json.suggestions.Count -eq 0
        Write-TestResult "X-Quiet suppresses suggestions" $noSuggestions
    } catch {
        Write-TestResult "GET /api/v1/stone (X-Quiet)" $false $_.Exception.Message
    }

    # Test: /api/v1/garden
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:7185/api/v1/garden" -UseBasicParsing
        $json = $response.Content | ConvertFrom-Json
        $hasStones = $json.stones -ne $null
        Write-TestResult "GET /api/v1/garden returns data" $hasStones "Stones: $($json.stones.Count)"
    } catch {
        Write-TestResult "GET /api/v1/garden" $false $_.Exception.Message
    }

    # Test: /api/v1/pond/status
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:7185/api/v1/pond/status" -UseBasicParsing
        $json = $response.Content | ConvertFrom-Json
        $hasStatus = $json.active -ne $null
        Write-TestResult "GET /api/v1/pond/status returns status" $hasStatus "Active: $($json.active)"
    } catch {
        Write-TestResult "GET /api/v1/pond/status" $false $_.Exception.Message
    }

    # Test: Legacy endpoint still works
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:7185/api/services" -UseBasicParsing
        $passed = $response.StatusCode -eq 200
        Write-TestResult "Legacy /api/services still works" $passed
    } catch {
        Write-TestResult "Legacy /api/services" $false $_.Exception.Message
    }

    # ========================================================================
    # Zen Syntax Tests
    # ========================================================================
    Write-TestHeader "Zen Syntax - Core Verbs"

    $env:RUST_LOG = "warn"

    # Test: explore
    try {
        $output = & .\target\debug\garden-rake.exe explore 2>&1 | Out-String
        $passed = $output -match "AI|DATA|MESSAGING"
        Write-TestResult "explore (list offerings)" $passed
    } catch {
        Write-TestResult "explore" $false $_.Exception.Message
    }

    # Test: garden (observe all)
    try {
        $output = & .\target\debug\garden-rake.exe garden 2>&1 | Out-String
        $passed = $output -match "GARDEN OVERVIEW"
        Write-TestResult "garden (observe all)" $passed
    } catch {
        Write-TestResult "garden" $false $_.Exception.Message
    }

    # Test: touch (status)
    try {
        $output = & .\target\debug\garden-rake.exe touch 2>&1 | Out-String
        $passed = $output -match "Stone:|CPU:|Memory:"
        Write-TestResult "touch (deep inspection)" $passed
    } catch {
        Write-TestResult "touch" $false $_.Exception.Message
    }

    # ========================================================================
    # Zen Syntax - Positional Keywords
    # ========================================================================
    Write-TestHeader "Zen Syntax - Positional Keywords"

    # Test: at <url>
    try {
        $output = & .\target\debug\garden-rake.exe touch at http://localhost:7185 2>&1 | Out-String
        $passed = $output -match "Stone:|localhost"
        Write-TestResult "at <url> positional" $passed
    } catch {
        Write-TestResult "at <url>" $false $_.Exception.Message
    }

    # Test: quietly keyword
    try {
        $output = & .\target\debug\garden-rake.exe garden quietly 2>&1 | Out-String
        $passed = $output -match "GARDEN OVERVIEW"
        Write-TestResult "quietly keyword" $passed
    } catch {
        Write-TestResult "quietly" $false $_.Exception.Message
    }

    # ========================================================================
    # Quiet Mode Tests
    # ========================================================================
    Write-TestHeader "Quiet Mode - Multiple Sources"

    # Test: --quiet flag
    try {
        $output = & .\target\debug\garden-rake.exe garden --quiet 2>&1 | Out-String
        $passed = $output -match "GARDEN OVERVIEW"
        Write-TestResult "--quiet flag" $passed
    } catch {
        Write-TestResult "--quiet flag" $false $_.Exception.Message
    }

    # Test: -q short flag
    try {
        $output = & .\target\debug\garden-rake.exe garden -q 2>&1 | Out-String
        $passed = $output -match "GARDEN OVERVIEW"
        Write-TestResult "-q short flag" $passed
    } catch {
        Write-TestResult "-q flag" $false $_.Exception.Message
    }

    # Test: GARDEN_QUIET env var
    try {
        $env:GARDEN_QUIET = "1"
        $output = & .\target\debug\garden-rake.exe garden 2>&1 | Out-String
        $passed = $output -match "GARDEN OVERVIEW"
        Remove-Item Env:\GARDEN_QUIET
        Write-TestResult "GARDEN_QUIET env var" $passed
    } catch {
        Write-TestResult "GARDEN_QUIET" $false $_.Exception.Message
    }

    # ========================================================================
    # Parser Validation Tests
    # ========================================================================
    Write-TestHeader "Parser - Syntax Validation"

    # Test: Zen verb recognized
    try {
        $output = & .\target\debug\garden-rake.exe observe --help 2>&1 | Out-String
        $passed = $output -match "Observe garden state"
        Write-TestResult "Zen verb 'observe' recognized" $passed
    } catch {
        Write-TestResult "Zen verb recognition" $false $_.Exception.Message
    }

    # ========================================================================
    # Backwards Compatibility
    # ========================================================================
    Write-TestHeader "Backwards Compatibility"

    # Test: Normative list command
    try {
        $output = & .\target\debug\garden-rake.exe list 2>&1 | Out-String
        $exitOk = $LASTEXITCODE -eq 0 -or $output -notmatch "error:"
        Write-TestResult "Normative 'list' command works" $exitOk
    } catch {
        Write-TestResult "Normative list" $false $_.Exception.Message
    }

    # Test: Normative status command
    try {
        $output = & .\target\debug\garden-rake.exe status 2>&1 | Out-String
        $exitOk = $LASTEXITCODE -eq 0 -or $output -notmatch "error:"
        Write-TestResult "Normative 'status' command works" $exitOk
    } catch {
        Write-TestResult "Normative status" $false $_.Exception.Message
    }

} finally {
    # Cleanup
    if (-not $mossWasRunning -and $mossContainer) {
        Write-Host "`nCleaning up test environment..." -ForegroundColor Yellow
        Stop-MossForTesting $mossContainer
        Write-Host "✓ Cleanup complete" -ForegroundColor Green
    }
}

# ============================================================================
# Test Summary
# ============================================================================

Write-Host "`n╔════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║                        Test Summary                            ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan

$total = $script:PassedTests + $script:FailedTests + $script:SkippedTests
Write-Host "`nTotal Tests: $total" -ForegroundColor White
Write-Host "  Passed:  $($script:PassedTests)" -ForegroundColor Green
Write-Host "  Failed:  $($script:FailedTests)" -ForegroundColor Red
Write-Host "  Skipped: $($script:SkippedTests)" -ForegroundColor Yellow

if ($script:FailedTests -eq 0) {
    Write-Host "`n✓ All tests passed!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "`n✗ Some tests failed" -ForegroundColor Red
    exit 1
}
