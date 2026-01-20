#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Push a new moss binary to all discovered stones on the network.

.DESCRIPTION
    This script:
    1. Discovers all stones via UDP broadcast (port 7184)
    2. Uploads the moss binary to each stone via POST /api/system/refresh
    3. Waits for each stone to restart and come back online
    4. Reports success/failure for each stone

.PARAMETER BinaryPath
    Path to the moss binary to deploy (default: ../dist/linux/garden-moss)

.PARAMETER Timeout
    Discovery timeout in seconds (default: 3)

.PARAMETER Parallel
    Push to stones in parallel instead of sequentially

.PARAMETER Port
    Override the port number (default: 0 = use discovered port, typically 7185)

.EXAMPLE
    .\push-moss-to-all-stones.ps1
    
.EXAMPLE
    .\push-moss-to-all-stones.ps1 -BinaryPath ./target/release/garden-moss -Parallel

.EXAMPLE
    .\push-moss-to-all-stones.ps1 -Port 7185
#>

param(
    [int]$Timeout = 3,
    [switch]$Parallel,
    [int]$Port = 0,  # Override port (0 = use discovered port)
    [switch]$SkipBuild  # Skip automatic build (for testing)
)

$ErrorActionPreference = "Stop"

# Always build release binaries unless explicitly skipped
if (-not $SkipBuild) {
    Write-Host "🔨 Building release binaries for all platforms..." -ForegroundColor Cyan
    Write-Host ""
    
    $buildScript = Join-Path $PSScriptRoot "dist.ps1"
    & $buildScript
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Build failed" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
}

# Define binary paths
$distRoot = Resolve-Path "$PSScriptRoot/../dist"
$linuxMoss = Join-Path $distRoot "linux/garden-moss"
$linuxRake = Join-Path $distRoot "linux/garden-rake"
$windowsMoss = Join-Path $distRoot "windows/garden-moss.exe"
$windowsRake = Join-Path $distRoot "windows/garden-rake.exe"

# Validate binaries exist
if (-not (Test-Path $linuxMoss)) { throw "Linux moss binary not found: $linuxMoss" }
if (-not (Test-Path $linuxRake)) { throw "Linux rake binary not found: $linuxRake" }
if (-not (Test-Path $windowsMoss)) { throw "Windows moss binary not found: $windowsMoss" }
if (-not (Test-Path $windowsRake)) { throw "Windows rake binary not found: $windowsRake" }

function Write-Status {
    param([string]$Message, [string]$Type = "Info")
    $color = switch ($Type) {
        "Success" { "Green" }
        "Error" { "Red" }
        "Warning" { "Yellow" }
        default { "White" }
    }
    Write-Host $Message -ForegroundColor $color
}

function Discover-AllStones {
    param([int]$TimeoutSeconds)
    
    Write-Status "🔍 Discovering stones on network (timeout: ${TimeoutSeconds}s)..."
    
    # Create UDP socket
    $udpClient = New-Object System.Net.Sockets.UdpClient
    $udpClient.EnableBroadcast = $true
    $udpClient.Client.ReceiveTimeout = $TimeoutSeconds * 1000
    
    # Prepare discovery request
    $requestId = [guid]::NewGuid().ToString()
    $request = @{
        discover = "moss"
        request_id = $requestId
        requester = "push-moss-script"
    } | ConvertTo-Json -Compress
    
    $requestBytes = [System.Text.Encoding]::UTF8.GetBytes($request)
    
    # Send broadcast
    $broadcastEndpoint = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Broadcast, 7184)
    $sent = $udpClient.Send($requestBytes, $requestBytes.Length, $broadcastEndpoint)
    Write-Status "   Sent broadcast: $sent bytes to 255.255.255.255:7184"
    
    # Collect responses
    [System.Collections.ArrayList]$stones = @()
    $remoteEP = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, 0)
    
    $startTime = Get-Date
    while (((Get-Date) - $startTime).TotalSeconds -lt $TimeoutSeconds) {
        try {
            $responseBytes = $udpClient.Receive([ref]$remoteEP)
            $responseJson = [System.Text.Encoding]::UTF8.GetString($responseBytes)
            $response = $responseJson | ConvertFrom-Json
            
            # Check if this response matches our request (some responses might not have request_id)
            $endpoint = $response.stone_endpoint
            
            # Override port if specified
            if ($Port -gt 0) {
                $endpoint = $endpoint -replace ':\d+$', ":$Port"
            }
            
            $stones.Add([PSCustomObject]@{
                Name = $response.stone_name
                Endpoint = $endpoint
                Address = $remoteEP.Address.ToString()
            }) | Out-Null
            Write-Status "   ✓ Found: $($response.stone_name) at $endpoint" -Type "Success"
        }
        catch [System.Net.Sockets.SocketException] {
            # Timeout or no more responses
            break
        }
        catch {
            Write-Status "   Warning: Failed to parse response from $($remoteEP.Address): $_" -Type "Warning"
        }
    }
    
    $udpClient.Close()
    
    Write-Status "   Discovery complete: Found $($stones.Count) stone(s)" -Type "Success"
    return $stones
}

function Get-StoneInfo {
    param([PSCustomObject]$Stone)
    
    try {
        $url = "$($Stone.Endpoint.TrimEnd('/'))/health"
        $health = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 3
        
        return [PSCustomObject]@{
            OS = if ($health.os) { $health.os } else { "linux" }  # Default to linux for older versions
            Architecture = if ($health.architecture) { $health.architecture } else { "x86_64" }
        }
    }
    catch {
        Write-Status "   Warning: Could not query $($Stone.Name) health endpoint, assuming Linux x86_64" -Type "Warning"
        return [PSCustomObject]@{
            OS = "linux"
            Architecture = "x86_64"
        }
    }
}

function Get-BinariesForPlatform {
    param([string]$OS, [string]$Architecture)
    
    if ($OS -match "windows") {
        return [PSCustomObject]@{
            Moss = $windowsMoss
            Rake = $windowsRake
            Platform = "Windows"
        }
    }
    else {
        return [PSCustomObject]@{
            Moss = $linuxMoss
            Rake = $linuxRake
            Platform = "Linux"
        }
    }
}

function Test-BinaryFile {
    param([string]$Path, [bool]$AllowPE = $false)
    
    Write-Status "📦 Validating binary file: $(Split-Path -Leaf $Path)..."
    
    if (-not (Test-Path $Path)) {
        throw "Binary file not found: $Path"
    }
    
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $sizeMb = [math]::Round($bytes.Length / 1MB, 2)
    
    # Check binary format
    $format = "Unknown"
    if ($bytes.Length -ge 4) {
        # ELF header: 0x7f 'E' 'L' 'F'
        if ($bytes[0] -eq 0x7f -and $bytes[1] -eq 0x45 -and $bytes[2] -eq 0x4c -and $bytes[3] -eq 0x46) {
            $format = "ELF (Linux)"
        }
        # PE header: 'M' 'Z' (DOS stub)
        elseif ($bytes[0] -eq 0x4d -and $bytes[1] -eq 0x5a) {
            $format = "PE (Windows)"
            if (-not $AllowPE) {
                throw "Windows PE binary not expected for this platform"
            }
        }
        else {
            throw "Not a valid executable binary"
        }
    }
    else {
        throw "File too small to be a valid binary"
    }
    
    Write-Status "   ✓ Size: $sizeMb MB" -Type "Success"
    Write-Status "   ✓ Format: $format" -Type "Success"
    
    return $bytes
}

function Push-BinariesToStone {
    param(
        [PSCustomObject]$Stone,
        [byte[]]$MossBinaryData,
        [byte[]]$RakeBinaryData,
        [string]$Platform
    )
    
    Write-Status "`n🚀 Pushing binaries to $($Stone.Name) ($Platform)..."
    
    # Push moss first
    Write-Status "   [1/2] Uploading garden-moss..."
    $mossBase64 = [Convert]::ToBase64String($MossBinaryData)
    $mossPayload = @{
        component = "garden-moss"
        binary_data = $mossBase64
    } | ConvertTo-Json -Depth 10 -Compress
    
    $url = "$($Stone.Endpoint.TrimEnd('/'))/api/v1/stone/upgrade"
    
    try {
        $response = Invoke-RestMethod -Uri $url -Method Post -Body $mossPayload -ContentType "application/json; charset=utf-8" -TimeoutSec 30
        
        Write-Status "   ✅ Moss upload successful" -Type "Success"
        if ($response.architecture) {
            Write-Status "      Architecture: $($response.architecture)"
        }
        
        # Wait for moss to restart
        Write-Status "   ⏳ Waiting for moss to restart..."
        Start-Sleep -Seconds 3
        
        # Poll health endpoint
        $healthUrl = "$($Stone.Endpoint.TrimEnd('/'))/health"
        $maxAttempts = 10
        $online = $false
        
        for ($i = 1; $i -le $maxAttempts; $i++) {
            Start-Sleep -Seconds 1
            try {
                $health = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 2
                if ($health.status) {
                    Write-Status "   ✅ $($Stone.Name) is back online" -Type "Success"
                    $online = $true
                    break
                }
            }
            catch {
                Write-Host "." -NoNewline
            }
        }
        
        if (-not $online) {
            Write-Status "   ⚠️  $($Stone.Name) moss did not respond after restart" -Type "Warning"
            Write-Status "      Skipping rake push" -Type "Warning"
            return $false
        }
        
        # Push rake
        Write-Status "   [2/2] Uploading garden-rake..."
        $rakeBase64 = [Convert]::ToBase64String($RakeBinaryData)
        $rakePayload = @{
            component = "garden-rake"
            binary_data = $rakeBase64
        } | ConvertTo-Json -Depth 10 -Compress
        
        try {
            $rakeResponse = Invoke-RestMethod -Uri $url -Method Post -Body $rakePayload -ContentType "application/json; charset=utf-8" -TimeoutSec 30
            Write-Status "   ✅ Rake upload successful" -Type "Success"
            Write-Status "   ✅ $($Stone.Name) fully updated" -Type "Success"
            return $true
        }
        catch {
            Write-Status "   ⚠️  Rake push failed but moss is updated" -Type "Warning"
            Write-Status "      Error: $_" -Type "Warning"
            return $true  # Still count as success since moss updated
        }
        
    }
    catch {
        Write-Status "   ✗ Failed to push to $($Stone.Name)" -Type "Error"
        Write-Status "      Error: $_" -Type "Error"
        return $false
    }
}

# Main execution
try {
    Write-Status "`n═══════════════════════════════════════════════════════════════"
    Write-Status "  Push Garden Binaries to All Stones"
    Write-Status "═══════════════════════════════════════════════════════════════`n"
    
    # Discover stones
    $stones = Discover-AllStones -TimeoutSeconds $Timeout
    
    if ($stones.Count -eq 0) {
        Write-Status "`n⚠️  No stones discovered on the network" -Type "Warning"
        Write-Status "   Make sure stones are running and reachable" -Type "Warning"
        exit 1
    }
    
    # Detect platform for each stone and prepare binaries
    Write-Status "`n🔍 Detecting platform for each stone..."
    $stoneConfigs = @()
    foreach ($stone in $stones) {
        Write-Status "   $($stone.Name): " -NoNewline
        $info = Get-StoneInfo -Stone $stone
        $binaries = Get-BinariesForPlatform -OS $info.OS -Architecture $info.Architecture
        Write-Status "$($binaries.Platform) $($info.Architecture)" -Type "Success"
        
        $stoneConfigs += [PSCustomObject]@{
            Stone = $stone
            Platform = $binaries.Platform
            MossPath = $binaries.Moss
            RakePath = $binaries.Rake
        }
    }
    
    # Validate all binaries
    Write-Status "`n📦 Validating binaries..."
    $mossLinuxData = $null
    $rakeLinuxData = $null
    $mossWindowsData = $null
    $rakeWindowsData = $null
    
    $needsLinux = @($stoneConfigs | Where-Object { $_.Platform -eq "Linux" }).Count -gt 0
    $needsWindows = @($stoneConfigs | Where-Object { $_.Platform -eq "Windows" }).Count -gt 0
    
    if ($needsLinux) {
        $mossLinuxData = Test-BinaryFile -Path $linuxMoss -AllowPE $false
        $rakeLinuxData = Test-BinaryFile -Path $linuxRake -AllowPE $false
    }
    
    if ($needsWindows) {
        $mossWindowsData = Test-BinaryFile -Path $windowsMoss -AllowPE $true
        $rakeWindowsData = Test-BinaryFile -Path $windowsRake -AllowPE $true
    }
    
    # Push to all stones
    Write-Status "`n📡 Pushing binaries to $($stones.Count) stone(s)..."
    
    $results = @()
    
    if ($Parallel) {
        Write-Status "   Mode: Parallel deployment`n"
        
        $jobs = @()
        foreach ($config in $stoneConfigs) {
            $mossBinary = if ($config.Platform -eq "Windows") { $mossWindowsData } else { $mossLinuxData }
            $rakeBinary = if ($config.Platform -eq "Windows") { $rakeWindowsData } else { $rakeLinuxData }
            
            $jobs += Start-Job -ScriptBlock {
                param($StoneName, $StoneEndpoint, $MossBinary, $RakeBinary, $Platform)
                
                $mossBase64 = [Convert]::ToBase64String($MossBinary)
                $mossPayload = @{
                    component = "garden-moss"
                    binary_data = $mossBase64
                } | ConvertTo-Json -Depth 10 -Compress
                
                $url = "$($StoneEndpoint.TrimEnd('/'))/api/v1/stone/upgrade"
                
                try {
                    # Push moss
                    $response = Invoke-RestMethod -Uri $url -Method Post -Body $mossPayload -ContentType "application/json; charset=utf-8" -TimeoutSec 30
                    
                    # Wait and check health
                    Start-Sleep -Seconds 4
                    $healthUrl = "$($StoneEndpoint.TrimEnd('/'))/health"
                    
                    $online = $false
                    for ($i = 1; $i -le 10; $i++) {
                        Start-Sleep -Seconds 1
                        try {
                            $health = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 2
                            if ($health.status) {
                                $online = $true
                                break
                            }
                        }
                        catch {}
                    }
                    
                    if (-not $online) {
                        return @{ Success = $false; Name = $StoneName; Error = "Moss did not come back online" }
                    }
                    
                    # Push rake
                    $rakeBase64 = [Convert]::ToBase64String($RakeBinary)
                    $rakePayload = @{
                        component = "garden-rake"
                        binary_data = $rakeBase64
                    } | ConvertTo-Json -Depth 10 -Compress
                    
                    try {
                        Invoke-RestMethod -Uri $url -Method Post -Body $rakePayload -ContentType "application/json; charset=utf-8" -TimeoutSec 30 | Out-Null
                        return @{ Success = $true; Name = $StoneName; Platform = $Platform }
                    }
                    catch {
                        # Moss updated, rake failed - still success
                        return @{ Success = $true; Name = $StoneName; Platform = $Platform; Warning = "Rake push failed" }
                    }
                }
                catch {
                    return @{ Success = $false; Name = $StoneName; Error = $_.Exception.Message }
                }
            } -ArgumentList $config.Stone.Name, $config.Stone.Endpoint, $mossBinary, $rakeBinary, $config.Platform
        }
        
        # Wait for all jobs
        $jobs | Wait-Job | Out-Null
        $results = $jobs | Receive-Job
        $jobs | Remove-Job
        
    }
    else {
        Write-Status "   Mode: Sequential deployment`n"
        
        foreach ($config in $stoneConfigs) {
            $mossBinary = if ($config.Platform -eq "Windows") { $mossWindowsData } else { $mossLinuxData }
            $rakeBinary = if ($config.Platform -eq "Windows") { $rakeWindowsData } else { $rakeLinuxData }
            
            $success = Push-BinariesToStone -Stone $config.Stone -MossBinaryData $mossBinary -RakeBinaryData $rakeBinary -Platform $config.Platform
            $results += @{
                Success = $success
                Name = $config.Stone.Name
                Platform = $config.Platform
            }
        }
    }
    
    # Summary
    Write-Status "`n═══════════════════════════════════════════════════════════════"
    Write-Status "  Deployment Summary"
    Write-Status "═══════════════════════════════════════════════════════════════`n"
    
    $totalStones = if ($stones -is [System.Collections.ArrayList]) { $stones.Count } else { @($stones).Count }
    $successful = @($results | Where-Object { $_.Success }).Count
    $failed = @($results | Where-Object { -not $_.Success }).Count
    
    Write-Status "   Total stones: $totalStones"
    Write-Status "   Successful: $successful" -Type "Success"
    if ($failed -gt 0) {
        Write-Status "   Failed: $failed" -Type "Error"
    }
    
    if ($failed -gt 0) {
        Write-Status "`nFailed stones:"
        foreach ($result in ($results | Where-Object { -not $_.Success })) {
            Write-Status "   ✗ $($result.Name)" -Type "Error"
            if ($result.PSObject.Properties['Error'] -and $result.Error) {
                Write-Status "      $($result.Error)" -Type "Error"
            }
        }
    }
    
    Write-Status ""
    
    if ($failed -eq 0) {
        Write-Status "✅ All stones updated successfully!" -Type "Success"
        exit 0
    }
    else {
        Write-Status "⚠️  Some stones failed to update" -Type "Warning"
        exit 1
    }
}
catch {
    Write-Status "`n✗ Script failed: $_" -Type "Error"
    Write-Status $_.ScriptStackTrace -Type "Error"
    exit 1
}
