<#
.SYNOPSIS
    Build Zen Garden Windows binaries natively

.DESCRIPTION
    Builds garden-moss.exe and garden-rake.exe for Windows using the MSVC toolchain.
    Requires Rust with x86_64-pc-windows-msvc target installed.

.PARAMETER Release
    Build optimized release binaries (default: debug)

.PARAMETER SkipTests
    Skip running tests before build

.EXAMPLE
    .\build-windows.ps1 -Release
    # Build release binaries for Windows

.EXAMPLE
    .\build-windows.ps1 -Release -SkipTests
    # Fast build without tests
#>

[CmdletBinding()]
param(
    [switch]$Release,
    [switch]$SkipTests
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$WORKSPACE_ROOT = (Get-Item $PSScriptRoot).Parent.FullName
$DIST_DIR = Join-Path $WORKSPACE_ROOT "dist"
$WINDOWS_DIR = Join-Path $DIST_DIR "windows"

Write-Host "`n╔════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Zen Garden Windows Build                         ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════╝`n" -ForegroundColor Cyan

if (-not $IsWindows) {
    Write-Host "✗ This script must run on Windows." -ForegroundColor Red
    Write-Host "  For Linux builds, use build-linux.ps1" -ForegroundColor Yellow
    exit 1
}

# Determine build type
$buildProfile = if ($Release) { "release" } else { "debug" }
$buildFlag = if ($Release) { "--release" } else { "" }

Write-Host "Configuration:" -ForegroundColor Yellow
Write-Host "  Platform: Windows"
Write-Host "  Build Type: $(if ($Release) { 'Release (optimized)' } else { 'Debug (fast)' })"
Write-Host "  Output Dir: $WINDOWS_DIR"
Write-Host ""

# Generate build number if not already set by parent script
if (-not $env:CARGO_BUILD_NUMBER) {
    $env:CARGO_BUILD_NUMBER = (Get-Date).ToString("yyyyMMdd.HHmm")
    Write-Host "Build Number: $env:CARGO_BUILD_NUMBER" -ForegroundColor Cyan
    Write-Host ""
}

# Create dist directories
New-Item -ItemType Directory -Force -Path $WINDOWS_DIR | Out-Null

# Run tests
if (-not $SkipTests) {
    Write-Host "Running tests..." -ForegroundColor Yellow
    Push-Location $WORKSPACE_ROOT
    try {
        cargo test --workspace --target x86_64-pc-windows-msvc
        if ($LASTEXITCODE -ne 0) {
            throw "Tests failed with exit code $LASTEXITCODE"
        }
        Write-Host "✓ All tests passed`n" -ForegroundColor Green
    } finally {
        Pop-Location
    }
} else {
    Write-Host "⚠ Skipping tests`n" -ForegroundColor DarkYellow
}

# Check if MSVC target installed
Write-Host "Checking Rust toolchain..." -ForegroundColor Yellow
$targets = rustup target list --installed 2>$null
if ($targets -notcontains "x86_64-pc-windows-msvc") {
    Write-Host "  Installing x86_64-pc-windows-msvc target..."
    rustup target add x86_64-pc-windows-msvc
    if ($LASTEXITCODE -ne 0) { throw "Failed to install Windows target" }
}
Write-Host "  ✓ x86_64-pc-windows-msvc target ready`n" -ForegroundColor Green

# Build Windows binaries
Write-Host "Building Windows binaries..." -ForegroundColor Cyan

Push-Location $WORKSPACE_ROOT
try {
    Write-Host "  → Building garden-moss.exe (Windows daemon)..."
    $buildArgs = @("build")
    if ($Release) { $buildArgs += "--release" }
    $buildArgs += @("--bin", "garden-moss", "--target", "x86_64-pc-windows-msvc")
    
    cargo @buildArgs
    
    if ($LASTEXITCODE -ne 0) { 
        Write-Host "  ⚠ garden-moss.exe build failed" -ForegroundColor Yellow
        Write-Host "    Cross-platform support may not be fully implemented yet." -ForegroundColor DarkYellow
    }
    
    Write-Host "  → Building garden-rake.exe (Windows CLI)..."
    $buildArgs = @("build")
    if ($Release) { $buildArgs += "--release" }
    $buildArgs += @("--bin", "garden-rake", "--target", "x86_64-pc-windows-msvc")
    
    cargo @buildArgs
    
    if ($LASTEXITCODE -ne 0) { throw "garden-rake.exe build failed" }
    
    # Copy binaries from target to dist/windows/
    $srcDir = Join-Path $WORKSPACE_ROOT "target\x86_64-pc-windows-msvc\$buildProfile"
    
    $mossBuilt = $false
    if (Test-Path "$srcDir\garden-moss.exe") {
        Copy-Item "$srcDir\garden-moss.exe" "$WINDOWS_DIR\garden-moss.exe" -Force
        $mossBuilt = $true
        Write-Host "  ✓ garden-moss.exe built" -ForegroundColor Green
    } else {
        Write-Host "  ⚠ garden-moss.exe not found (build may have failed)" -ForegroundColor Yellow
    }
    
    Copy-Item "$srcDir\garden-rake.exe" "$WINDOWS_DIR\garden-rake.exe" -Force
    Write-Host "  ✓ garden-rake.exe built" -ForegroundColor Green
    
    Write-Host "`n✓ Windows binaries built`n" -ForegroundColor Green
    
} finally {
    Pop-Location
}

# Display results
Write-Host "╔════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║   Build Complete!                                  ║" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════════════════╝`n" -ForegroundColor Green

Write-Host "Artifacts in $WINDOWS_DIR`:" -ForegroundColor Cyan

$artifacts = Get-ChildItem $WINDOWS_DIR -Filter "*.exe" -ErrorAction SilentlyContinue
if ($artifacts) {
    $artifacts | ForEach-Object {
        $sizeMB = [math]::Round($_.Length / 1MB, 2)
        $sizeStr = if ($sizeMB -lt 1) {
            "$([math]::Round($_.Length / 1KB, 0)) KB"
        } else {
            "$sizeMB MB"
        }
        
        Write-Host ("  ✓ {0,-20} {1,10}" -f $_.Name, $sizeStr) -ForegroundColor Green
    }
} else {
    Write-Host "  (no Windows artifacts found)" -ForegroundColor DarkGray
}

Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "  1. Test garden-rake.exe: .\dist\windows\garden-rake.exe list"
if (Test-Path "$WINDOWS_DIR\garden-moss.exe") {
    Write-Host "  2. Test garden-moss.exe (requires admin): .\dist\windows\garden-moss.exe --help"
}
Write-Host ""
