#!/usr/bin/env pwsh
# Comprehensive Test Suite for Zen Garden Features
# Tests: Caching, Graceful Shutdown, Config, Health, Pooling, Errors, Templates

param(
    [switch]$Verbose,
    [switch]$KeepContainers
)

$ErrorActionPreference = "Stop"
$TestResults = @()

function Write-TestHeader {
    param([string]$Title)
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host "========================================`n" -ForegroundColor Cyan
}

function Write-TestResult {
    param(
        [string]$TestName,
        [bool]$Passed,
        [string]$Details = ""
    )
    
    $result = @{
        Test = $TestName
        Passed = $Passed
        Details = $Details
        Timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    }
    
    $script:TestResults += $result
    
    if ($Passed) {
        Write-Host "✓ PASS: $TestName" -ForegroundColor Green
        if ($Details) { Write-Host "  $Details" -ForegroundColor Gray }
    } else {
        Write-Host "✗ FAIL: $TestName" -ForegroundColor Red
        if ($Details) { Write-Host "  $Details" -ForegroundColor Yellow }
    }
}

function Invoke-HttpRequest {
    param(
        [string]$Url,
        [string]$Method = "GET",
        [hashtable]$Headers = @{},
        [object]$Body = $null
    )
    
    try {
        $params = @{
            Uri = $Url
            Method = $Method
            Headers = $Headers
            TimeoutSec = 10
        }
        
        if ($Body) {
            $params.Body = ($Body | ConvertTo-Json)
            $params.ContentType = "application/json"
        }
        
        $response = Invoke-WebRequest @params
        return @{
            Success = $true
            StatusCode = $response.StatusCode
            Content = $response.Content | ConvertFrom-Json -ErrorAction SilentlyContinue
            RawContent = $response.Content
        }
    } catch {
        $errorContent = $null
        $rawError = $null
        try {
            if ($_.ErrorDetails.Message) {
                $rawError = $_.ErrorDetails.Message
                $errorContent = $rawError | ConvertFrom-Json
            }
        } catch {
            # JSON parsing failed, keep rawError
        }
        
        return @{
            Success = $false
            StatusCode = $_.Exception.Response.StatusCode.value__
            Error = $_.Exception.Message
            Content = $errorContent
            RawContent = $rawError
        }
    }
}

# =============================================================================
# TEST 1: Stone Discovery Hot Caching
# =============================================================================
function Test-HotCaching {
    Write-TestHeader "TEST 1: Stone Discovery Hot Caching"
    
    # Test 1.1: First request should populate cache
    $start1 = Get-Date
    $result1 = & .\target\x86_64-pc-windows-msvc\debug\garden-rake.exe list --at http://127.0.0.1:3001 2>&1
    $duration1 = (Get-Date) - $start1
    
    Write-TestResult `
        -TestName "1.1: First discovery request" `
        -Passed ($LASTEXITCODE -eq 0) `
        -Details "Duration: $($duration1.TotalMilliseconds)ms"
    
    # Test 1.2: Second request should use cache (faster)
    Start-Sleep -Milliseconds 100
    $start2 = Get-Date
    $env:RUST_LOG = "info"
    $result2 = & .\target\x86_64-pc-windows-msvc\debug\garden-rake.exe list --at http://127.0.0.1:3001 2>&1 | Select-Object -First 10
    $duration2 = (Get-Date) - $start2
    
    Write-TestResult `
        -TestName "1.2: Cached discovery request" `
        -Passed ($LASTEXITCODE -eq 0) `
        -Details "Duration: $($duration2.TotalMilliseconds)ms"
    
    # Test 1.3: Verify cache hit in logs
    $cacheHitFound = ($result2 | Where-Object { $_ -match "Cache hit" }).Count -gt 0
    Write-TestResult `
        -TestName "1.3: Cache hit detected in output" `
        -Passed $cacheHitFound `
        -Details "Expected cache reuse for subsequent requests"
}

# =============================================================================
# TEST 2: Graceful Shutdown
# =============================================================================
function Test-GracefulShutdown {
    Write-TestHeader "TEST 2: Graceful Shutdown"
    
    # Test 2.1: Health check before shutdown
    $health = Invoke-HttpRequest -Url "http://127.0.0.1:3001/health"
    Write-TestResult `
        -TestName "2.1: Moss is healthy before shutdown" `
        -Passed ($health.Success -and $health.StatusCode -eq 200) `
        -Details "Status: $($health.Content.status)"
    
    # Test 2.2: Trigger graceful shutdown via HTTP
    $shutdown = Invoke-HttpRequest -Url "http://127.0.0.1:3001/admin/shutdown" -Method "POST"
    $hasResponse = $shutdown.StatusCode -eq 200 -and $shutdown.Content.success -eq $true
    Write-TestResult `
        -TestName "2.2: Graceful shutdown endpoint responds" `
        -Passed $hasResponse `
        -Details "Success: $($shutdown.Content.success), Message: $($shutdown.Content.message)"
    
    # Test 2.3: Wait for shutdown and verify moss stopped
    Start-Sleep -Seconds 3
    $healthAfter = Invoke-HttpRequest -Url "http://127.0.0.1:3001/health"
    Write-TestResult `
        -TestName "2.3: Moss stopped after graceful shutdown" `
        -Passed (-not $healthAfter.Success) `
        -Details "Connection refused (expected)"
    
    # Restart moss for remaining tests
    Write-Host "`nRestarting moss for remaining tests..." -ForegroundColor Yellow
    Start-Process -FilePath ".\target\x86_64-pc-windows-msvc\debug\garden-moss.exe" -NoNewWindow
    Start-Sleep -Seconds 5
    
    $healthRestarted = Invoke-HttpRequest -Url "http://127.0.0.1:3001/health"
    Write-TestResult `
        -TestName "2.4: Moss restarted successfully" `
        -Passed ($healthRestarted.Success) `
        -Details "Ready for remaining tests"
}

# =============================================================================
# TEST 3: Configuration File Support
# =============================================================================
function Test-ConfigurationFile {
    Write-TestHeader "TEST 3: Configuration File Support"
    
    # Test 3.1: Create test moss.toml
    $testConfig = @"
stone_name = "test-config-stone"
port = 3001
log_level = "debug"
fast_sync_timeout = 10
"@
    
    $configPath = ".\moss.toml"
    Set-Content -Path $configPath -Value $testConfig
    
    Write-TestResult `
        -TestName "3.1: Created moss.toml test config" `
        -Passed (Test-Path $configPath) `
        -Details "Config file: $configPath"
    
    # Test 3.2: Restart moss with config file
    Stop-Process -Name "garden-moss" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    
    Start-Process -FilePath ".\target\x86_64-pc-windows-msvc\debug\garden-moss.exe" -NoNewWindow
    Start-Sleep -Seconds 5
    
    # Test 3.3: Verify config was loaded (check capabilities endpoint)
    $caps = Invoke-HttpRequest -Url "http://127.0.0.1:3001/capabilities"
    $configLoaded = $caps.Content.stone_name -eq "test-config-stone"
    
    Write-TestResult `
        -TestName "3.2: Config file loaded correctly" `
        -Passed $configLoaded `
        -Details "Stone name from config: $($caps.Content.stone_name)"
    
    # Test 3.4: Test CLI override
    Stop-Process -Name "garden-moss" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    
    Start-Process -FilePath ".\target\x86_64-pc-windows-msvc\debug\garden-moss.exe" `
        -ArgumentList "--stone-name", "cli-override-stone" `
        -NoNewWindow
    Start-Sleep -Seconds 5
    
    $capsOverride = Invoke-HttpRequest -Url "http://127.0.0.1:3001/capabilities"
    $cliOverride = $capsOverride.Content.stone_name -eq "cli-override-stone"
    
    Write-TestResult `
        -TestName "3.3: CLI args override config file" `
        -Passed $cliOverride `
        -Details "CLI override stone name: $($capsOverride.Content.stone_name)"
    
    # Cleanup
    Remove-Item -Path $configPath -ErrorAction SilentlyContinue
    Stop-Process -Name "garden-moss" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Start-Process -FilePath ".\target\x86_64-pc-windows-msvc\debug\garden-moss.exe" -NoNewWindow
    Start-Sleep -Seconds 5
}

# =============================================================================
# TEST 4: Enhanced Health Checks
# =============================================================================
function Test-HealthChecks {
    Write-TestHeader "TEST 4: Enhanced Health Checks"
    
    # Test 4.1: Health endpoint returns structured data
    $health = Invoke-HttpRequest -Url "http://127.0.0.1:3001/health"
    
    $hasComponents = $health.Content.PSObject.Properties.Name -contains "components"
    Write-TestResult `
        -TestName "4.1: Health response has components field" `
        -Passed $hasComponents `
        -Details "Fields: $($health.Content.PSObject.Properties.Name -join ', ')"
    
    # Test 4.2: Docker component present
    $hasDocker = $health.Content.components.PSObject.Properties.Name -contains "docker"
    Write-TestResult `
        -TestName "4.2: Docker component health check" `
        -Passed $hasDocker `
        -Details "Docker status: $($health.Content.components.docker.status)"
    
    # Test 4.3: Disk component present
    $hasDisk = $health.Content.components.PSObject.Properties.Name -contains "disk"
    Write-TestResult `
        -TestName "4.3: Disk component health check" `
        -Passed $hasDisk `
        -Details "Disk usage: $($health.Content.components.disk.usage_percent)%"
    
    # Test 4.4: Memory component present
    $hasMemory = $health.Content.components.PSObject.Properties.Name -contains "memory"
    Write-TestResult `
        -TestName "4.4: Memory component health check" `
        -Passed $hasMemory `
        -Details "Memory usage: $($health.Content.components.memory.usage_percent)%"
    
    # Test 4.5: Overall status is healthy/degraded/unhealthy
    $validStatus = $health.Content.status -in @("healthy", "degraded", "unhealthy")
    Write-TestResult `
        -TestName "4.5: Overall status is valid" `
        -Passed $validStatus `
        -Details "Status: $($health.Content.status)"
    
    # Test 4.6: Timestamp present
    $hasTimestamp = $null -ne $health.Content.timestamp
    Write-TestResult `
        -TestName "4.6: Health response has timestamp" `
        -Passed $hasTimestamp `
        -Details "Timestamp: $($health.Content.timestamp)"
}

# =============================================================================
# TEST 5: Connection Pooling
# =============================================================================
function Test-ConnectionPooling {
    Write-TestHeader "TEST 5: Connection Pooling"
    
    # Test 5.1: Multiple sequential requests (should reuse connection)
    $times = @()
    for ($i = 1; $i -le 5; $i++) {
        $start = Get-Date
        $result = Invoke-HttpRequest -Url "http://127.0.0.1:3001/health"
        $duration = (Get-Date) - $start
        $times += $duration.TotalMilliseconds
        
        if ($Verbose) {
            $ms = $duration.TotalMilliseconds
            Write-Host "  Request ${i}: ${ms}ms" -ForegroundColor Gray
        }
    }
    
    # First request establishes connection, subsequent should be faster
    $avgSubsequent = ($times | Select-Object -Skip 1 | Measure-Object -Average).Average
    $improvement = $times[0] -gt $avgSubsequent
    
    Write-TestResult `
        -TestName "5.1: Connection reuse improves latency" `
        -Passed $improvement `
        -Details "First: $($times[0])ms, Avg subsequent: $([math]::Round($avgSubsequent, 2))ms"
    
    # Test 5.2: Concurrent requests (HTTP/2 multiplexing)
    $jobs = 1..3 | ForEach-Object {
        Start-Job -ScriptBlock {
            Invoke-WebRequest -Uri "http://127.0.0.1:3001/capabilities" -TimeoutSec 10
        }
    }
    
    $results = $jobs | Wait-Job | Receive-Job
    $allSucceeded = ($results | Where-Object { $_.StatusCode -eq 200 }).Count -eq 3
    
    Write-TestResult `
        -TestName "5.2: Concurrent requests succeed" `
        -Passed $allSucceeded `
        -Details "3 parallel requests completed"
    
    $jobs | Remove-Job
}

# =============================================================================
# TEST 6: Standardized Error Responses
# =============================================================================
function Test-ErrorResponses {
    Write-TestHeader "TEST 6: Standardized Error Responses"
    
    # Test 6.1: 404 error has standard envelope
    $notFound = Invoke-HttpRequest -Url "http://127.0.0.1:3001/api/services/nonexistent-service"
    
    $hasErrorEnvelope = $notFound.Content.PSObject.Properties.Name -contains "error"
    Write-TestResult `
        -TestName "6.1: 404 response has error envelope" `
        -Passed ($hasErrorEnvelope -and $notFound.StatusCode -eq 404) `
        -Details "Status: $($notFound.StatusCode), Has error field: $hasErrorEnvelope"
    
    # Test 6.2: Error has code field
    $hasCode = $notFound.Content.error.PSObject.Properties.Name -contains "code"
    Write-TestResult `
        -TestName "6.2: Error envelope has code field" `
        -Passed $hasCode `
        -Details "Error code: $($notFound.Content.error.code)"
    
    # Test 6.3: Error has message field
    $hasMessage = $notFound.Content.error.PSObject.Properties.Name -contains "message"
    Write-TestResult `
        -TestName "6.3: Error envelope has message field" `
        -Passed $hasMessage `
        -Details "Message: $($notFound.Content.error.message)"
    
    # Test 6.4: Invalid offering returns 400
    $invalidReq = Invoke-HttpRequest `
        -Url "http://127.0.0.1:3001/api/operations/offer/invalid-offering-xyz" `
        -Method "POST"
    
    $is400 = $invalidReq.StatusCode -eq 400 -or $invalidReq.StatusCode -eq 404
    Write-TestResult `
        -TestName "6.4: Invalid request returns 4xx status" `
        -Passed $is400 `
        -Details "Status: $($invalidReq.StatusCode)"
}

# =============================================================================
# TEST 7: Template Commands
# =============================================================================
function Test-TemplateCommands {
    Write-TestHeader "TEST 7: Template Commands"
    
    # Test 7.1: Template list command
    $listOutput = & .\target\x86_64-pc-windows-msvc\debug\garden-rake.exe template list --at http://127.0.0.1:3001 2>&1
    $listSuccess = $LASTEXITCODE -eq 0
    
    Write-TestResult `
        -TestName "7.1: Template list command succeeds" `
        -Passed $listSuccess `
        -Details "Found templates in output"
    
    # Test 7.2: Template list shows known templates
    $hasMongoDb = ($listOutput | Where-Object { $_ -match "mongodb" }).Count -gt 0
    Write-TestResult `
        -TestName "7.2: Template list includes mongodb" `
        -Passed $hasMongoDb `
        -Details "MongoDB template available"
    
    # Test 7.3: Template show command for mongodb
    $showOutput = & .\target\x86_64-pc-windows-msvc\debug\garden-rake.exe template show mongodb --at http://127.0.0.1:3001 2>&1
    $showSuccess = $LASTEXITCODE -eq 0
    
    Write-TestResult `
        -TestName "7.3: Template show mongodb succeeds" `
        -Passed $showSuccess `
        -Details "Template details displayed"
    
    # Test 7.4: Template show includes port info
    $hasPortInfo = ($showOutput | Where-Object { $_ -match "Ports?:" }).Count -gt 0
    Write-TestResult `
        -TestName "7.4: Template details include port information" `
        -Passed $hasPortInfo `
        -Details "Port section found in output"
    
    # Test 7.5: Template show for non-existent returns error
    $invalidShow = & .\target\x86_64-pc-windows-msvc\debug\garden-rake.exe template show nonexistent-template-xyz --at http://127.0.0.1:3001 2>&1
    $showFailed = $LASTEXITCODE -ne 0
    
    Write-TestResult `
        -TestName "7.5: Template show fails gracefully for invalid template" `
        -Passed $showFailed `
        -Details "Expected failure for non-existent template"
}

# =============================================================================
# TEST 8: Integration Test - Full Workflow
# =============================================================================
function Test-FullWorkflow {
    Write-TestHeader "TEST 8: Full Workflow Integration"
    
    # Test 8.1: Discover stone
    $list = & .\target\x86_64-pc-windows-msvc\debug\garden-rake.exe list --at http://127.0.0.1:3001 2>&1
    Write-TestResult `
        -TestName "8.1: Stone discovery" `
        -Passed ($LASTEXITCODE -eq 0) `
        -Details "Stone list retrieved"
    
    # Test 8.2: Check stone health
    $status = & .\target\x86_64-pc-windows-msvc\debug\garden-rake.exe status --at http://127.0.0.1:3001 2>&1
    Write-TestResult `
        -TestName "8.2: Stone status check" `
        -Passed ($LASTEXITCODE -eq 0) `
        -Details "Status retrieved"
    
    # Test 8.3: List templates
    $templates = & .\target\x86_64-pc-windows-msvc\debug\garden-rake.exe template list --at http://127.0.0.1:3001 2>&1
    Write-TestResult `
        -TestName "8.3: List available templates" `
        -Passed ($LASTEXITCODE -eq 0) `
        -Details "Templates listed"
    
    # Test 8.4: View template details
    $templateShow = & .\target\x86_64-pc-windows-msvc\debug\garden-rake.exe template show redis --at http://127.0.0.1:3001 2>&1
    Write-TestResult `
        -TestName "8.4: View template details" `
        -Passed ($LASTEXITCODE -eq 0) `
        -Details "Template details shown"
}

# =============================================================================
# MAIN TEST EXECUTION
# =============================================================================

Write-Host "`n╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "║   ZEN GARDEN - COMPREHENSIVE FEATURE TEST SUITE          ║" -ForegroundColor Magenta
Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Magenta

# Ensure we're in the right directory
Push-Location "F:\Replica\NAS\Files\repo\github\koan-framework\other\zen-garden"

# Check if moss is running
$mossRunning = Get-Process -Name "garden-moss" -ErrorAction SilentlyContinue
if (-not $mossRunning) {
    Write-Host "`nStarting moss daemon..." -ForegroundColor Yellow
    Start-Process -FilePath ".\target\x86_64-pc-windows-msvc\debug\garden-moss.exe" -NoNewWindow
    Start-Sleep -Seconds 5
}

# Run all tests
try {
    Test-HotCaching
    Test-GracefulShutdown
    Test-ConfigurationFile
    Test-HealthChecks
    Test-ConnectionPooling
    Test-ErrorResponses
    Test-TemplateCommands
    Test-FullWorkflow
    
    # Summary
    Write-Host "`n╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
    Write-Host "║   TEST SUMMARY                                           ║" -ForegroundColor Magenta
    Write-Host "╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Magenta
    
    $totalTests = $TestResults.Count
    $passedTests = ($TestResults | Where-Object { $_.Passed }).Count
    $failedTests = $totalTests - $passedTests
    $passRate = [math]::Round(($passedTests / $totalTests) * 100, 1)
    
    Write-Host "`nTotal Tests: $totalTests" -ForegroundColor White
    Write-Host "Passed:      $passedTests " -ForegroundColor Green -NoNewline
    Write-Host "($passRate%)" -ForegroundColor Green
    Write-Host "Failed:      $failedTests" -ForegroundColor $(if ($failedTests -eq 0) { "Green" } else { "Red" })
    
    if ($failedTests -gt 0) {
        Write-Host "`nFailed Tests:" -ForegroundColor Yellow
        $TestResults | Where-Object { -not $_.Passed } | ForEach-Object {
            Write-Host "  ✗ $($_.Test)" -ForegroundColor Red
            if ($_.Details) {
                Write-Host "    $($_.Details)" -ForegroundColor Gray
            }
        }
    }
    
    # Export results
    $TestResults | ConvertTo-Json | Out-File -FilePath ".\tests\test-results.json"
    Write-Host "`nTest results saved to: .\tests\test-results.json" -ForegroundColor Cyan
    
} finally {
    # Cleanup
    if (-not $KeepContainers) {
        Write-Host "`nCleaning up..." -ForegroundColor Yellow
        Stop-Process -Name "garden-moss" -Force -ErrorAction SilentlyContinue
    }
    
    Pop-Location
}

Write-Host "`n✓ Test suite complete!`n" -ForegroundColor Green

exit $(if ($failedTests -eq 0) { 0 } else { 1 })
