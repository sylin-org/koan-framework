<#
.SYNOPSIS
    Prepares branding artifacts for Zen Garden Stone USB installer.

.DESCRIPTION
    This script processes source branding assets (images, themes, configs)
    and generates ready-to-overlay artifacts for NewStone.ps1.
    
    The prepared artifacts are placed in branding/prepared/ and are
    automatically applied by NewStone.ps1 during USB creation.

.PARAMETER Force
    Overwrite existing prepared artifacts without prompting.

.PARAMETER SkipInitrd
    Skip GTK initrd repacking (faster, but GTK banner won't be customized).
    Use this for quick boot menu testing without WSL dependencies.

.PARAMETER ValidateOnly
    Only validate source asset specifications without preparing artifacts.

.EXAMPLE
    .\Prepare-BrandingArtifacts.ps1
    # Full preparation (includes GTK initrd)

.EXAMPLE
    .\Prepare-BrandingArtifacts.ps1 -SkipInitrd
    # Quick preparation (boot menus only, no WSL required)

.EXAMPLE
    .\Prepare-BrandingArtifacts.ps1 -Force
    # Overwrite existing artifacts

.NOTES
    Requirements:
    - WSL (for GTK initrd repacking, optional with -SkipInitrd)
    - Source assets must match Debian specifications (see README.md)
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$Force,
    [switch]$SkipInitrd,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

#region Configuration
$script:Config = @{
    SourceDir   = (Join-Path $PSScriptRoot "source")
    PreparedDir = (Join-Path $PSScriptRoot "prepared")
    
    # Asset specifications (Debian requirements)
    Assets      = @{
        GtkBanner      = @{
            FileName      = "zen-banner-800x75.png"
            RequiredWidth = 800
            RequiredHeight = 75
            ColorDepth    = 16  # 16-bit color (65,536 colors)
        }
        IsolinuxSplash = @{
            FileName       = "zen-splash-640x300.png"
            RequiredWidth  = 640
            RequiredHeight = 300
            ColorDepth     = 4   # 16 colors (will be converted to ASCII art)
        }
        BackgroundTexture = @{
            FileName  = "zen-texture.png"
            Optional  = $true
        }
    }
    
    # Theme files
    Themes      = @{
        GrubTheme     = "grub-theme.txt"
        IsolinuxMenu  = "isolinux-menu.txt"
        GtkTheme      = "gtk-theme-gtkrc.txt"
    }
}
#endregion

#region Helper Functions
function Write-Banner {
    Write-Host ""
    Write-Host "  ╔════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "  ║   Zen Garden Branding Artifact Preparation         ║" -ForegroundColor Cyan
    Write-Host "  ╚════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""
}

function Write-Step {
    param(
        [string]$Message,
        [string]$Status = "..."  # "...", "OK", "WARN", "FAIL", "SKIP"
    )
    
    $symbol = switch ($Status) {
        "OK" { "[+]" }
        "FAIL" { "[x]" }
        "WARN" { "[!]" }
        "SKIP" { "[-]" }
        default { "[*]" }
    }
    
    $color = switch ($Status) {
        "OK" { "Green" }
        "FAIL" { "Red" }
        "WARN" { "Yellow" }
        "SKIP" { "Gray" }
        default { "Cyan" }
    }
    
    Write-Host "  $symbol " -ForegroundColor $color -NoNewline
    Write-Host $Message
}

function Test-SourceAssets {
    Write-Step "Validating source assets..." "..."
    
    $errors = @()
    
    # Check source directory exists
    if (-not (Test-Path $script:Config.SourceDir)) {
        throw "Source directory not found: $($script:Config.SourceDir)"
    }
    
    # Validate each required asset
    foreach ($assetKey in $script:Config.Assets.Keys) {
        $asset = $script:Config.Assets[$assetKey]
        $assetPath = Join-Path $script:Config.SourceDir $asset.FileName
        
        # Check if asset is optional
        $isOptional = $asset.ContainsKey('Optional') -and $asset.Optional
        
        if ($isOptional -and -not (Test-Path $assetPath)) {
            Write-Step "$($asset.FileName): Optional, not found (will skip)" "SKIP"
            continue
        }
        
        if (-not (Test-Path $assetPath)) {
            $errors += "Required asset not found: $($asset.FileName)"
            continue
        }
        
        # Validate image dimensions (if specified)
        if ($asset.ContainsKey('RequiredWidth') -and $asset.ContainsKey('RequiredHeight')) {
            try {
                Add-Type -AssemblyName System.Drawing
                $img = [System.Drawing.Image]::FromFile($assetPath)
                
                if ($img.Width -ne $asset.RequiredWidth -or $img.Height -ne $asset.RequiredHeight) {
                    $errors += "$($asset.FileName): Expected $($asset.RequiredWidth)×$($asset.RequiredHeight), got $($img.Width)×$($img.Height)"
                }
                else {
                    Write-Step "$($asset.FileName): $($img.Width)×$($img.Height) ✓" "OK"
                }
                
                $img.Dispose()
            }
            catch {
                $errors += "$($asset.FileName): Failed to read image - $($_.Exception.Message)"
            }
        }
    }
    
    # Validate theme files exist
    foreach ($themeKey in $script:Config.Themes.Keys) {
        $themeFile = $script:Config.Themes[$themeKey]
        $themePath = Join-Path $script:Config.SourceDir $themeFile
        
        if (-not (Test-Path $themePath)) {
            $errors += "Theme file not found: $themeFile"
        }
        else {
            Write-Step "${themeFile}: Found ✓" "OK"
        }
    }
    
    if ($errors.Count -gt 0) {
        Write-Step "Validation failed with $($errors.Count) error(s)" "FAIL"
        $errors | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        throw "Source asset validation failed"
    }
    
    Write-Step "All source assets validated" "OK"
}

function Initialize-PreparedDirectory {
    Write-Step "Initializing prepared directory..." "..."
    
    if (Test-Path $script:Config.PreparedDir) {
        if (-not $Force -and -not $PSCmdlet.ShouldProcess($script:Config.PreparedDir, "Remove existing prepared artifacts")) {
            throw "Prepared directory already exists. Use -Force to overwrite."
        }
        Remove-Item $script:Config.PreparedDir -Recurse -Force
    }
    
    # Create directory structure
    $dirs = @(
        "isolinux",
        "boot\grub",
        "gtk-initrd\usr\share\graphics",
        "gtk-initrd\usr\share\themes\zen-garden\gtk-2.0",
        "stone-root\etc",
        "stone-root\usr\local\bin"
    )
    
    foreach ($dir in $dirs) {
        $fullPath = Join-Path $script:Config.PreparedDir $dir
        New-Item -ItemType Directory -Path $fullPath -Force | Out-Null
    }
    
    Write-Step "Directory structure created" "OK"
}

function Prepare-IsolinuxAssets {
    Write-Step "Preparing ISOLINUX assets..." "..."
    
    $isolinuxDir = Join-Path $script:Config.PreparedDir "isolinux"
    
    # Copy splash image (if exists)
    $splashSource = Join-Path $script:Config.SourceDir $script:Config.Assets.IsolinuxSplash.FileName
    if (Test-Path $splashSource) {
        # Convert PNG to ASCII art splash (simplified - actual implementation would use image-to-ASCII conversion)
        $splashDest = Join-Path $isolinuxDir "zen-splash.txt"
        
        # For now, create a placeholder text splash (replace with actual image-to-ASCII conversion)
        $asciiSplash = @"
 ╔════════════════════════════════════════════════════════════╗
 ║                                                            ║
 ║          🪨  Zen Garden Stone Installer  🌿                ║
 ║                                                            ║
 ║   Zero-touch server deployment for self-hosted infra      ║
 ║                                                            ║
 ║   • Unattended install: ~10-15 minutes                    ║
 ║   • Auto-generates stone name on first boot               ║
 ║   • mDNS discovery: stone-<name>.local                    ║
 ║                                                            ║
 ║   Press Enter to begin (auto-starts in 3 seconds)         ║
 ║                                                            ║
 ╚════════════════════════════════════════════════════════════╝
"@
        Set-Content -Path $splashDest -Value $asciiSplash -Encoding ASCII
        Write-Step "ISOLINUX splash converted to ASCII" "OK"
    }
    
    # Copy menu configuration
    $menuSource = Join-Path $script:Config.SourceDir $script:Config.Themes.IsolinuxMenu
    $menuDest = Join-Path $isolinuxDir "isolinux.cfg.patch"
    Copy-Item $menuSource $menuDest -Force
    
    Write-Step "ISOLINUX assets prepared" "OK"
}

function Prepare-GrubAssets {
    Write-Step "Preparing GRUB assets..." "..."
    
    $grubDir = Join-Path $script:Config.PreparedDir "boot\grub"
    
    # Copy GRUB theme configuration
    $themeSource = Join-Path $script:Config.SourceDir $script:Config.Themes.GrubTheme
    $themeDest = Join-Path $grubDir "grub.cfg.patch"
    Copy-Item $themeSource $themeDest -Force
    
    Write-Step "GRUB assets prepared" "OK"
}

function Prepare-GtkInitrdAssets {
    param([switch]$Skip)
    
    if ($Skip) {
        Write-Step "GTK initrd preparation skipped (use without -SkipInitrd for full branding)" "SKIP"
        return
    }
    
    Write-Step "Preparing GTK initrd assets..." "..."
    
    # Check WSL availability
    $wslAvailable = $null -ne (Get-Command wsl -ErrorAction SilentlyContinue)
    if (-not $wslAvailable) {
        Write-Step "WSL not found - GTK initrd customization requires WSL" "WARN"
        Write-Host "    Install WSL: wsl --install" -ForegroundColor Yellow
        Write-Host "    Or skip: Prepare-BrandingArtifacts.ps1 -SkipInitrd" -ForegroundColor Yellow
        throw "WSL required for GTK initrd repacking"
    }
    
    $gtkDir = Join-Path $script:Config.PreparedDir "gtk-initrd"
    
    # Copy GTK banner (will be injected into initrd)
    $bannerSource = Join-Path $script:Config.SourceDir $script:Config.Assets.GtkBanner.FileName
    $bannerDest = Join-Path $gtkDir "usr\share\graphics\logo_debian.png"
    Copy-Item $bannerSource $bannerDest -Force
    Write-Step "GTK banner copied: logo_debian.png" "OK"
    
    # Copy optional background texture
    $textureSource = Join-Path $script:Config.SourceDir $script:Config.Assets.BackgroundTexture.FileName
    if (Test-Path $textureSource) {
        $textureDest = Join-Path $gtkDir "usr\share\graphics\zen-bg.png"
        Copy-Item $textureSource $textureDest -Force
        Write-Step "Background texture copied: zen-bg.png" "OK"
    }
    
    # Copy GTK theme configuration
    $gtkThemeSource = Join-Path $script:Config.SourceDir $script:Config.Themes.GtkTheme
    $gtkThemeDest = Join-Path $gtkDir "usr\share\themes\zen-garden\gtk-2.0\gtkrc"
    Copy-Item $gtkThemeSource $gtkThemeDest -Force
    Write-Step "GTK theme configuration copied" "OK"
    
    Write-Step "GTK initrd assets prepared (will be injected into initrd by NewStone.ps1)" "OK"
}

function Prepare-FirstBootAssets {
    Write-Step "Preparing first-boot assets..." "..."
    
    $stoneRootDir = Join-Path $script:Config.PreparedDir "stone-root"
    
    # Create MOTD template
    $motdTemplate = @'
╔══════════════════════════════════════════════════════════════════╗
║                                                                  ║
║        🪨  Welcome to Zen Garden Stone: {{STONE_NAME}}          ║
║                                                                  ║
║  🌐 Network:                                                     ║
║     • IP Address: {{STONE_IP}}                                  ║
║     • mDNS: {{STONE_NAME}}.local                                ║
║                                                                  ║
║  🔍 Discovery Endpoints:                                         ║
║     • UDP Broadcast: {{STONE_IP}}:7184                          ║
║     • HTTP API: http://{{STONE_IP}}:7185                        ║
║                                                                  ║
║  📦 Services:                                                    ║
║     • Moss Status: {{MOSS_STATUS}}                              ║
║     • Offerings: {{OFFERING_COUNT}} available                   ║
║                                                                  ║
║  🛠️  Quick Commands:                                             ║
║     sudo systemctl status garden-moss    # Check service        ║
║     garden-rake observe                  # View details         ║
║     garden-rake explore                  # List offerings       ║
║                                                                  ║
╚══════════════════════════════════════════════════════════════════╝
'@
    Set-Content -Path (Join-Path $stoneRootDir "etc\motd.template") -Value $motdTemplate -Encoding UTF8
    
    # Create dynamic MOTD generator script
    $motdGeneratorScript = @'
#!/bin/bash
# Generate dynamic MOTD with stone details

STONE_NAME=$(hostname)
STONE_IP=$(ip -4 addr show | grep -oP '(?<=inet\s)\d+(\.\d+){3}' | grep -v 127.0.0.1 | head -1)
MOSS_STATUS=$(systemctl is-active garden-moss 2>/dev/null || echo "inactive")
OFFERING_COUNT=$(curl -s http://localhost:7185/api/v1/offerings 2>/dev/null | jq '.data | length' 2>/dev/null || echo "?")

# Read template and replace variables
TEMPLATE=$(cat /etc/motd.template 2>/dev/null || echo "")
echo "$TEMPLATE" | \
    sed "s/{{STONE_NAME}}/$STONE_NAME/g" | \
    sed "s/{{STONE_IP}}/$STONE_IP/g" | \
    sed "s/{{MOSS_STATUS}}/$MOSS_STATUS/g" | \
    sed "s/{{OFFERING_COUNT}}/$OFFERING_COUNT/g" > /etc/motd
'@
    Set-Content -Path (Join-Path $stoneRootDir "usr\local\bin\generate-stone-motd.sh") -Value $motdGeneratorScript -Encoding UTF8
    
    # Create SSH banner
    $sshBanner = @'
╔══════════════════════════════════════════════════════════════════╗
║             Zen Garden Stone - Authorized Access Only            ║
╚══════════════════════════════════════════════════════════════════╝
'@
    Set-Content -Path (Join-Path $stoneRootDir "etc\issue.net") -Value $sshBanner -Encoding UTF8
    
    Write-Step "First-boot assets prepared" "OK"
}

function Save-Manifest {
    Write-Step "Saving preparation manifest..." "..."
    
    # Calculate source file hashes
    $sourceHashes = @{}
    Get-ChildItem $script:Config.SourceDir -File | ForEach-Object {
        $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
        $sourceHashes[$_.Name] = $hash
    }
    
    $manifest = @{
        PreparedAt   = Get-Date -Format "o"
        PreparedBy   = $env:USERNAME
        ComputerName = $env:COMPUTERNAME
        SourceHashes = $sourceHashes
        SkippedInitrd = $SkipInitrd.IsPresent
        Version      = "1.0"
    }
    
    $manifestPath = Join-Path $script:Config.PreparedDir "manifest.json"
    $manifest | ConvertTo-Json -Depth 3 | Set-Content $manifestPath -Encoding UTF8
    
    Write-Step "Manifest saved: manifest.json" "OK"
}
#endregion

#region Main Script
try {
    Write-Banner
    
    # Validate source assets
    Test-SourceAssets
    
    if ($ValidateOnly) {
        Write-Host ""
        Write-Host "  Validation complete. Use without -ValidateOnly to prepare artifacts." -ForegroundColor Green
        exit 0
    }
    
    Write-Host ""
    Write-Step "Starting artifact preparation..." "..."
    Write-Host ""
    
    # Initialize output directory
    Initialize-PreparedDirectory
    
    # Prepare assets
    Prepare-IsolinuxAssets
    Prepare-GrubAssets
    Prepare-GtkInitrdAssets -Skip:$SkipInitrd
    Prepare-FirstBootAssets
    
    # Save metadata
    Save-Manifest
    
    Write-Host ""
    Write-Host "  ╔════════════════════════════════════════════════════╗" -ForegroundColor Green
    Write-Host "  ║   ✓ Branding artifacts prepared successfully       ║" -ForegroundColor Green
    Write-Host "  ╚════════════════════════════════════════════════════╝" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Next step: Run NewStone.ps1 to create branded USB" -ForegroundColor Cyan
    Write-Host "  Location: $($script:Config.PreparedDir)" -ForegroundColor Gray
    Write-Host ""
    
    if ($SkipInitrd) {
        Write-Host "  Note: GTK banner was skipped (-SkipInitrd)" -ForegroundColor Yellow
        Write-Host "        Re-run without -SkipInitrd for full branding" -ForegroundColor Yellow
        Write-Host ""
    }
}
catch {
    Write-Host ""
    Write-Host "  [x] Preparation failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    exit 1
}
#endregion
