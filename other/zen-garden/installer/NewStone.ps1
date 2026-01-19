<#
.SYNOPSIS
    Creates a bootable USB drive that auto-installs a Zen Garden Stone.

.DESCRIPTION
    This script prepares a USB drive with Debian and preseed file
    configuration for fully unattended Zen Garden Stone installation.
    
    The target machine will:
    1. Boot from USB
    2. Auto-install Debian (no user interaction)
    3. Install Docker, Traefik, Registry, Homepage
    4. Configure mDNS discovery
    5. Display ready status with IP and name

.PARAMETER UsbDrive
    Optional. The drive letter of the USB drive (e.g., "E:" or "E").
    If not provided, auto-detects available USB drives.

.PARAMETER Force
    Skip confirmation prompts.

.EXAMPLE
    .\NewStone.ps1
    # Auto-detects USB drive

.EXAMPLE
    .\NewStone.ps1 -UsbDrive "E:"

.NOTES
    Requires: Windows 10/11, Administrator privileges, 8GB+ USB drive
    Author: Koan Framework Team
    License: Apache 2.0
#>

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $false)]
    [ValidatePattern('^[A-Za-z]:?$')]
    [string]$UsbDrive,

    [switch]$Force,
    
    [Parameter(Mandatory = $false)]
    [switch]$UpdateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

#region Configuration
$script:Config = @{
    # Debian ISO settings - dynamically detected latest stable version
    DebianVersion       = $null  # Auto-detected on first use
    IsoUrl              = $null  # Auto-detected on first use
    IsoSha256           = $null  # Auto-detected on first use
    IsoCachePath        = $null  # Set after version detection
    DebianBaseUrl       = "https://cdimage.debian.org/debian-cd/current/amd64/iso-cd"
    
    # Local paths
    CacheDir            = (Join-Path $PSScriptRoot "dependencies")
    ManifestsDir        = (Join-Path $PSScriptRoot "..\manifests")
    
    # Zen Garden binaries
    MossUrl             = "https://github.com/koan-framework/zen-garden/releases/latest/download/garden-moss-linux-amd64"
    MossPath            = (Join-Path $PSScriptRoot "..\dist\linux\garden-moss")
    GardenRakeUrl       = "https://github.com/koan-framework/zen-garden/releases/latest/download/garden-rake-linux-amd64"
    GardenRakePath      = (Join-Path $PSScriptRoot "..\dist\linux\garden-rake")
    
    # USB requirements
    MinUsbSizeGB        = 4
    RecommendedUsbGB    = 8
    
    # Default credentials (Lab mode - change recommended)
    DefaultUser         = "stone"
    # Password hash for 'stone' - SHA-256 crypt format
    # Generated with: docker run --rm alpine sh -c "echo 'stone' | mkpasswd -m sha-256"
    DefaultPasswordHash = '$5$GSU4ufbowaCNyICT$lXgLCJYSQL1q2soL5DNiQtDkciqqkH/9QDFVZUzb5WA'
    
    # Tool exit codes
    RobocopySuccessMax  = 7  # Robocopy exit codes 0-7 are success/warnings
    
    # UI constants
    BoxWidth            = 52
    BoxIndent           = '  '

    # Installer is intentionally agnostic about final stone naming.
    # Debian unattended install needs some hostname; moss will rename on first boot.
    InstallHostname     = "stone"
    
}

#endregion

#region Helper Functions
function Write-Banner {
    $banner = @"

  ╔════════════════════════════════════════════════════╗
  ║   Zen Garden Stone USB Creator                     ║
  ║   Creates bootable USB for auto stone install      ║
  ╚════════════════════════════════════════════════════╝

"@
    Write-Host $banner -ForegroundColor Cyan
}

function Write-Panel {
    param(
        [string]$Title = '',
        [string[]]$Lines = @(),
        [int]$InnerWidth = $script:Config.BoxWidth,
        [string]$TitleColor = 'Cyan',
        [string]$BodyColor = 'Cyan',
        [hashtable]$LineColors
    )

    if (-not $Lines) { $Lines = @() }
    if (-not $LineColors) { $LineColors = @{} }

    $indent = $script:Config.BoxIndent
    $top = "$indent┌" + ('─' * $InnerWidth) + '┐'
    $bottom = "$indent└" + ('─' * $InnerWidth) + '┘'

    Write-Host $top -ForegroundColor $TitleColor

    if ($Title) {
        $titleText = ($Title + ' ').PadRight($InnerWidth).Substring(0, $InnerWidth)
        Write-Host "$indent│$titleText│" -ForegroundColor $TitleColor
    }

    for ($i = 0; $i -lt $Lines.Count; $i++) {
        $text = ($Lines[$i] | ForEach-Object { $_ })
        if ($null -eq $text) { $text = '' }
        $safe = (' ' + $text.ToString()).PadRight($InnerWidth).Substring(0, $InnerWidth)
        $color = if ($LineColors.ContainsKey($i)) { $LineColors[$i] } else { $BodyColor }
        Write-Host "$indent│$safe│" -ForegroundColor $color
    }

    Write-Host $bottom -ForegroundColor $TitleColor
    Write-Host ""
}

function Write-Step {
    param(
        [string]$Message,
        [string]$Status = "..."  # "...", "OK", "WARN", "FAIL"
    )
    
    $symbol = switch ($Status) {
        "OK" { "[+]" }
        "FAIL" { "[x]" }
        "WARN" { "[!]" }
        default { "[*]" }
    }
    
    $color = switch ($Status) {
        "OK" { "Green" }
        "FAIL" { "Red" }
        "WARN" { "Yellow" }
        default { "Cyan" }
    }
    
    Write-Host "  $symbol " -ForegroundColor $color -NoNewline
    Write-Host $Message
}

function Show-StatusBar {
    param(
        [string]$UsbDrive = "?",
        [string]$StoneName = "?",
        [string]$StepLabel = ""
    )

    $lines = @(
        "USB Drive: $UsbDrive",
        "Stone: $StoneName"
    )
    
    if ($StepLabel) {
        $lines += "Step: $StepLabel"
    }

    Write-Panel -Title "Status" -Lines $lines -InnerWidth $script:Config.BoxWidth -TitleColor 'Cyan' -BodyColor 'Cyan'
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-NormalizedDriveLetter {
    param([string]$Drive)
    return ($Drive -replace ':$', '').ToUpper() + ":"
}

function Get-RemovableUsbDrives {
    <#
    .SYNOPSIS
        Detects removable USB drives suitable for Stone installation.
    #>
    $drives = @()
    
    # Get all USB disks
    $usbDisks = Get-Disk | Where-Object { 
        $_.BusType -eq 'USB' -and 
        $_.OperationalStatus -eq 'Online' -and
        $_.Size -gt ($script:Config.MinUsbSizeGB * 1GB)
    }
    
    foreach ($disk in $usbDisks) {
        # Get partitions and volumes for this disk
        $partitions = Get-Partition -DiskNumber $disk.DiskNumber -ErrorAction SilentlyContinue |
        Where-Object { $_.DriveLetter }
        
        foreach ($partition in $partitions) {
            $volume = Get-Volume -DriveLetter $partition.DriveLetter -ErrorAction SilentlyContinue
            if ($volume) {
                $sizeGB = [math]::Round($disk.Size / 1GB, 1)
                $drives += [PSCustomObject]@{
                    DriveLetter   = "$($partition.DriveLetter):"
                    Label         = if ($volume.FileSystemLabel) { $volume.FileSystemLabel } else { "(No Label)" }
                    SizeGB        = $sizeGB
                    DiskNumber    = $disk.DiskNumber
                    FriendlyName  = $disk.FriendlyName
                    IsRecommended = $sizeGB -ge $script:Config.RecommendedUsbGB
                }
            }
        }
        
        # Handle unpartitioned disks
        if (-not $partitions) {
            $sizeGB = [math]::Round($disk.Size / 1GB, 1)
            $drives += [PSCustomObject]@{
                DriveLetter   = $null
                Label         = "(Unpartitioned)"
                SizeGB        = $sizeGB
                DiskNumber    = $disk.DiskNumber
                FriendlyName  = $disk.FriendlyName
                IsRecommended = $sizeGB -ge $script:Config.RecommendedUsbGB
            }
        }
    }
    
    return $drives
}

function Select-UsbDrive {
    <#
    .SYNOPSIS
        Interactively selects a USB drive from detected drives.
    #>
    param([switch]$Force)
    
    Write-Host ""
    Write-Step "Detecting USB drives" "..."
    
    $drives = @(Get-RemovableUsbDrives)  # Force array
    
    if ($drives.Count -eq 0) {
        Write-Step "No suitable USB drives detected" "FAIL"
        Write-Host ""
        Write-Host "  Requirements:" -ForegroundColor Yellow
        Write-Host "    • USB removable drive (not fixed disk)" -ForegroundColor Gray
        Write-Host "    • Minimum $($script:Config.MinUsbSizeGB)GB capacity (recommended $($script:Config.RecommendedUsbGB)GB+)" -ForegroundColor Gray
        Write-Host ""
        Write-Host "  Please insert a USB drive and run again." -ForegroundColor Yellow
        return $null
    }
    
    Write-Step "Detected $($drives.Count) USB drive(s)" "OK"
    
    if ($drives.Count -eq 1) {
        $drive = $drives[0]
        $sizeInfo = if ($drive.IsRecommended) { "$($drive.SizeGB)GB [OK]" } else { "$($drive.SizeGB)GB (small)" }
        $driveId = if ($drive.DriveLetter) { $drive.DriveLetter } else { "Disk $($drive.DiskNumber)" }
        $lines = @(
            "Drive:   $driveId",
            "Label:   $($drive.Label)",
            "Size:    $sizeInfo",
            "Device:  $($drive.FriendlyName)",
            "",
            "WARNING: ALL DATA WILL BE ERASED!"
        )
        Write-Panel -Title "USB Drive Detected" -Lines $lines -InnerWidth $script:Config.BoxWidth -TitleColor 'Cyan' -BodyColor 'Cyan' -LineColors @{5 = 'Yellow' }
        Write-Step "Selected $driveId automatically" "OK"
        # Return hashtable with both DriveLetter and DiskNumber
        return @{ DriveLetter = $drive.DriveLetter; DiskNumber = $drive.DiskNumber }
    }
    
    # Multiple drives - show selection menu
    Write-Host ""
    Write-Host "  ┌────────────────────────────────────────────────────┐" -ForegroundColor Cyan
    Write-Host "  │ Multiple USB Drives Detected                       │" -ForegroundColor Cyan
    Write-Host "  └────────────────────────────────────────────────────┘" -ForegroundColor Cyan
    Write-Host ""
    
    for ($i = 0; $i -lt $drives.Count; $i++) {
        $drive = $drives[$i]
        $sizeInfo = if ($drive.IsRecommended) { "$($drive.SizeGB)GB [Recommended]" } else { "$($drive.SizeGB)GB" }
        $driveId = if ($drive.DriveLetter) { $drive.DriveLetter } else { "Disk$($drive.DiskNumber)" }
        
        Write-Host "  [$($i + 1)] $($driveId.PadRight(8)) $($sizeInfo.PadRight(25)) $($drive.Label)" -ForegroundColor White
    }
    
    Write-Host ""
    Write-Host "  WARNING: Selected drive will be COMPLETELY ERASED!" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  Select drive (1-$($drives.Count)) or [Q]uit: " -NoNewline -ForegroundColor White
    
    while ($true) {
        $userInput = Read-Host
        if ($userInput -eq 'q' -or $userInput -eq 'Q') {
            Write-Host "  Cancelled by user." -ForegroundColor Yellow
            return $null
        }
        
        $selection = 0
        if ([int]::TryParse($userInput, [ref]$selection) -and $selection -ge 1 -and $selection -le $drives.Count) {
            $selectedDrive = $drives[$selection - 1]
            return @{ DriveLetter = $selectedDrive.DriveLetter; DiskNumber = $selectedDrive.DiskNumber }
        }
        
        Write-Host "  Invalid selection. Enter 1-$($drives.Count) or Q: " -NoNewline -ForegroundColor Yellow
    }
}

function Get-LatestDebianVersion {
    Write-Step "Detecting latest Debian stable version..." "..."
    
    try {
        $baseUrl = $script:Config.DebianBaseUrl
        
        # Fetch the ISO directory listing
        $response = Invoke-WebRequest -Uri $baseUrl -UseBasicParsing -TimeoutSec 30
        
        # Parse HTML to find netinst ISO files (format: debian-XX.X.X-amd64-netinst.iso)
        $isoPattern = 'debian-(\d+\.\d+\.\d+)-amd64-netinst\.iso'
        $matches = [regex]::Matches($response.Content, $isoPattern)
        
        if ($matches.Count -eq 0) {
            throw "Could not find Debian netinst ISO in directory listing"
        }
        
        # Extract version numbers and sort to find latest
        $versions = $matches | ForEach-Object { 
            [PSCustomObject]@{
                Version  = $_.Groups[1].Value
                FileName = $_.Value
            }
        } | Sort-Object { [version]$_.Version } -Descending | Select-Object -First 1
        
        $version = $versions.Version
        $isoFileName = $versions.FileName
        $isoUrl = "$baseUrl/$isoFileName"
        
        Write-Step "Detected Debian $version" "OK"
        
        # Download SHA256SUMS file to get checksum
        Write-Step "Fetching SHA256 checksum..." "..."
        $sha256Url = "$baseUrl/SHA256SUMS"
        $sha256Response = Invoke-WebRequest -Uri $sha256Url -UseBasicParsing -TimeoutSec 15
        $sha256Content = [System.Text.Encoding]::UTF8.GetString($sha256Response.Content)
        
        # Parse SHA256SUMS file (format: "hash  filename" or "hash filename")
        # Use regex escape to handle dots in filename
        $sha256Lines = $sha256Content -split "[\r\n]+" | Where-Object { $_.Trim() -ne '' }
        $sha256Line = $sha256Lines | Where-Object { $_ -match [regex]::Escape($isoFileName) } | Select-Object -First 1
        if (-not $sha256Line) {
            throw "Could not find SHA256 checksum for $isoFileName in SHA256SUMS file"
        }
        
        # Extract hash (first field, whitespace-separated)
        $sha256Hash = ($sha256Line -split '\s+', 2)[0].Trim()
        if ($sha256Hash.Length -ne 64) {
            throw "Invalid SHA256 hash format (expected 64 chars, got $($sha256Hash.Length)): $sha256Hash"
        }
        Write-Step "SHA256: $($sha256Hash.Substring(0, 16))..." "OK"
        
        # Update global config
        $script:Config.DebianVersion = $version
        $script:Config.IsoUrl = $isoUrl
        $script:Config.IsoSha256 = $sha256Hash
        $script:Config.IsoCachePath = Join-Path $env:USERPROFILE ".koan\zen-garden\debian-$version-amd64-netinst.iso"
        
        return @{
            Version  = $version
            Url      = $isoUrl
            Sha256   = $sha256Hash
            FileName = $isoFileName
        }
    }
    catch {
        Write-Step "Failed to detect Debian version: $_" "FAIL"
        Write-Host "  Debug: Attempted to fetch from $baseUrl" -ForegroundColor Gray
        if ($sha256Content) {
            Write-Host "  SHA256SUMS entries found:" -ForegroundColor Gray
            ($sha256Lines | Select-Object -First 5) | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
        }
        throw "Unable to detect latest Debian version. Check internet connection or use manual configuration."
    }
}

function Get-DebianIsoCacheStatus {
    # Auto-detect version if not yet set
    if (-not $script:Config.DebianVersion) {
        Get-LatestDebianVersion | Out-Null
    }
    
    $cacheDir = $script:Config.CacheDir
    $isoPath = $script:Config.IsoCachePath
    
    $result = [ordered]@{
        Path          = $isoPath
        HasValidCache = $false
        Status        = "No cached ISO found"
    }
    
    if (-not (Test-Path $cacheDir)) {
        New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
        return $result
    }
    
    if (Test-Path $isoPath) {
        $result.HasValidCache = $true
        $result.Status = "Cached ISO found"
    }
    
    return $result
}

function Get-DebianIso {
    $cacheDir = $script:Config.CacheDir
    $cacheStatus = Get-DebianIsoCacheStatus
    $isoPath = $cacheStatus.Path
    Write-Step "Checking cache for Debian $($script:Config.DebianVersion)" "..."
    
    if (-not (Test-Path $cacheDir)) {
        New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
    }
    
    if ($cacheStatus.HasValidCache) {
        Write-Step "Debian ISO found in cache" "OK"
        return $isoPath
    }
    else {
        Write-Step $cacheStatus.Status "WARN"
        # Remove invalid cache if it exists
        if (Test-Path $isoPath) {
            Write-Step "Removing invalid cached ISO" "..."
            Remove-Item -LiteralPath $isoPath -Force -ErrorAction SilentlyContinue
        }
    }
    
    # Check disk space before downloading (~200 MB needed)
    $cacheDrive = Split-Path $cacheDir -Qualifier
    $freeSpace = (Get-PSDrive $cacheDrive.TrimEnd(':')).Free
    $requiredSpace = 500MB  # 180 MB ISO + margin
    if ($freeSpace -lt $requiredSpace) {
        $freeGB = [math]::Round($freeSpace / 1GB, 1)
        Write-Step "Insufficient disk space: $freeGB GB free, need 0.5 GB" "FAIL"
        throw "Not enough disk space to download ISO"
    }
    
    Write-Step "Downloading Debian $($script:Config.DebianVersion)..." "..."
    Write-Host "       This may take 2-5 minutes depending on your connection." -ForegroundColor Gray
    Write-Host "       URL: $($script:Config.IsoUrl)" -ForegroundColor Gray
    Write-Host ""
    
    try {
        # Ensure cache directory exists
        $cacheDir = Split-Path $isoPath -Parent
        if (-not (Test-Path $cacheDir)) {
            Write-Step "Creating cache directory..." "..."
            New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null
        }
        
        # Clean up partial downloads from previous failed attempts
        if (Test-Path $isoPath) {
            $existingSize = (Get-Item $isoPath).Length
            if ($existingSize -lt 100MB) {
                Write-Step "Removing incomplete ISO from previous attempt..." "WARN"
                Remove-Item $isoPath -Force
            }
        }
        
        # Use BITS for better download experience with progress
        $bitsJob = Start-BitsTransfer -Source $script:Config.IsoUrl -Destination $isoPath -Asynchronous -DisplayName "Debian ISO Download"
        
        $timeoutMinutes = 60
        $startTime = Get-Date
        while ($bitsJob.JobState -eq 'Transferring' -or $bitsJob.JobState -eq 'Connecting') {
            # Check for timeout
            if (((Get-Date) - $startTime).TotalMinutes -gt $timeoutMinutes) {
                Remove-BitsTransfer -BitsJob $bitsJob -ErrorAction SilentlyContinue
                throw "Download timed out after $timeoutMinutes minutes"
            }
            
            $percent = if ($bitsJob.BytesTotal -gt 0) { [math]::Round(($bitsJob.BytesTransferred / $bitsJob.BytesTotal) * 100, 1) } else { 0 }
            $mbTransferred = [math]::Round($bitsJob.BytesTransferred / 1MB, 1)
            $mbTotal = [math]::Round($bitsJob.BytesTotal / 1MB, 1)
            Write-Host "`r       Progress: $percent% ($mbTransferred MB / $mbTotal MB)        " -NoNewline
            Start-Sleep -Seconds 1
        }
        
        if ($bitsJob.JobState -eq 'Transferred') {
            Complete-BitsTransfer -BitsJob $bitsJob
            Write-Host ""
            
            # Verify downloaded file is reasonable size (Debian netinst is ~600MB)
            if (-not (Test-Path $isoPath)) {
                throw "ISO file not found after BITS transfer completed"
            }
            $downloadedSize = (Get-Item $isoPath).Length
            if ($downloadedSize -lt 400MB) {
                Remove-Item $isoPath -Force
                throw "Downloaded ISO is too small ($([math]::Round($downloadedSize/1MB))MB), likely corrupted"
            }
            
            # Verify SHA256 checksum
            Write-Step "Verifying ISO checksum..." "..."
            $actualHash = (Get-FileHash -Path $isoPath -Algorithm SHA256).Hash.ToLower()
            $expectedHash = $script:Config.IsoSha256.ToLower()
            if ($actualHash -ne $expectedHash) {
                Remove-Item -LiteralPath $isoPath -Force -ErrorAction SilentlyContinue
                throw "ISO checksum mismatch. Expected: $expectedHash, Got: $actualHash"
            }
            Write-Step "ISO checksum valid" "OK"
            
            Write-Step "Debian ISO downloaded" "OK"
            return $isoPath
        }
        else {
            # Clean up failed BITS job
            Remove-BitsTransfer -BitsJob $bitsJob -ErrorAction SilentlyContinue
            throw "Download failed: $($bitsJob.JobState)"
        }
    }
    catch {
        Write-Host ""
        Write-Step "BITS transfer failed, falling back to Invoke-WebRequest..." "WARN"
        
        # Clean up any partial file
        if (Test-Path $isoPath) {
            Remove-Item $isoPath -Force -ErrorAction SilentlyContinue
        }
        
        try {
            Invoke-WebRequest -Uri $script:Config.IsoUrl -OutFile $isoPath -UseBasicParsing -TimeoutSec 3600
            
            # Verify downloaded file
            if (-not (Test-Path $isoPath)) {
                throw "ISO file not found after download"
            }
            $downloadedSize = (Get-Item $isoPath).Length
            if ($downloadedSize -lt 100MB) {
                Remove-Item $isoPath -Force
                throw "Downloaded ISO is too small ($([math]::Round($downloadedSize/1MB))MB), likely corrupted"
            }
            
            Write-Step "Debian ISO downloaded" "OK"
            return $isoPath
        }
        catch {
            Write-Step "Failed to download Debian ISO: $_" "FAIL"
            # Clean up failed download
            if (Test-Path $isoPath) {
                Remove-Item $isoPath -Force -ErrorAction SilentlyContinue
            }
            throw
        }
    }
}

function Format-UsbDrive {
    param(
        [string]$DriveLetter,
        [int]$DiskNumber
    )
    
    $volumeName = "GARDEN-STONE"
    Write-Step "Formatting USB drive as FAT32..." "..."
    
    # Get disk number from drive letter if not provided
    if (-not $DiskNumber) {
        $driveLetter = $DriveLetter -replace ':', ''
        $partition = Get-Partition -DriveLetter $driveLetter -ErrorAction Stop
        $DiskNumber = $partition.DiskNumber
    }
    
    # Safety check - ensure not system disk
    $systemDisk = (Get-Partition | Where-Object { $_.DriveLetter -eq $env:SystemDrive[0] } | Select-Object -First 1).DiskNumber
    if ($DiskNumber -eq $systemDisk) {
        throw "SAFETY: Cannot format system disk"
    }
    
    # Ensure disk is online and writable
    $disk = Get-Disk -Number $DiskNumber -ErrorAction Stop
    if ($disk.IsOffline) { Set-Disk -Number $DiskNumber -IsOffline $false -ErrorAction Stop }
    if ($disk.IsReadOnly) { Set-Disk -Number $DiskNumber -IsReadOnly $false -ErrorAction Stop }

    Write-Step "Clearing disk $DiskNumber..." "..."

    # Clear disk (removes all partitions)
    if ($disk.PartitionStyle -ne 'RAW') {
        Clear-Disk -Number $DiskNumber -RemoveData -RemoveOEM -Confirm:$false -ErrorAction Stop
        $disk = Get-Disk -Number $DiskNumber -ErrorAction Stop
    }

    # Initialize as MBR if raw, otherwise reset style to MBR
    if ($disk.PartitionStyle -eq 'RAW') {
        Initialize-Disk -Number $DiskNumber -PartitionStyle MBR -ErrorAction Stop
    }
    else {
        Set-Disk -Number $DiskNumber -PartitionStyle MBR -ErrorAction Stop
    }
    
    # Create primary partition and try to auto-assign a drive letter up front
    $newPartition = New-Partition -DiskNumber $DiskNumber -UseMaximumSize -IsActive -AssignDriveLetter -ErrorAction Stop
    
    # Format as FAT32
    $vol = Format-Volume -Partition $newPartition -FileSystem FAT32 -NewFileSystemLabel $volumeName -Confirm:$false -ErrorAction Stop
    $newDriveLetter = $vol.DriveLetter
    
    # Assign drive letter if needed (robustly resolve partition then add path)
    if (-not $newDriveLetter) {
        $primaryPartition = Get-Partition -DiskNumber $DiskNumber | Sort-Object Offset | Select-Object -First 1
        $newDriveLetter = ($primaryPartition | Add-PartitionAccessPath -AssignDriveLetter -PassThru).DriveLetter
    }
    
    if (-not $newDriveLetter) {
        throw "Failed to assign drive letter after format"
    }
    
    Write-Step "USB formatted: ${newDriveLetter}:" "OK"
    
    # Return the new drive letter
    return "${newDriveLetter}:"
}

function Copy-IsoContents {
    param(
        [string]$IsoPath,
        [string]$UsbDrive
    )
    
    Write-Step "Mounting Debian ISO..." "..."
    $attempt = 0
    $mountResult = $null
    $isoDriveLetter = $null
    while ($true) {
        try {
            $mountResult = Mount-DiskImage -ImagePath $IsoPath -PassThru
            $isoDriveLetter = ($mountResult | Get-Volume).DriveLetter + ":"
            break
        }
        catch {
            if ($attempt -ge 1) {
                throw "Failed to mount Debian ISO after retry: $_"
            }
            Write-Step "Cached ISO unreadable, re-downloading..." "WARN"
            Remove-Item -LiteralPath $IsoPath -Force -ErrorAction SilentlyContinue
            $IsoPath = Get-DebianIso
            $attempt++
        }
    }
    
    try {
        Write-Step "ISO mounted at $isoDriveLetter" "OK"
        
        # Count files/bytes for display
        $totalInfo = Get-ChildItem -Path $isoDriveLetter -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum
        $totalFiles = if ($totalInfo) { $totalInfo.Count } else { 0 }
        $totalBytes = if ($totalInfo -and ($totalInfo | Get-Member -Name Sum)) { $totalInfo.Sum } else { 0 }
        $totalMB = if ($totalBytes -gt 0) { [math]::Round($totalBytes / 1MB, 1) } else { 0 }
        
        # Validate USB drive has enough space
        $usbDriveLetter = $UsbDrive -replace ':', ''
        $usbVolume = Get-Volume -DriveLetter $usbDriveLetter -ErrorAction Stop
        $freeSpace = $usbVolume.SizeRemaining
        $freeSpaceGB = [math]::Round($freeSpace / 1GB, 1)
        $requiredSpaceGB = [math]::Round($totalBytes / 1GB, 1) + 1  # Add 1GB buffer
        
        if ($freeSpace -lt ($totalBytes + 1GB)) {
            throw "Insufficient USB space: $freeSpaceGB GB free, need at least $requiredSpaceGB GB"
        }
        
        Write-Step "Copying $totalFiles files (~$totalMB MB) to USB..." "..."

        # Start robocopy in a background job so we can poll progress
        $sourcePath = "$isoDriveLetter\"
        $destPath = "$UsbDrive\"
        $robocopyArgs = @($sourcePath, $destPath, "/E", "/NJH", "/NJS", "/NDL", "/NC", "/NFL", "/R:1", "/W:1", "/NP")
        $job = Start-Job -ScriptBlock {
            param($rcArgs)
            $out = & robocopy @rcArgs
            [pscustomobject]@{ Code = $LASTEXITCODE; Output = $out }
        } -ArgumentList (, $robocopyArgs)

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        while ($job.State -eq 'Running') {
            $destInfo = Get-ChildItem -Path $UsbDrive -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum
            $copiedBytes = if ($destInfo -and ($destInfo | Get-Member -Name Sum)) { $destInfo.Sum } else { 0 }
            $copiedMB = if ($copiedBytes -gt 0) { [math]::Round($copiedBytes / 1MB, 1) } else { 0 }
            $percent = if ($totalBytes -gt 0) { [math]::Round([math]::Min(100, ($copiedBytes / $totalBytes) * 100), 1) } else { 0 }
            $eta = "--"
            if ($percent -gt 0 -and $percent -lt 100) {
                $elapsed = $sw.Elapsed.TotalSeconds
                $etaSeconds = $elapsed * ((100 - $percent) / $percent)
                $eta = "{0:N0}s" -f $etaSeconds
            }
            Write-Host "`r       Progress: $percent% ($copiedMB/$totalMB MB) ETA: $eta        " -NoNewline
            Start-Sleep -Milliseconds 750
        }
        $sw.Stop()
        Write-Host "`r       Progress: 100% complete                    "

        $jobResult = Receive-Job -Job $job
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
        
        # Robocopy exit codes: 0-7 are success/warnings, 8+ are errors
        $jobExitCode = if ($jobResult -and ($jobResult.PSObject.Properties.Name -contains 'Code')) { 
            $jobResult.Code 
        }
        elseif ($jobResult -is [int]) { 
            $jobResult 
        }
        else { 
            8  # Assume error if we can't parse result
        }
        
        if ($jobExitCode -gt $script:Config.RobocopySuccessMax) {
            $logTail = ""
            if ($jobResult -and ($jobResult.PSObject.Properties.Name -contains 'Output') -and $jobResult.Output) {
                $lines = @($jobResult.Output)
                if ($lines.Count -gt 0) {
                    $logTail = ($lines | Select-Object -Last 20) -join "`n"
                }
            }
            $msg = "Robocopy failed with exit code $jobExitCode"
            if ($logTail) { $msg += "`nLog tail:`n$logTail" }
            throw $msg
        }
        
        $filesCopied = (Get-ChildItem -Path $UsbDrive -Recurse -File -ErrorAction SilentlyContinue).Count
        Write-Step "ISO contents copied ($filesCopied files)" "OK"
        
        # Validate critical Debian boot files exist
        $criticalFiles = @(
            "boot\grub\grub.cfg",
            "install.amd\vmlinuz",
            "install.amd\initrd.gz"
        )
        $missingFiles = @()
        foreach ($file in $criticalFiles) {
            $fullPath = Join-Path $UsbDrive $file
            if (-not (Test-Path $fullPath)) {
                $missingFiles += $file
            }
        }
        if ($missingFiles.Count -gt 0) {
            throw "Critical boot files missing after copy: $($missingFiles -join ', ')"
        }
        Write-Step "Boot files validated" "OK"
    }
    finally {
        if ($mountResult) {
            Dismount-DiskImage -ImagePath $IsoPath -ErrorAction SilentlyContinue
        }
    }
}

function New-DebianConfig {
    param(
        [string]$UsbDrive,
        [hashtable]$StoneConfig
    )
    
    # For unattended Debian install we use a neutral hostname.
    $name = $script:Config.InstallHostname
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    
    # Load Debian preseed template
    $templatesDir = Join-Path $PSScriptRoot "templates"
    
    $debianPreseedPath = Join-Path $templatesDir "debian-preseed.template"
    $debianSetupPath = Join-Path $templatesDir "stone-setup-debian.sh.template"
    $mossServicePath = Join-Path $templatesDir "garden-moss.service.template"
    $mossConfigPath = Join-Path $templatesDir "garden-moss.toml.template"
    
    if (-not (Test-Path $debianPreseedPath)) {
        throw "Template not found: $debianPreseedPath"
    }
    if (-not (Test-Path $debianSetupPath)) {
        throw "Template not found: $debianSetupPath"
    }
    if (-not (Test-Path $mossServicePath)) {
        throw "Template not found: $mossServicePath"
    }
    if (-not (Test-Path $mossConfigPath)) {
        throw "Template not found: $mossConfigPath"
    }
    
    $debianPreseedTemplate = Get-Content $debianPreseedPath -Raw
    $debianSetupTemplate = Get-Content $debianSetupPath -Raw
    $mossServiceTemplate = Get-Content $mossServicePath -Raw
    $mossConfigTemplate = Get-Content $mossConfigPath -Raw
    
    # Moss will manage service offerings - no pre-install manifest needed
    $script:PreparedPreInstallManifest = $null
    
    # Process Debian preseed template
    Write-Step "Processing Debian preseed..." "..."
    
    $debianPreseed = $debianPreseedTemplate
    $debianPreseed = $debianPreseed.Replace('{{HOSTNAME}}', $name)
    $debianPreseed = $debianPreseed.Replace('{{USERNAME}}', $script:Config.DefaultUser)
    $debianPreseed = $debianPreseed.Replace('{{PASSWORD}}', $script:Config.DefaultUser)
    $debianPreseed = $debianPreseed.Replace('{{PASSWORDHASH}}', $script:Config.DefaultPasswordHash)
    $debianPreseed = $debianPreseed.Replace('{{TIMESTAMP}}', $timestamp)
    
    $debianSetup = $debianSetupTemplate
    $debianSetup = $debianSetup.Replace('{{USERNAME}}', $script:Config.DefaultUser)
    $debianSetup = $debianSetup.Replace('{{HOSTNAME}}', $name)
    $debianSetup = $debianSetup.Replace('{{TIMESTAMP}}', $timestamp)
    
    # Note: zen-garden-services.service removed - moss handles service installation via manifest
    
    $mossService = $mossServiceTemplate
    $mossService = $mossService.Replace('{{USERNAME}}', $script:Config.DefaultUser)
    
    $mossConfig = $mossConfigTemplate
    $mossConfig = $mossConfig.Replace('{{TIMESTAMP}}', $timestamp)
    
    # Store processed templates (removed PreparedSystemdService - no longer needed)
    $script:PreparedDebianPreseed = $debianPreseed
    $script:PreparedDebianSetupScript = $debianSetup
    $script:PreparedMossService = $mossService
    $script:PreparedMossConfig = $mossConfig
    
    Write-Step "Debian preseed processed" "OK"
}

# Debian preseed doesn't require overlay files like Alpine's apkovl
# All configuration is handled via preseed.cfg and late_command hooks

function Copy-BrandingAssets {
    param([string]$UsbDrive)
    
    $brandingPreparedDir = Join-Path $PSScriptRoot "branding\prepared"
    
    if (-not (Test-Path $brandingPreparedDir)) {
        Write-Step "Branding assets not found (optional)" "SKIP"
        return
    }
    
    $manifestPath = Join-Path $brandingPreparedDir "manifest.json"
    if (-not (Test-Path $manifestPath)) {
        Write-Step "Branding manifest missing; skipping branding" "SKIP"
        return
    }
    
    Write-Step "Applying branding overlay..." "..."
    
    # Check manifest age
    try {
        $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
        $preparedDate = [datetime]::Parse($manifest.prepared_at)
        $ageInDays = ([datetime]::UtcNow - $preparedDate).Days
        
        if ($ageInDays -gt 30) {
            Write-Step "Branding assets are $ageInDays days old; consider re-running Prepare-BrandingArtifacts.ps1" "WARN"
        }
    }
    catch {
        # Non-fatal - continue with branding application
    }
    
    # Copy ISOLINUX splash screen (BIOS boot)
    $isolinuxSplash = Join-Path $brandingPreparedDir "isolinux\zen-splash.txt"
    $isolinuxDest = Join-Path $UsbDrive "isolinux"
    if ((Test-Path $isolinuxSplash) -and (Test-Path $isolinuxDest)) {
        Copy-Item $isolinuxSplash $isolinuxDest -Force
        Write-Step "ISOLINUX splash screen applied" "OK"
    }
    
    # Copy first-boot branding to stone-root (MOTD, SSH banners, etc.)
    $stoneRootSource = Join-Path $brandingPreparedDir "stone-root"
    $stoneRootDest = Join-Path $UsbDrive "stone-root"
    if ((Test-Path $stoneRootSource) -and (Test-Path $stoneRootDest)) {
        # Recursively copy all first-boot assets
        Get-ChildItem $stoneRootSource -Recurse -File | ForEach-Object {
            $relativePath = $_.FullName.Substring($stoneRootSource.Length + 1)
            $destPath = Join-Path $stoneRootDest $relativePath
            $destDir = Split-Path $destPath -Parent
            
            if (-not (Test-Path $destDir)) {
                New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            }
            
            Copy-Item $_.FullName $destPath -Force
        }
        Write-Step "First-boot branding applied (MOTD, SSH banner)" "OK"
    }
    
    # Note: GRUB/ISOLINUX menu configs are applied by Update-GrubConfig/Update-IsolinuxConfig
    # Note: GTK installer branding requires initrd repacking (not yet implemented)
    
    Write-Step "Branding overlay complete" "OK"
}

function Write-StoneFiles {
    param([string]$UsbDrive)
    
    Write-Step "Writing stone setup files to USB..." "..."
    
    # Write Debian preseed file
    $preseedPath = Join-Path $UsbDrive "preseed.cfg"
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        $script:PreparedDebianPreseed | Out-File -FilePath $preseedPath -Encoding utf8NoBOM -NoNewline
    }
    else {
        # PowerShell 5.1 doesn't support utf8NoBOM, use UTF8 (with BOM) - GRUB handles this
        $script:PreparedDebianPreseed | Out-File -FilePath $preseedPath -Encoding utf8 -NoNewline
    }
    
    Write-Step "Preseed configuration written" "OK"
    
    # Write moss pre-install manifest if offerings were configured
    if ($script:PreparedPreInstallManifest) {
        Write-Step "Writing garden-moss-preinstall.json..." "..."
        $manifestPath = Join-Path $UsbDrive "garden-moss-preinstall.json"
        if ($PSVersionTable.PSVersion.Major -ge 6) {
            $script:PreparedPreInstallManifest | Out-File -FilePath $manifestPath -Encoding utf8NoBOM -NoNewline
        }
        else {
            $script:PreparedPreInstallManifest | Out-File -FilePath $manifestPath -Encoding utf8 -NoNewline
        }
        Write-Step "garden-moss-preinstall.json written (garden-moss will process on first boot)" "OK"
    }
    
    # Write setup script for reference (not strictly needed as preseed handles installation)
    $setupScriptPath = Join-Path $UsbDrive "stone-setup.sh"
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        $script:PreparedDebianSetupScript | Out-File -FilePath $setupScriptPath -Encoding utf8NoBOM -NoNewline
    }
    else {
        $script:PreparedDebianSetupScript | Out-File -FilePath $setupScriptPath -Encoding utf8 -NoNewline
    }
    
    # Prepare stone-root directory structure (mirrors target filesystem)
    Write-Step "Preparing stone-root filesystem..." "..."
    $stoneRoot = Join-Path $PSScriptRoot "stone-root"
    
    # Clear and recreate stone-root structure
    $stoneRootUsb = Join-Path $UsbDrive "stone-root"
    if (Test-Path $stoneRootUsb) {
        # Forcefully remove all contents first, then the directory
        Get-ChildItem -Path $stoneRootUsb -Recurse -Force -ErrorAction SilentlyContinue | 
            Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
        Remove-Item $stoneRootUsb -Force -Recurse -ErrorAction Stop
    }
    Copy-Item $stoneRoot $stoneRootUsb -Recurse -Force
    
    # Copy manifests to stone-root/etc/zen-garden/templates/
    $templatesDir = Join-Path $stoneRootUsb "etc\zen-garden\templates"
    if (-not (Test-Path $templatesDir)) {
        New-Item -ItemType Directory -Path $templatesDir -Force | Out-Null
    }
    
    $manifestsSource = $script:Config.ManifestsDir
    if (Test-Path $manifestsSource) {
        # Copy taxonomy dictionary (best-effort)
        $taxonomyDict = Join-Path $manifestsSource "taxonomy.dictionary.yaml"
        if (Test-Path $taxonomyDict) {
            Copy-Item $taxonomyDict (Join-Path $templatesDir "taxonomy.dictionary.yaml") -Force -ErrorAction SilentlyContinue
        }

        # Copy each category directory
        $categories = @("data", "messaging", "ai", "vector", "secrets", "observability", "cache")
        foreach ($category in $categories) {
            $categoryPath = Join-Path $manifestsSource $category
            if (Test-Path $categoryPath) {
                $categoryDest = Join-Path $templatesDir $category
                New-Item -ItemType Directory -Path $categoryDest -Force | Out-Null
                # Copy all offering artifacts needed by moss at runtime:
                # - *.snippet.yaml: service definitions
                # - *.compatibility.yaml: Pass/Fallback/Fail rules
                # - *.frontmatter.json: metadata (optional today, but cheap to ship)
                Copy-Item (Join-Path $categoryPath "*.snippet.yaml") $categoryDest -Force -ErrorAction SilentlyContinue
                Copy-Item (Join-Path $categoryPath "*.compatibility.yaml") $categoryDest -Force -ErrorAction SilentlyContinue
                Copy-Item (Join-Path $categoryPath "*.frontmatter.json") $categoryDest -Force -ErrorAction SilentlyContinue
            }
        }
        Write-Step "manifests → stone-root/etc/zen-garden/templates/" "OK"
    }
    else {
        Write-Step "manifests directory not found; runtime templates are required for offerings (list/info/install will be unavailable)" "WARN"
    }
    
    # Copy binaries to stone-root/usr/local/bin/
    $binDir = Join-Path $stoneRootUsb "usr\local\bin"
    if (Test-Path $script:Config.MossPath) {
        Copy-Item $script:Config.MossPath (Join-Path $binDir "garden-moss") -Force
        Write-Step "garden-moss binary → stone-root/usr/local/bin/" "OK"
    }
    else {
        Write-Step "garden-moss binary not found at $($script:Config.MossPath)" "FAIL"
        throw "garden-moss binary required. Run build-linux.ps1 first to create binaries in ../dist/linux/"
    }
    
    if (Test-Path $script:Config.GardenRakePath) {
        Copy-Item $script:Config.GardenRakePath (Join-Path $binDir "garden-rake") -Force
        Write-Step "garden-rake binary → stone-root/usr/local/bin/" "OK"
    }
    else {
        Write-Step "garden-rake binary not found at $($script:Config.GardenRakePath)" "FAIL"
        throw "garden-rake binary required. Run build-linux.ps1 first to create binaries in ../dist/linux/"
    }
    
    # Write garden-moss.service to stone-root/etc/systemd/system/
    $serviceDir = Join-Path $stoneRootUsb "etc\systemd\system"
    $mossServicePath = Join-Path $serviceDir "garden-moss.service"
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        $script:PreparedMossService | Out-File -FilePath $mossServicePath -Encoding utf8NoBOM -NoNewline
    }
    else {
        $script:PreparedMossService | Out-File -FilePath $mossServicePath -Encoding utf8 -NoNewline
    }
    Write-Step "garden-moss.service → stone-root/etc/systemd/system/" "OK"
    
    # Copy moss-update-helper.sh to stone-root/usr/local/bin/
    $helperScript = Join-Path $PSScriptRoot "moss-update-helper.sh"
    if (Test-Path $helperScript) {
        Copy-Item $helperScript (Join-Path $binDir "moss-update-helper.sh") -Force
        Write-Step "moss-update-helper.sh → stone-root/usr/local/bin/" "OK"
    }
    else {
        Write-Step "moss-update-helper.sh not found" "WARN"
    }
    
    # Copy sudoers configuration to stone-root/etc/sudoers.d/
    $sudoersFile = Join-Path $PSScriptRoot "sudoers.d-moss"
    if (Test-Path $sudoersFile) {
        $sudoersDir = Join-Path $stoneRootUsb "etc\sudoers.d"
        New-Item -ItemType Directory -Path $sudoersDir -Force | Out-Null
        Copy-Item $sudoersFile (Join-Path $sudoersDir "moss") -Force
        Write-Step "sudoers.d/moss → stone-root/etc/sudoers.d/" "OK"
    }
    else {
        Write-Step "sudoers.d-moss not found" "WARN"
    }
    
    # Create staging directory for binary updates
    $stagingDir = Join-Path $stoneRootUsb "home\stone\bin"
    New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
    Write-Step "Binary staging directory → stone-root/home/stone/bin/" "OK"
    
    # Write garden-moss.toml to stone-root/etc/zen-garden/
    $configDir = Join-Path $stoneRootUsb "etc\zen-garden"
    $mossConfigPath = Join-Path $configDir "garden-moss.toml"
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        $script:PreparedMossConfig | Out-File -FilePath $mossConfigPath -Encoding utf8NoBOM -NoNewline
    }
    else {
        $script:PreparedMossConfig | Out-File -FilePath $mossConfigPath -Encoding utf8 -NoNewline
    }
    Write-Step "garden-moss.toml → stone-root/etc/zen-garden/" "OK"
    
    # Copy quickstart guide to stone-root/home/stone/
    $homeDir = Join-Path $stoneRootUsb "home\stone"
    $quickstartSource = Join-Path $PSScriptRoot "templates\garden-rake-quickstart.sh"
    if (Test-Path $quickstartSource) {
        Copy-Item $quickstartSource (Join-Path $homeDir "garden-rake-quickstart.sh") -Force
        Write-Step "garden-rake-quickstart.sh → stone-root/home/stone/" "OK"
    }
    
    # Write garden-moss-preinstall.json to stone-root/home/stone/ if offerings specified
    if ($script:PreparedPreInstallManifest) {
        $manifestPath = Join-Path $homeDir "garden-moss-preinstall.json"
        if ($PSVersionTable.PSVersion.Major -ge 6) {
            $script:PreparedPreInstallManifest | Out-File -FilePath $manifestPath -Encoding utf8NoBOM
        }
        else {
            $script:PreparedPreInstallManifest | Out-File -FilePath $manifestPath -Encoding utf8
        }
        Write-Step "garden-moss-preinstall.json → stone-root/home/stone/" "OK"
    }
    
    Write-Step "stone-root filesystem ready" "OK"
}

function Update-GrubConfig {
    param([string]$UsbDrive)
    
    Write-Step "Configuring GRUB for Debian..." "..."

    $grubPaths = @()
    $primary = Join-Path $UsbDrive "boot\grub\grub.cfg"
    $efi = Join-Path $UsbDrive "EFI\BOOT\grub.cfg"
    $loopback = Join-Path $UsbDrive "boot\grub\loopback.cfg"
    if (Test-Path $primary) { $grubPaths += $primary }
    if (Test-Path $efi) { $grubPaths += $efi }
    if (Test-Path $loopback) { $grubPaths += $loopback }

    if (-not $grubPaths) {
        Write-Step "GRUB config not found (will use manual install)" "FAIL"
        throw "GRUB config not found in boot/grub, boot/grub/loopback.cfg, or EFI/BOOT"
    }

    foreach ($grubCfgPath in $grubPaths) {
        try {
            [System.IO.File]::SetAttributes($grubCfgPath, [System.IO.FileAttributes]::Normal)
        }
        catch {
            $errMsg = $_.Exception.Message
            Write-Step "Cannot clear read-only on $grubCfgPath - $errMsg" "FAIL"
            throw
        }

        $lines = Get-Content $grubCfgPath
        $patchedKernel = $false

        # Debian: Add preseed file path to kernel command line
        $lines = $lines | ForEach-Object {
            if ($_ -match '^\s*linux\s+.*(/install\.amd/vmlinuz|/vmlinuz)') {
                # Skip if already patched (contains our cdrom preseed parameter)
                if ($_ -match 'preseed/file=/cdrom/preseed\.cfg') {
                    # Already patched, skip
                    return $_
                }
                $patchedKernel = $true
                # Debian preseed automation (USB media):
                # auto - enables automated install mode
                # priority=critical - skip all non-critical questions  
                # preseed/file=/cdrom/preseed.cfg - preseed file on USB root (netinst mounts USB at /cdrom)
                # locale=en_US language=en country=US - bypass language selection
                # console-setup/ask_detect=false - skip keyboard detection
                # netcfg/choose_interface=auto - auto select network interface
                return "$_ auto priority=critical preseed/file=/cdrom/preseed.cfg locale=en_US language=en country=US console-setup/ask_detect=false keymap=us netcfg/choose_interface=auto"
            }
            return $_
        }

        if (-not $patchedKernel) {
            Write-Step "No linux entries patched in $grubCfgPath" "WARN"
        }

        # Set timeout to 1 second and hide menu
        $hasTimeout = $false
        $hasTimeoutStyle = $false
        $hasDefault = $false
        
        $lines = $lines | ForEach-Object { 
            if ($_ -match '^set timeout=') { 
                $hasTimeout = $true
                'set timeout=1' 
            }
            elseif ($_ -match '^set timeout_style=') {
                $hasTimeoutStyle = $true
                'set timeout_style=hidden'
            }
            elseif ($_ -match '^set default=') {
                $hasDefault = $true
                'set default=0'
            }
            else { 
                $_ 
            } 
        }
        
        # If timeout settings don't exist, add them at the start (after any initial comments/font setup)
        if (-not $hasTimeout -or -not $hasTimeoutStyle -or -not $hasDefault) {
            $insertIndex = 0
            # Find a good spot after initial setup (after font loading)
            for ($i = 0; $i -lt $lines.Count; $i++) {
                if ($lines[$i] -match 'loadfont.*then' -or $lines[$i] -match 'terminal_output') {
                    $insertIndex = $i + 1
                    break
                }
            }
            
            $newSettings = @()
            if (-not $hasTimeout) { $newSettings += 'set timeout=1' }
            if (-not $hasTimeoutStyle) { $newSettings += 'set timeout_style=hidden' }
            if (-not $hasDefault) { $newSettings += 'set default=0' }
            
            if ($insertIndex -gt 0 -and $newSettings.Count -gt 0) {
                $lines = $lines[0..($insertIndex - 1)] + $newSettings + $lines[$insertIndex..($lines.Count - 1)]
            }
        }

        $grubContent = ($lines -join "`n") + "`n"

        try {
            # Use utf8NoBOM to avoid BOM issues with GRUB
            if ($PSVersionTable.PSVersion.Major -ge 6) {
                $grubContent | Out-File -FilePath $grubCfgPath -Encoding utf8NoBOM -NoNewline
            }
            else {
                # PowerShell 5.1: Write as UTF8 with BOM, then remove BOM
                $grubContent | Out-File -FilePath $grubCfgPath -Encoding utf8 -NoNewline
                $bytes = [System.IO.File]::ReadAllBytes($grubCfgPath)
                if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
                    [System.IO.File]::WriteAllBytes($grubCfgPath, $bytes[3..($bytes.Length - 1)])
                }
            }
            
            # Validate file was written and contains preseed parameter
            if (-not (Test-Path $grubCfgPath)) {
                throw "GRUB config disappeared after writing"
            }
            $verifyContent = Get-Content $grubCfgPath -Raw
            if ($verifyContent -notmatch 'preseed/file=/cdrom/preseed\.cfg') {
                throw "GRUB config missing preseed file parameter after write"
            }
        }
        catch {
            $errMsg = $_.Exception.Message
            Write-Step "Cannot write GRUB config $grubCfgPath - $errMsg" "FAIL"
            throw
        }
        Write-Step "GRUB configured for Debian preseed at $grubCfgPath" "OK"
    }
}

function Update-IsolinuxConfig {
    param([string]$UsbDrive)
    
    Write-Step "Configuring ISOLINUX for Debian..." "..."

    $isolinuxDir = Join-Path $UsbDrive "isolinux"
    if (-not (Test-Path $isolinuxDir)) {
        Write-Step "ISOLINUX not found, skipping (UEFI-only boot)" "WARN"
        return
    }

    # Patch all .cfg files in isolinux directory that contain kernel/append lines
    $cfgFiles = Get-ChildItem -Path $isolinuxDir -Filter "*.cfg" -File
    $patchedAny = $false

    foreach ($cfgFile in $cfgFiles) {
        try {
            [System.IO.File]::SetAttributes($cfgFile.FullName, [System.IO.FileAttributes]::Normal)
        }
        catch {}

        $lines = Get-Content $cfgFile.FullName
        $modified = $false

        # Patch kernel append lines
        $lines = $lines | ForEach-Object {
            if ($_ -match '^\s*append\s+.*initrd=') {
                # Skip if already patched
                if ($_ -match 'preseed/file=/cdrom/preseed\.cfg') {
                    return $_
                }
                $modified = $true
                $patchedAny = $true
                # Add preseed parameters to append line
                return "$_ auto priority=critical preseed/file=/cdrom/preseed.cfg locale=en_US language=en country=US console-setup/ask_detect=false keymap=us netcfg/choose_interface=auto"
            }
            return $_
        }

        if ($modified) {
            $content = ($lines -join "`n") + "`n"
            if ($PSVersionTable.PSVersion.Major -ge 6) {
                $content | Out-File -FilePath $cfgFile.FullName -Encoding utf8NoBOM -NoNewline
            }
            else {
                $content | Out-File -FilePath $cfgFile.FullName -Encoding utf8 -NoNewline
                $bytes = [System.IO.File]::ReadAllBytes($cfgFile.FullName)
                if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
                    [System.IO.File]::WriteAllBytes($cfgFile.FullName, $bytes[3..($bytes.Length - 1)])
                }
            }
            Write-Step "Patched $($cfgFile.Name)" "OK"
        }
    }

    # Set timeout to 1 second for auto-boot
    $isolinuxCfg = Join-Path $isolinuxDir "isolinux.cfg"
    if (Test-Path $isolinuxCfg) {
        $lines = Get-Content $isolinuxCfg
        $hasSplash = Test-Path (Join-Path $isolinuxDir "zen-splash.txt")
        $hasDisplay = $false
        
        $lines = $lines | ForEach-Object {
            if ($_ -match '^timeout\s+') {
                'timeout 10'  # 10 = 1 second in ISOLINUX
            }
            elseif ($_ -match '^display\s+') {
                $hasDisplay = $true
                $_
            }
            else {
                $_
            }
        }
        
        # Add display directive if splash exists and not already present
        if ($hasSplash -and -not $hasDisplay) {
            $lines = @('display zen-splash.txt') + $lines
        }
        
        $content = ($lines -join "`n") + "`n"
        if ($PSVersionTable.PSVersion.Major -ge 6) {
            $content | Out-File -FilePath $isolinuxCfg -Encoding utf8NoBOM -NoNewline
        }
        else {
            $content | Out-File -FilePath $isolinuxCfg -Encoding utf8 -NoNewline
            $bytes = [System.IO.File]::ReadAllBytes($isolinuxCfg)
            if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
                [System.IO.File]::WriteAllBytes($isolinuxCfg, $bytes[3..($bytes.Length - 1)])
            }
        }
    }

    if ($patchedAny) {
        Write-Step "ISOLINUX configured for Debian preseed" "OK"
    }
    else {
        Write-Step "No ISOLINUX entries found to patch" "WARN"
    }
}

function Assert-UsbFiles {
    param(
        [string]$UsbDrive,
        [string]$GrubPath
    )
    $required = @(
        $GrubPath,
        (Join-Path $UsbDrive "preseed.cfg"),
        (Join-Path $UsbDrive "stone-setup.sh")
    )
    foreach ($path in $required) {
        if (-not (Test-Path $path)) {
            throw "Required file missing on USB: $path"
        }
    }
}

function Show-Completion {
    param([hashtable]$Config)
    Write-Host ""
    $lines = @(
        "A new stone joins the garden.",
        "",
        "Name: (assigned by moss on first boot)",
        "URL:  http://<stone-name>.local"
    )
    Write-Panel -Title "The Stone Awaits" -Lines $lines -InnerWidth $script:Config.BoxWidth -TitleColor 'Green' -BodyColor 'Green'

    Write-Host "  Next Steps:" -ForegroundColor Cyan
    Write-Host "  ────────────────────────────────────────────────────" -ForegroundColor DarkGray
    Write-Host "  1. Insert USB into target computer" -ForegroundColor White
    Write-Host "  2. Power on and boot from USB:" -ForegroundColor White
    Write-Host "       Dell: F12  |  HP: F9/Esc  |  Lenovo: F12" -ForegroundColor Gray
    Write-Host "  3. Wait 15-20 minutes for auto-install" -ForegroundColor White
    Write-Host "  4. Remove USB when prompted" -ForegroundColor White
    Write-Host "  5. Access dashboard at the URL above" -ForegroundColor White
    Write-Host ""
    Write-Host "  SSH Credentials (Lab Mode):" -ForegroundColor Yellow
    Write-Host "       User: $($script:Config.DefaultUser)  Password: $($script:Config.DefaultUser)" -ForegroundColor Gray
    Write-Host ""
}
#endregion

#region Main Script
function Main {
    Write-Banner
    
    # Check administrator
    if (-not (Test-Administrator)) {
        Write-Step "This script requires Administrator privileges" "FAIL"
        Write-Host "       Right-click PowerShell and select 'Run as Administrator'" -ForegroundColor Gray
        exit 1
    }
    Write-Step "Running as Administrator" "OK"
    Write-Host ""
    
    # Initialize wizard state
    $wizardState = @{
        UsbDrive      = "?"
        UsbDiskNumber = $null
        UsbDisplay    = "?"
        UsbSize       = 0
        StoneName     = "?"
        UpdateOnly    = $UpdateOnly
    }
    
    # Discover USB drive (no formatting yet; defer until user confirms creation)
    if ($UsbDrive) {
        $wizardState.UsbDrive = Get-NormalizedDriveLetter -Drive $UsbDrive
        $wizardState.UsbDiskNumber = $null  # Will resolve if needed later
        $wizardState.UsbDisplay = $wizardState.UsbDrive
        $volume = Get-Volume -DriveLetter ($wizardState.UsbDrive -replace ':', '') -ErrorAction SilentlyContinue
        if (-not $volume) {
            Write-Step "USB drive $($wizardState.UsbDrive) not found" "FAIL"
            exit 1
        }
        $wizardState.UsbSize = [math]::Round($volume.Size / 1GB, 1)
    }
    else {
        $usbInfo = Select-UsbDrive -Force:$Force
        if (-not $usbInfo) {
            exit 1
        }
        $wizardState.UsbDrive = if ($usbInfo.DriveLetter) { $usbInfo.DriveLetter } else { $null }
        $wizardState.UsbDiskNumber = $usbInfo.DiskNumber
        $wizardState.UsbDisplay = if ($wizardState.UsbDrive) { $wizardState.UsbDrive } else { "Disk $($usbInfo.DiskNumber) (unpartitioned)" }
        if ($wizardState.UsbDrive) {
            $volume = Get-Volume -DriveLetter ($wizardState.UsbDrive -replace ':', '') -ErrorAction SilentlyContinue
            if (-not $volume) {
                Write-Step "USB drive $($wizardState.UsbDrive) not found" "FAIL"
                exit 1
            }
            $wizardState.UsbSize = [math]::Round($volume.Size / 1GB, 1)
        }
        else {
            $disk = Get-Disk -Number $wizardState.UsbDiskNumber -ErrorAction Stop
            $wizardState.UsbSize = [math]::Round($disk.Size / 1GB, 1)
        }
    }
    
    if ($wizardState.UsbSize -lt $script:Config.MinUsbSizeGB) {
        Write-Step "USB drive too small: $($wizardState.UsbSize)GB (minimum: $($script:Config.MinUsbSizeGB)GB)" "FAIL"
        exit 1
    }
    
    # The installer does not assign a unique stone name.
    # Moss will pick a unique adjective-noun name on first boot and set the hostname.
    $wizardState.StoneName = "(assigned by moss on first boot)"
    Write-Step "Stone name will be assigned by moss on first boot" "OK"
    
    # Service offerings are now managed directly in Moss after stone creation
    
    $startCreation = $false
    if (-not $Force) {
        while ($true) {
            $isoCacheStatus = Get-DebianIsoCacheStatus
            Show-StatusBar -UsbDrive $wizardState.UsbDisplay -StoneName $wizardState.StoneName -StepLabel "Ready to start"
            
            $lines = @(
                "USB Drive: $($wizardState.UsbDisplay) ($($wizardState.UsbSize)GB)",
                "Stone Name: $($wizardState.StoneName)",
                "Mode: Lab (no authentication)",
                "URL: Will be assigned by Moss on first boot",
                "Debian ISO: $(if ($isoCacheStatus.HasValidCache) { "Cached ($($script:Config.DebianVersion))" } else { "Download required (~600MB netinst)" })",
                "Update Only: $(if ($wizardState.UpdateOnly) { 'Yes (fast mode)' } else { 'No (full creation)' })"
            )
            $lineColors = @{}
            if ($isoCacheStatus.HasValidCache) { $lineColors[4] = 'Green' } else { $lineColors[4] = 'Yellow' }
            if ($wizardState.UpdateOnly) { $lineColors[5] = 'Yellow' } else { $lineColors[5] = 'Cyan' }
            Write-Panel -Title "Configuration" -Lines $lines -InnerWidth $script:Config.BoxWidth -TitleColor 'Cyan' -BodyColor 'Cyan' -LineColors $lineColors
            
            # Warning panel
            $warningLines = if ($wizardState.UpdateOnly) {
                @(
                    "UPDATE MODE: Will only refresh Debian config files and GRUB",
                    "No format or ISO copy - much faster for testing"
                )
            }
            else {
                @(
                    "ALL DATA on $($wizardState.UsbDrive) WILL BE PERMANENTLY ERASED!",
                    "This action CANNOT be undone."
                )
            }
            $warningColor = if ($wizardState.UpdateOnly) { 'Yellow' } else { 'Red' }
            $warningBodyColor = if ($wizardState.UpdateOnly) { 'White' } else { 'Yellow' }
            Write-Panel -Title "WARNING" -Lines $warningLines -InnerWidth $script:Config.BoxWidth -TitleColor $warningColor -BodyColor $warningBodyColor
            
            Write-Host "  Options:" -ForegroundColor White
            if ($wizardState.UpdateOnly) {
                Write-Host "    [Enter] Update USB (fast - no erase)" -ForegroundColor Green
            }
            else {
                Write-Host "    [Enter] Start USB creation (WARNING: ERASES $($wizardState.UsbDrive))" -ForegroundColor Green
            }
            Write-Host "    [B]     Build Linux binaries (moss + rake)" -ForegroundColor Cyan
            Write-Host "    [U]     Toggle update-only mode (dev mode)" -ForegroundColor White
            Write-Host "    [D]     Change USB drive" -ForegroundColor White
            Write-Host "    [Q]     Quit" -ForegroundColor Yellow
            Write-Host ""
            Write-Host "  Choice: " -NoNewline -ForegroundColor Yellow
            $choice = $null
            try {
                $key = [Console]::ReadKey($true)
                $choice = if ($key.Key -eq [ConsoleKey]::Enter) { "" } else { $key.KeyChar }
                Write-Host ""
            }
            catch {
                Write-Host ""  # finish the prompt line
                $choice = Read-Host
            }
            
            $choiceString = if ($choice) { $choice.ToString().ToLower() } else { "" }
            switch ($choiceString) {
                "" {
                    $startCreation = $true
                }
                "b" {
                    # Build Linux binaries
                    Write-Host ""
                    Write-Step "Building Linux binaries..." "..."
                    $buildScript = Join-Path $PSScriptRoot "build-linux.ps1"
                    
                    if (Test-Path $buildScript) {
                        try {
                            & $buildScript -Release
                            Write-Host ""
                            Write-Step "✓ Binaries built successfully" "OK"
                            Write-Host "  Updated: ..\dist\linux\moss, ..\dist\linux\garden-rake" -ForegroundColor Gray
                            Write-Host ""
                            Write-Host "  Press Enter to continue..." -ForegroundColor Gray
                            [void][Console]::ReadLine()
                        }
                        catch {
                            Write-Step "Build failed: $_" "FAIL"
                            Write-Host ""
                            Write-Host "  Press Enter to continue..." -ForegroundColor Gray
                            [void][Console]::ReadLine()
                        }
                    }
                    else {
                        Write-Step "build-linux.ps1 not found at $buildScript" "FAIL"
                        Start-Sleep -Seconds 2
                    }
                }
                "q" {
                    Write-Host "  Cancelled by user" -ForegroundColor Yellow
                    return
                }
                "d" {
                    $usbInfo = Select-UsbDrive -Force:$Force
                    if ($usbInfo) {
                        $wizardState.UsbDrive = if ($usbInfo.DriveLetter) { $usbInfo.DriveLetter } else { $null }
                        $wizardState.UsbDiskNumber = $usbInfo.DiskNumber
                        $wizardState.UsbDisplay = if ($wizardState.UsbDrive) { $wizardState.UsbDrive } else { "Disk $($usbInfo.DiskNumber) (unpartitioned)" }

                        if ($wizardState.UsbDrive) {
                            $volume = Get-Volume -DriveLetter ($wizardState.UsbDrive -replace ':', '') -ErrorAction SilentlyContinue
                            if (-not $volume) {
                                Write-Step "USB drive $($wizardState.UsbDrive) not found" "FAIL"
                                Start-Sleep -Seconds 1
                                continue
                            }
                            $wizardState.UsbSize = [math]::Round($volume.Size / 1GB, 1)
                        }
                        else {
                            $disk = Get-Disk -Number $wizardState.UsbDiskNumber -ErrorAction Stop
                            $wizardState.UsbSize = [math]::Round($disk.Size / 1GB, 1)
                        }

                        if ($wizardState.UsbSize -lt $script:Config.MinUsbSizeGB) {
                            Write-Step "USB drive too small: $($wizardState.UsbSize) GB (minimum: $($script:Config.MinUsbSizeGB) GB)" "FAIL"
                            Start-Sleep -Seconds 1
                            continue
                        }

                        Write-Step "Using drive $($wizardState.UsbDisplay) ($($wizardState.UsbSize) GB)" "OK"
                    }
                }
                "u" {
                    $wizardState.UpdateOnly = -not $wizardState.UpdateOnly
                    $mode = if ($wizardState.UpdateOnly) { "enabled" } else { "disabled" }
                    Write-Step "Update-only mode $mode" "OK"
                    Start-Sleep -Milliseconds 500
                }
                default {
                    # ignore and redisplay
                }
            }
            if ($startCreation) { break }
        }
    }
    else {
        Write-Host "  Using non-interactive mode (Force)." -ForegroundColor Gray
        $startCreation = $true
    }
    
    # Begin creation
    Show-StatusBar -UsbDrive $wizardState.UsbDisplay -StoneName $wizardState.StoneName -StepLabel "Creating USB"
    Write-Step "Starting USB creation" "..."

    $formattedAlready = $false

    # Ensure we have a usable drive letter before writing files
    if ($wizardState.UpdateOnly) {
        if (-not $wizardState.UsbDrive) {
            throw "Update-only mode requires an existing partitioned USB drive with a drive letter"
        }
    }
    else {
        if (-not $wizardState.UsbDrive) {
            Write-Step "Formatting selected disk..." "..."
            $wizardState.UsbDrive = Format-UsbDrive -DiskNumber $wizardState.UsbDiskNumber
            $wizardState.UsbDisplay = $wizardState.UsbDrive
            $volume = Get-Volume -DriveLetter ($wizardState.UsbDrive -replace ':', '') -ErrorAction Stop
            $wizardState.UsbSize = [math]::Round($volume.Size / 1GB, 1)
            $formattedAlready = $true
        }
    }
    
    # Create and validate Debian configuration (fail fast before ISO download)
    Write-Step "Preparing Debian configuration..." "..."
    $stoneConfig = @{ Name = $script:Config.InstallHostname }
    New-DebianConfig -UsbDrive $wizardState.UsbDrive -StoneConfig $stoneConfig
    Write-Step "Debian templates validated" "OK"
    
    if ($wizardState.UpdateOnly) {
        Write-Host ""
        Write-Host "  ⚡ UPDATE-ONLY MODE: Skipping format and ISO copy" -ForegroundColor Yellow
        Write-Host "     Only updating Debian config files and GRUB" -ForegroundColor Gray
        Write-Host ""
    }
    else {
        # Download/cache Debian ISO
        $isoPath = Get-DebianIso
        
        # Format USB drive
        if (-not $formattedAlready) {
            Format-UsbDrive -DriveLetter $wizardState.UsbDrive
        }
        
        # Copy ISO contents
        Copy-IsoContents -IsoPath $isoPath -UsbDrive $wizardState.UsbDrive
    }
    
    # Write stone setup files to USB
    Write-StoneFiles -UsbDrive $wizardState.UsbDrive
    
    # Apply branding overlay (boot menus, GTK theme, first-boot assets)
    Copy-BrandingAssets -UsbDrive $wizardState.UsbDrive
    
    # Update GRUB for Debian autoinstall (UEFI boot)
    Update-GrubConfig -UsbDrive $wizardState.UsbDrive
    
    # Update ISOLINUX for Debian autoinstall (BIOS boot)
    Update-IsolinuxConfig -UsbDrive $wizardState.UsbDrive

    # Validate required files
    $grubCfgPath = if (Test-Path (Join-Path $wizardState.UsbDrive "boot\grub\grub.cfg")) {
        Join-Path $wizardState.UsbDrive "boot\grub\grub.cfg"
    }
    elseif (Test-Path (Join-Path $wizardState.UsbDrive "EFI\BOOT\grub.cfg")) {
        Join-Path $wizardState.UsbDrive "EFI\BOOT\grub.cfg"
    }
    else {
        throw "GRUB config not found after update"
    }
    Assert-UsbFiles -UsbDrive $wizardState.UsbDrive -GrubPath $grubCfgPath
    
    # Final validation: verify all critical files still exist and are readable
    Write-Step "Performing final validation..." "..."
    $finalChecks = @(
        @{ Path = (Join-Path $wizardState.UsbDrive "preseed.cfg"); Name = "Debian preseed file" }
        @{ Path = (Join-Path $wizardState.UsbDrive "stone-setup.sh"); Name = "Stone setup script" }
        @{ Path = (Join-Path $wizardState.UsbDrive "install.amd\vmlinuz"); Name = "Kernel" }
        @{ Path = (Join-Path $wizardState.UsbDrive "install.amd\initrd.gz"); Name = "Initial ramdisk" }
        @{ Path = $grubCfgPath; Name = "GRUB config" }
    )
    
    $missingOrUnreadable = @()
    foreach ($check in $finalChecks) {
        if (-not (Test-Path $check.Path)) {
            $missingOrUnreadable += "$($check.Name) (missing)"
        }
        else {
            try {
                $content = Get-Content $check.Path -Raw -ErrorAction Stop
                if ([string]::IsNullOrWhiteSpace($content)) {
                    $missingOrUnreadable += "$($check.Name) (empty)"
                }
            }
            catch {
                $missingOrUnreadable += "$($check.Name) (unreadable)"
            }
        }
    }
    
    if ($missingOrUnreadable.Count -gt 0) {
        throw "Final validation failed. Issues: $($missingOrUnreadable -join ', ')"
    }
    Write-Step "All files validated" "OK"
    
    # Done!
    Show-Completion -Config $stoneConfig
    Write-Host "  Press Enter to exit..." -ForegroundColor Gray
    [void][Console]::ReadLine()
    exit 0
}

# Run main
try {
    Main
}
catch {
    Write-Host ""
    Write-Step "Error: $_" "FAIL"
    Write-Host "       $($_.ScriptStackTrace)" -ForegroundColor DarkGray
    Write-Host "  Press Enter to exit..." -ForegroundColor Gray
    [void][Console]::ReadLine()
    exit 1
}
#endregion
