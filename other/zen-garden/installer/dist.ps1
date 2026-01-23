<#
.SYNOPSIS
    Build complete Zen Garden distribution (Linux + Windows)

.DESCRIPTION
    Orchestrates builds for all platforms:
    - Linux (garden-moss, garden-rake) via build-linux.ps1
    - Windows (garden-moss.exe, garden-rake.exe) via build-windows.ps1

    This is the main entry point for full distribution builds.
    Default: fast-release profile (thin LTO, parallel codegen) - best for iteration.

.PARAMETER DebugBuild
    Build debug binaries (fastest compile, largest size, no optimization)

.PARAMETER Release
    Build full-release binaries (full LTO, codegen-units=1)
    Slower build but smallest binaries. Use for final production builds.

.PARAMETER SkipTests
    Skip running tests before build (default: tests are skipped)

.PARAMETER RunTests
    Run tests before build (overrides default skip)

.PARAMETER SkipLinux
    Skip Linux build (build Windows only)

.PARAMETER SkipWindows
    Skip Windows build (build Linux only)

.PARAMETER ForceRebuild
    Force rebuild of Docker build container (Linux only)

.PARAMETER Jobs
    Number of parallel cargo jobs (default: number of CPUs)

.PARAMETER SkipPackages
    Skip creating deployment packages after build

.EXAMPLE
    .\dist.ps1
    # Default: fast-release, skip tests, all platforms

.EXAMPLE
    .\dist.ps1 -Release
    # Full LTO release (smallest binaries, slower build)

.EXAMPLE
    .\dist.ps1 -RunTests
    # Fast-release with tests

.EXAMPLE
    .\dist.ps1 -DebugBuild
    # Debug binaries (fastest compile, largest size)

.EXAMPLE
    .\dist.ps1 -SkipWindows
    # Build Linux binaries only
#>

[CmdletBinding()]
param(
    [switch]$DebugBuild,
    [switch]$Release,
    [switch]$SkipTests,
    [switch]$RunTests,
    [switch]$SkipLinux,
    [switch]$SkipWindows,
    [switch]$ForceRebuild,
    [switch]$SkipPackages,
    [int]$Jobs = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Fix for Windows PowerShell (doesn't have $IsWindows automatic variable)
if ($null -eq (Get-Variable -Name IsWindows -ErrorAction SilentlyContinue)) {
    $IsWindows = $env:OS -eq "Windows_NT"
}

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
$env:CARGO_BUILD_NUMBER = $revision  # For Rust build.rs

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
    
    # Default: fast-release. Use -Release for full LTO.
    $dockerArgs = @{}
    if ($DebugBuild) { $dockerArgs.Add('DebugBuild', $true) }
    if (-not $Release -and -not $DebugBuild) { $dockerArgs.Add('Fast', $true) }
    if ($ForceRebuild) { $dockerArgs.Add('ForceRebuild', $true) }
    if ($Jobs -gt 0) { $dockerArgs.Add('Jobs', $Jobs) }

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
        
        # Default: fast-release, skip tests. Use -Release for full LTO, -RunTests for tests.
        $windowsArgs = @{}
        if ($DebugBuild) { $windowsArgs['DebugBuild'] = $true }
        if (-not $Release -and -not $DebugBuild) { $windowsArgs['Fast'] = $true }
        if (-not $RunTests) { $windowsArgs['SkipTests'] = $true }
        if ($Jobs -gt 0) { $windowsArgs['Jobs'] = $Jobs }

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

# Create deployment packages
if (-not $SkipPackages -and $buildErrors.Count -eq 0) {
    Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
    Write-Host " Phase 3: Create Deployment Packages" -ForegroundColor Cyan
    Write-Host "═══════════════════════════════════════════════════`n" -ForegroundColor Cyan

    $packagesDir = Join-Path $DIST_DIR "packages"
    New-Item -ItemType Directory -Force -Path $packagesDir | Out-Null

    # Clean up old packages (keep only latest)
    Get-ChildItem $packagesDir -File -ErrorAction SilentlyContinue | Remove-Item -Force

    $manifestsDir = Join-Path $WORKSPACE_ROOT "manifests"
    $linuxDir = Join-Path $DIST_DIR "linux"
    $windowsDir = Join-Path $DIST_DIR "windows"

    # Helper function to create package manifest
    function New-PackageManifest {
        param(
            [string]$Platform,
            [string]$BinDir,
            [string]$ManifestsDir,
            [string]$ScriptsDir,
            [string]$Version,
            [string]$Description
        )

        $components = @{}
        $binExt = if ($Platform -eq "windows") { ".exe" } else { "" }

        foreach ($name in @("garden-moss", "garden-rake", "garden-lantern")) {
            $binaryPath = Join-Path $BinDir "$name$binExt"
            if (Test-Path $binaryPath) {
                $hash = (Get-FileHash $binaryPath -Algorithm SHA256).Hash.ToLower()
                $size = (Get-Item $binaryPath).Length
                $components[$name] = @{
                    path = "bin/$name$binExt"
                    sha256 = $hash
                    size = $size
                    required = $name -in @("garden-moss", "garden-rake")
                }
            }
        }

        $manifests = @{}
        if (Test-Path $ManifestsDir) {
            # Include all manifest files
            Get-ChildItem $ManifestsDir -Recurse -File | ForEach-Object {
                $relativePath = $_.FullName.Replace("$ManifestsDir\", "").Replace("\", "/")
                $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()
                $manifests[$relativePath] = $hash
            }
        }

        $scripts = @{}
        if ($ScriptsDir -and (Test-Path $ScriptsDir)) {
            # Include deployment scripts (Linux only)
            foreach ($scriptName in @("moss-update-helper.sh", "garden-upgrade.sh")) {
                $scriptPath = Join-Path $ScriptsDir $scriptName
                if (Test-Path $scriptPath) {
                    $hash = (Get-FileHash $scriptPath -Algorithm SHA256).Hash.ToLower()
                    $size = (Get-Item $scriptPath).Length
                    $scripts[$scriptName] = @{
                        path = "scripts/$scriptName"
                        sha256 = $hash
                        size = $size
                        target = "/usr/local/bin/$scriptName"
                    }
                }
            }
        }

        return @{
            version = $Version
            platform = $Platform
            architecture = "amd64"
            created = (Get-Date).ToUniversalTime().ToString("o")
            components = $components
            manifests = $manifests
            scripts = $scripts
            minimumVersion = $null
            notes = $Description
        }
    }

    # Create Linux package
    if ((Test-Path $linuxDir) -and (Get-ChildItem $linuxDir -File -ErrorAction SilentlyContinue)) {
        Write-Host "Creating Linux package..." -ForegroundColor DarkGray

        $packageName = "zen-garden-$version-linux-amd64"
        $packageDir = Join-Path $env:TEMP $packageName
        $tarPath = Join-Path $packagesDir "$packageName.tar.gz"
        $scriptsDir = $INSTALLER_DIR  # Scripts are in the installer directory

        # Clean and create package directory
        if (Test-Path $packageDir) { Remove-Item $packageDir -Recurse -Force }
        New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $packageDir "bin") -Force | Out-Null

        # Copy binaries
        Get-ChildItem $linuxDir -File | ForEach-Object {
            Copy-Item $_.FullName (Join-Path $packageDir "bin")
        }

        # Copy manifests if they exist
        if (Test-Path $manifestsDir) {
            Copy-Item $manifestsDir (Join-Path $packageDir "manifests") -Recurse
        }

        # Copy deployment scripts (Linux only)
        $scriptsPackageDir = Join-Path $packageDir "scripts"
        New-Item -ItemType Directory -Path $scriptsPackageDir -Force | Out-Null
        foreach ($scriptName in @("moss-update-helper.sh", "garden-upgrade.sh")) {
            $scriptPath = Join-Path $scriptsDir $scriptName
            if (Test-Path $scriptPath) {
                Copy-Item $scriptPath $scriptsPackageDir
            }
        }

        # Create manifest
        $manifest = New-PackageManifest -Platform "linux" -BinDir $linuxDir -ManifestsDir $manifestsDir -ScriptsDir $scriptsDir -Version $version -Description $versionData.description
        $manifest | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $packageDir "package.json") -Encoding UTF8

        # Create tar.gz using tar (available on Windows 10+)
        # Use -C to change directory and relative paths to avoid Windows path issues
        try {
            $tarFile = "$packageName.tar.gz"
            & tar -czf $tarFile -C $env:TEMP $packageName 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0 -and (Test-Path $tarFile)) {
                Move-Item $tarFile $tarPath -Force
                $sizeMB = [math]::Round((Get-Item $tarPath).Length / 1MB, 2)
                Write-Host "  ✓ $packageName.tar.gz ($sizeMB MB)" -ForegroundColor Green
            } else {
                Write-Host "  ✗ Failed to create Linux package (tar error: $LASTEXITCODE)" -ForegroundColor Red
                $buildErrors += "Linux package creation failed"
            }
        } finally {
            Remove-Item $packageDir -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item $tarFile -Force -ErrorAction SilentlyContinue
        }
    }

    # Create Windows package
    if ((Test-Path $windowsDir) -and (Get-ChildItem $windowsDir -File -ErrorAction SilentlyContinue)) {
        Write-Host "Creating Windows package..." -ForegroundColor DarkGray

        $packageName = "zen-garden-$version-windows-amd64"
        $packageDir = Join-Path $env:TEMP $packageName
        $zipPath = Join-Path $packagesDir "$packageName.zip"

        # Clean and create package directory
        if (Test-Path $packageDir) { Remove-Item $packageDir -Recurse -Force }
        New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $packageDir "bin") -Force | Out-Null

        # Copy binaries
        Get-ChildItem $windowsDir -File | ForEach-Object {
            Copy-Item $_.FullName (Join-Path $packageDir "bin")
        }

        # Copy manifests if they exist
        if (Test-Path $manifestsDir) {
            Copy-Item $manifestsDir (Join-Path $packageDir "manifests") -Recurse
        }

        # Create manifest (no scripts for Windows packages)
        $manifest = New-PackageManifest -Platform "windows" -BinDir $windowsDir -ManifestsDir $manifestsDir -ScriptsDir "" -Version $version -Description $versionData.description
        $manifest | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $packageDir "package.json") -Encoding UTF8

        # Create zip
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        Compress-Archive -Path $packageDir -DestinationPath $zipPath -Force

        $sizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
        Write-Host "  ✓ $packageName.zip ($sizeMB MB)" -ForegroundColor Green

        Remove-Item $packageDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host ""
} elseif ($SkipPackages) {
    Write-Host "Skipping package creation (use -SkipPackages:`$false to enable)`n" -ForegroundColor DarkGray
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
$packagesDir = Join-Path $DIST_DIR "packages"
$linuxArtifacts = Get-ChildItem $linuxDir -File -ErrorAction SilentlyContinue
$windowsArtifacts = Get-ChildItem $windowsDir -File -ErrorAction SilentlyContinue
$packageArtifacts = Get-ChildItem $packagesDir -File -ErrorAction SilentlyContinue

$artifacts = @($linuxArtifacts) + @($windowsArtifacts) + @($packageArtifacts)
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

    if ($packageArtifacts) {
        Write-Host "`n  Packages ($packagesDir):" -ForegroundColor Cyan
        $packageArtifacts | ForEach-Object {
            $sizeMB = [math]::Round($_.Length / 1MB, 2)
            $sizeStr = if ($sizeMB -lt 1) {
                "$([math]::Round($_.Length / 1KB, 0)) KB"
            } else {
                "$sizeMB MB"
            }
            Write-Host ("    ✓ {0,-35} {1,10}" -f $_.Name, $sizeStr) -ForegroundColor Green
        }
    }
} else {
    Write-Host "  (no artifacts found)" -ForegroundColor DarkGray
}

Write-Host "`nNext steps:" -ForegroundColor Yellow
if ($packageArtifacts) {
    Write-Host "  Package deployment:" -ForegroundColor Cyan
    Write-Host "    cd installer; .\push2all.ps1 -UsePackage"
}
if ($linuxArtifacts) {
    Write-Host "  Linux USB installer:" -ForegroundColor Cyan
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

