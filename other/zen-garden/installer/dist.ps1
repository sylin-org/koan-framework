<#
.SYNOPSIS
    Build complete Zen Garden distribution (Linux + Windows)

.DESCRIPTION
    Orchestrates builds for all platforms:
    - Linux (garden-moss, garden-rake) via build-linux.ps1
    - Windows (garden-moss.exe, garden-rake.exe) via build-windows.ps1
    
    This is the main entry point for full distribution builds.

.PARAMETER Release
    Build optimized release binaries (default: debug)

.PARAMETER SkipTests
    Skip running tests before build

.PARAMETER SkipLinux
    Skip Linux build (build Windows only)

.PARAMETER SkipWindows
    Skip Windows build (build Linux only)

.PARAMETER ForceRebuild
    Force rebuild of Docker build container (Linux only)

.EXAMPLE
    .\dist.ps1 -Release
    # Build release binaries for all platforms

.EXAMPLE
    .\dist.ps1 -Release -SkipTests
    # Fast build without tests

.EXAMPLE
    .\dist.ps1 -SkipWindows -Release
    # Build Linux binaries only
#>

[CmdletBinding()]
param(
    [switch]$Release,
    [switch]$SkipTests,
    [switch]$SkipLinux,
    [switch]$SkipWindows,
    [switch]$ForceRebuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$WORKSPACE_ROOT = (Get-Item $PSScriptRoot).Parent.FullName
$DIST_DIR = Join-Path $WORKSPACE_ROOT "dist"
$INSTALLER_DIR = $PSScriptRoot

# Load version from version.json
$versionFile = Join-Path $WORKSPACE_ROOT "version.json"
if (-not (Test-Path $versionFile)) {
    Write-Error "version.json not found at $versionFile"
    exit 1
}

$versionData = Get-Content $versionFile | ConvertFrom-Json
$major = $versionData.major
$minor = $versionData.minor
$revision = (Get-Date).ToString("yyyyMMddHHmm")
$version = "$major.$minor.$revision"

Write-Host "`n╔════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Zen Garden Distribution Build                   ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════╝`n" -ForegroundColor Cyan

Write-Host "Version: $version" -ForegroundColor Cyan
Write-Host "  Phase: $major.$minor - $($versionData.description)" -ForegroundColor DarkGray
Write-Host "  Moment: $revision ($(Get-Date -Format 'yyyy-MM-dd HH:mm'))" -ForegroundColor DarkGray
Write-Host ""

Write-Host "Platform Selection:" -ForegroundColor Yellow
Write-Host "  Linux Build: $(if ($SkipLinux) { '❌ Skipped' } else { '✓ Enabled' })"
Write-Host "  Windows Build: $(if ($SkipWindows) { '❌ Skipped' } else { '✓ Enabled' })"
Write-Host ""

# Set version for build scripts
$env:GARDEN_VERSION = $version
$env:BUILD_NUMBER = $revision

# Update Cargo.toml files with version
Write-Host "Updating Cargo.toml files with version $major.$minor..." -ForegroundColor DarkGray
$cargoFiles = @(
    (Join-Path $WORKSPACE_ROOT "src\moss\Cargo.toml"),
    (Join-Path $WORKSPACE_ROOT "src\rake\Cargo.toml"),
    (Join-Path $WORKSPACE_ROOT "src\lantern\Cargo.toml"),
    (Join-Path $WORKSPACE_ROOT "src\common\Cargo.toml")
)

foreach ($file in $cargoFiles) {
    if (Test-Path $file) {
        $lines = Get-Content $file
        $updated = $lines | ForEach-Object {
            if ($_ -match '^version\s*=\s*"[\d\.]+"' -and $_ -notmatch 'rust-version') {
                "version = `"$major.$minor.0`""
            } else {
                $_
            }
        }
        Set-Content $file ($updated -join "`n")
    }
}
Write-Host ""

# Create dist directory
New-Item -ItemType Directory -Force -Path $DIST_DIR | Out-Null

$buildErrors = @()

# Build Linux binaries (via Docker)
if (-not $SkipLinux) {
    Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host " Phase 1: Linux Build (Docker)" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════`n" -ForegroundColor Cyan
    
    $dockerArgs = @{}
    if ($Release) { $dockerArgs.Add('Release', $true) }
    if ($ForceRebuild) { $dockerArgs.Add('ForceRebuild', $true) }
    
    $linuxScript = Join-Path $INSTALLER_DIR "build-linux.ps1"
    try {
        & $linuxScript @dockerArgs
        if ($LASTEXITCODE -eq 0 -or $null -eq $LASTEXITCODE) {
            Write-Host "✓ Linux build completed`n" -ForegroundColor Green
        } else {
            $buildErrors += "Linux build failed with exit code $LASTEXITCODE"
            Write-Host "✗ Linux build failed with exit code $LASTEXITCODE`n" -ForegroundColor Red
        }
    } catch {
        $buildErrors += "Linux build: $_"
        Write-Host "✗ Linux build failed: $_`n" -ForegroundColor Red
    }
} else {
    Write-Host "Skipping Linux build (use -SkipLinux=`$false to enable)`n" -ForegroundColor DarkGray
}

# Build Windows binaries (native)
if (-not $SkipWindows) {
    if (-not $IsWindows) {
        Write-Host "Skipping Windows build (not on Windows host)`n" -ForegroundColor DarkGray
    } else {
        Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host " Phase 2: Windows Build (Native)" -ForegroundColor Cyan
        Write-Host "═══════════════════════════════════════════════════`n" -ForegroundColor Cyan
        
        $windowsArgs = @{}
        if ($Release) { $windowsArgs['Release'] = $true }
        if ($SkipTests) { $windowsArgs['SkipTests'] = $true }
        
        $windowsScript = Join-Path $INSTALLER_DIR "build-windows.ps1"
        & $windowsScript @windowsArgs
        
        if ($LASTEXITCODE -eq 0 -or $null -eq $LASTEXITCODE) {
            Write-Host "✓ Windows build completed`n" -ForegroundColor Green
        } else {
            $buildErrors += "Windows build failed with exit code $LASTEXITCODE"
            Write-Host "✗ Windows build failed with exit code $LASTEXITCODE`n" -ForegroundColor Red
        }
    }
} else {
    Write-Host "Skipping Windows build (use -SkipWindows=`$false to enable)`n" -ForegroundColor DarkGray
}

# Summary
Write-Host "`n╔════════════════════════════════════════════════════╗" -ForegroundColor $(if ($buildErrors.Count -gt 0) { "Yellow" } else { "Green" })
Write-Host "║   Build Summary                                    ║" -ForegroundColor $(if ($buildErrors.Count -gt 0) { "Yellow" } else { "Green" })
Write-Host "╚════════════════════════════════════════════════════╝`n" -ForegroundColor $(if ($buildErrors.Count -gt 0) { "Yellow" } else { "Green" })

if ($buildErrors.Count -gt 0) {
    Write-Host "Build completed with errors:" -ForegroundColor Yellow
    foreach ($buildError in $buildErrors) {
        Write-Host "  ✗ $buildError" -ForegroundColor Red
    }
    Write-Host ""
}

Write-Host "Distribution artifacts:" -ForegroundColor Cyan

$linuxDir = Join-Path $DIST_DIR "linux"
$windowsDir = Join-Path $DIST_DIR "windows"
$linuxArtifacts = Get-ChildItem $linuxDir -ErrorAction SilentlyContinue
$windowsArtifacts = Get-ChildItem $windowsDir -ErrorAction SilentlyContinue

$artifacts = @($linuxArtifacts) + @($windowsArtifacts)
if ($artifacts) {
    if ($linuxArtifacts) {
        Write-Host "`n  Linux ($linuxDir):" -ForegroundColor Cyan
        $linuxArtifacts | ForEach-Object {
            $sizeMB = [math]::Round($_.Length / 1MB, 2)
            $sizeStr = if ($sizeMB -lt 1) {
                "$([math]::Round($_.Length / 1KB, 0)) KB"
            } else {
                "$sizeMB MB"
            }
            Write-Host ("    ✓ {0,-18} {1,10}" -f $_.Name, $sizeStr) -ForegroundColor Green
        }
    }
    
    if ($windowsArtifacts) {
        Write-Host "`n  Windows ($windowsDir):" -ForegroundColor Cyan
        $windowsArtifacts | ForEach-Object {
            $sizeMB = [math]::Round($_.Length / 1MB, 2)
            $sizeStr = if ($sizeMB -lt 1) {
                "$([math]::Round($_.Length / 1KB, 0)) KB"
            } else {
                "$sizeMB MB"
            }
            Write-Host ("    ✓ {0,-18} {1,10}" -f $_.Name, $sizeStr) -ForegroundColor Green
        }
    }
} else {
    Write-Host "  (no artifacts found)" -ForegroundColor DarkGray
}

Write-Host "`nNext steps:" -ForegroundColor Yellow
if ($linuxArtifacts) {
    Write-Host "  Linux deployment:" -ForegroundColor Cyan
    Write-Host "    cd installer; .\NewStone.ps1 -UsbDrive G:"
}
if ($windowsArtifacts) {
    Write-Host "  Windows testing:" -ForegroundColor Cyan
    Write-Host "    .\dist\windows\garden-rake.exe list"
    if (Test-Path "$windowsDir\garden-moss.exe") {
        Write-Host "    .\dist\windows\garden-moss.exe --help"
    }
}
Write-Host ""

# Exit with error if any build failed
if ($buildErrors.Count -gt 0) {
    exit 1
}

