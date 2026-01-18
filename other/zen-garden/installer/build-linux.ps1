<#
.SYNOPSIS
    Build Zen Garden Linux binaries using Docker

.DESCRIPTION
    Intelligently builds moss and garden-rake binaries for Linux:
    - On Windows: Uses Docker container for Linux cross-compilation
    - On Linux: Can build natively or use Docker for consistency
    - Detects existing build container and reuses it (perennial)
    - Only rebuilds container when Dockerfile changes or forced

.PARAMETER Release
    Build optimized release binaries (default: debug)

.PARAMETER ForceRebuild
    Force rebuild of Docker build container

.PARAMETER Native
    On Linux: build natively instead of using Docker

.PARAMETER CheckUpdates
    Check for outdated dependencies before building

.EXAMPLE
    .\build-linux.ps1 -Release
    # Build release binaries using Docker (reuses existing image)

.EXAMPLE
    .\build-linux.ps1 -ForceRebuild
    # Rebuild Docker image and compile debug binaries

.EXAMPLE
    .\build-linux.ps1 -Native -Release
    # On Linux: build natively without Docker

.EXAMPLE
    .\build-linux.ps1 -CheckUpdates
    # Check for outdated crates before building
#>

[CmdletBinding()]
param(
    [switch]$Release,
    [switch]$ForceRebuild,
    [switch]$Native,
    [switch]$CheckUpdates
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$WORKSPACE_ROOT = (Get-Item $PSScriptRoot).Parent.FullName
$DIST_DIR = Join-Path $WORKSPACE_ROOT "dist"
$LINUX_DIR = Join-Path $DIST_DIR "linux"
$IMAGE_NAME = "zen-garden-builder:latest"

Write-Host "`n╔════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Zen Garden Distribution Build                   ║" -ForegroundColor Cyan
Write-Host "╚════════════════════════════════════════════════════╝`n" -ForegroundColor Cyan

# Detect platform
$IsLinuxHost = $PSVersionTable.Platform -eq "Unix" -and $PSVersionTable.OS -match "Linux"
$IsWslHost = $env:WSL_DISTRO_NAME -ne $null
$UseDocker = -not ($IsLinuxHost -and $Native)

Write-Host "Platform Detection:" -ForegroundColor Yellow
Write-Host "  OS: $(if ($IsWindows) { 'Windows' } elseif ($IsLinuxHost) { 'Linux' } else { 'Unix' })"
if ($IsWslHost) { Write-Host "  Environment: WSL ($env:WSL_DISTRO_NAME)" }
Write-Host "  Build Method: $(if ($UseDocker) { 'Docker Container' } else { 'Native' })"
Write-Host ""

# Create dist directories
New-Item -ItemType Directory -Force -Path $LINUX_DIR | Out-Null

if ($UseDocker) {
    # Docker-based build (Windows, or Linux with Docker preference)
    
    # Check Docker availability
    try {
        docker version | Out-Null
    } catch {
        Write-Host "✗ Docker not available." -ForegroundColor Red
        if ($IsWindows) {
            Write-Host "  Install Docker Desktop: https://www.docker.com/products/docker-desktop/" -ForegroundColor Yellow
        } else {
            Write-Host "  Install Docker Engine or use -Native flag for native build" -ForegroundColor Yellow
        }
        exit 1
    }
    
    # Check if perennial build image exists
    $existingImage = docker images -q $IMAGE_NAME 2>$null
    $needsBuild = $ForceRebuild -or (-not $existingImage)
    
    if ($existingImage -and -not $ForceRebuild) {
        Write-Host "Build Container:" -ForegroundColor Yellow
        Write-Host "  ✓ Using existing image: $IMAGE_NAME" -ForegroundColor Green
        Write-Host "    (Use -ForceRebuild to recreate)" -ForegroundColor DarkGray
        Write-Host ""
    } else {
        Write-Host "Build Container:" -ForegroundColor Yellow
        Write-Host "  $(if ($ForceRebuild) { 'Rebuilding' } else { 'Creating' }) image: $IMAGE_NAME"
        
        Push-Location $WORKSPACE_ROOT
        try {
            docker build -f Dockerfile.build -t $IMAGE_NAME . --quiet
            if ($LASTEXITCODE -ne 0) { throw "Docker build failed" }
            Write-Host "  ✓ Image ready`n" -ForegroundColor Green
        } finally {
            Pop-Location
        }
    }
    
    # Determine build type
    $buildProfile = if ($Release) { "release" } else { "debug" }
    $buildFlag = if ($Release) { "--release" } else { "" }
    
    # Docker-based build
    Write-Host "Building binaries in container..." -ForegroundColor Cyan
    
    # Determine volume mount path (Windows uses /drive/path format)
    if ($IsWindows) {
        $driveLetter = $WORKSPACE_ROOT.Substring(0,1).ToLower()
        $unixPath = "/$driveLetter" + $WORKSPACE_ROOT.Substring(2).Replace('\', '/')
    } else {
        $unixPath = $WORKSPACE_ROOT
    }
    
    Push-Location $WORKSPACE_ROOT
    try {
        Write-Host "  → Building garden-moss (Linux daemon)..."
        Write-Host "  → Building garden-lantern (Linux service registry)..."
        Write-Host "  → Building garden-rake (Linux CLI)..."
        
        # Generate build number if not already set by parent script
        if (-not $env:CARGO_BUILD_NUMBER) {
            $env:CARGO_BUILD_NUMBER = (Get-Date).ToString("yyyyMMdd.HHmm")
            Write-Host "  Build Number: $env:CARGO_BUILD_NUMBER" -ForegroundColor Cyan
        }
        
        # Build all three binaries in one container run for efficiency
        $buildArgs = @("cargo", "build")
        if ($Release) { $buildArgs += "--release" }
        $buildArgs += @("--bin", "garden-moss", "--bin", "garden-lantern", "--bin", "garden-rake")
        
        $containerName = "zen-garden-builder-container"
        
        # Check if container already exists and is running
        $existingContainer = docker ps --filter "name=^/${containerName}$" --format "{{.Names}}" 2>$null
        $stoppedContainer = docker ps -a --filter "name=^/${containerName}$" --filter "status=exited" --format "{{.Names}}" 2>$null
        
        if ($existingContainer -eq $containerName) {
            Write-Host "  → Using running container: $containerName" -ForegroundColor DarkGray
        } elseif ($stoppedContainer -eq $containerName) {
            Write-Host "  → Starting existing container: $containerName" -ForegroundColor DarkGray
            docker start $containerName | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Failed to start container" }
        } else {
            Write-Host "  → Creating new container: $containerName" -ForegroundColor DarkGray
            
            docker run -d `
                --name $containerName `
                -v "${unixPath}:/build" `
                -v "zen-garden-cargo-cache:/root/.cargo" `
                -w /build `
                $IMAGE_NAME `
                tail -f /dev/null
            
            if ($LASTEXITCODE -ne 0) { throw "Failed to create container" }
        }
        
        # Check for outdated dependencies if requested
        if ($CheckUpdates) {
            Write-Host "`n  Checking for outdated dependencies..." -ForegroundColor Yellow
            docker exec $containerName cargo outdated --workspace --root-deps-only 2>$null
            if ($LASTEXITCODE -ne 0) {
                Write-Host "  → cargo-outdated not installed, installing..." -ForegroundColor DarkYellow
                docker exec $containerName cargo install cargo-outdated
            }
            Write-Host ""
        }
        
        # Execute build in the persistent container with build number
        docker exec -e CARGO_BUILD_NUMBER=$env:CARGO_BUILD_NUMBER $containerName $buildArgs
        
        if ($LASTEXITCODE -ne 0) { throw "Build failed" }
        
        # Copy binaries from target to dist/linux/
        $srcDir = Join-Path $WORKSPACE_ROOT "target" $buildProfile
        Copy-Item "$srcDir\garden-moss" "$LINUX_DIR\garden-moss" -Force
        Copy-Item "$srcDir\garden-rake" "$LINUX_DIR\garden-rake" -Force
        Copy-Item "$srcDir\garden-lantern" "$LINUX_DIR\garden-lantern" -Force
        
        Write-Host "  ✓ Linux binaries built`n" -ForegroundColor Green
        
    } finally {
        Pop-Location
    }
    
} else {
    # Native Linux build
    Write-Host "Building binaries natively..." -ForegroundColor Cyan
    
    # Determine build type
    $buildProfile = if ($Release) { "release" } else { "debug" }
    $buildFlag = if ($Release) { "--release" } else { "" }
    
    Push-Location $WORKSPACE_ROOT
    try {
        Write-Host "  → Building garden-moss (Linux daemon)..."
        Write-Host "  → Building garden-lantern (Linux service registry)..."
        Write-Host "  → Building garden-rake (Linux CLI)..."
        
        $buildArgs = @("build")
        if ($Release) { $buildArgs += "--release" }
        $buildArgs += @("--bin", "garden-moss", "--bin", "garden-lantern", "--bin", "garden-rake")
        
        cargo @buildArgs
        
        if ($LASTEXITCODE -ne 0) { throw "Build failed" }
        
        # Copy binaries from target to dist/linux/
        $srcDir = Join-Path $WORKSPACE_ROOT "target" $buildProfile
        Copy-Item "$srcDir/garden-lantern" "$LINUX_DIR/garden-lantern" -Force
        Copy-Item "$srcDir/garden-moss" "$LINUX_DIR/garden-moss" -Force
        Copy-Item "$srcDir/garden-rake" "$LINUX_DIR/garden-rake" -Force
        
        Write-Host "  ✓ Binaries built`n" -ForegroundColor Green
        
    } finally {
        Pop-Location
    }
}

# Display results
Write-Host "╔════════════════════════════════════════════════════╗" -ForegroundColor Green
Write-Host "║   Build Complete!                                  ║" -ForegroundColor Green
Write-Host "╚════════════════════════════════════════════════════╝`n" -ForegroundColor Green

Write-Host "Artifacts in $LINUX_DIR`:" -ForegroundColor Cyan

$artifacts = Get-ChildItem $LINUX_DIR -ErrorAction SilentlyContinue
if ($artifacts) {
    $artifacts | ForEach-Object {
        $sizeMB = [math]::Round($_.Length / 1MB, 2)
        $sizeStr = if ($sizeMB -lt 1) {
            "$([math]::Round($_.Length / 1KB, 0)) KB"
        } else {
            "$sizeMB MB"
        }
        
        # Verify binary type (platform-conditional)
        $marker = "-"
        if ($UseDocker -and $existingImage) {
            try {
                $fileType = docker run --rm -v "${LINUX_DIR}:/check" $IMAGE_NAME file "/check/$($_.Name)" 2>$null
                $isLinux = $fileType -match "ELF.*Linux"
                $marker = if ($isLinux) { "✓" } else { "?" }
            } catch {
                $marker = "-"
            }
        } elseif ($IsLinuxHost) {
            $fileType = file $_.FullName 2>$null
            $isLinux = $fileType -match "ELF"
            $marker = if ($isLinux) { "✓" } else { "?" }
        }
        
        Write-Host ("  {0} {1,-20} {2,10}" -f $marker, $_.Name, $sizeStr) -ForegroundColor $(if ($marker -eq "✓") { "Green" } else { "White" })
    }
} else {
    Write-Host "  (no artifacts found)" -ForegroundColor DarkGray
}

Write-Host "`nNext steps:" -ForegroundColor Yellow
Write-Host "  1. Create USB: cd installer; .\NewStone.ps1 -UsbDrive G:"
Write-Host "  2. Deploy to Stone and test"
if ($UseDocker -and $existingImage -and -not $ForceRebuild) {
    Write-Host "  (Build container cached for next run)" -ForegroundColor DarkGray
}
Write-Host ""
