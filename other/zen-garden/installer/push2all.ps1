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
    [string]$BinaryPath = "$PSScriptRoot/../dist/linux/garden-moss",
    [int]$Timeout = 3,
    [switch]$Parallel,
    [int]$Port = 0,  # Override port (0 = use discovered port)
    [switch]$SkipBuild  # Skip automatic build (for testing)
)

$ErrorActionPreference = "Stop"

# Always build release binaries unless explicitly skipped
if (-not $SkipBuild) {
    Write-Host "🔨 Building release binaries..." -ForegroundColor Cyan
    Write-Host ""
    
    $buildScript = Join-Path $PSScriptRoot "build-linux.ps1"
    & $buildScript -Release
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Build failed" -ForegroundColor Red
        exit 1
    }
    
    Write-Host ""
}

# Convert to absolute path
$BinaryPath = Resolve-Path $BinaryPath -ErrorAction Stop

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

function Test-BinaryFile {
    param([string]$Path)
    
    Write-Status "📦 Validating binary file..."
    
    if (-not (Test-Path $Path)) {
        throw "Binary file not found: $Path"
    }
    
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $sizeMb = [math]::Round($bytes.Length / 1MB, 2)
    
    # Check ELF header
    if ($bytes.Length -lt 4 -or $bytes[0] -ne 0x7f -or $bytes[1] -ne 0x45 -or $bytes[2] -ne 0x4c -or $bytes[3] -ne 0x46) {
        throw "Not a valid ELF binary (expected Linux executable)"
    }
    
    Write-Status "   ✓ Path: $Path" -Type "Success"
    Write-Status "   ✓ Size: $sizeMb MB" -Type "Success"
    Write-Status "   ✓ Format: ELF" -Type "Success"
    
    return $bytes
}

function Push-MossToStone {
    param(
        [PSCustomObject]$Stone,
        [byte[]]$BinaryData
    )
    
    Write-Status "`n🚀 Pushing moss to $($Stone.Name)..."
    
    # Encode to base64
    Write-Status "   Encoding binary..."
    $base64 = [Convert]::ToBase64String($BinaryData)
    
    # Prepare payload
    $payload = @{
        component = "garden-moss"
        binary_data = $base64
    } | ConvertTo-Json
    
    # Send upgrade request
    Write-Status "   Uploading to $($Stone.Endpoint)..."
    $url = "$($Stone.Endpoint.TrimEnd('/'))/api/v1/stone/upgrade"
    
    try {
        $response = Invoke-RestMethod -Uri $url -Method Post -Body $payload -ContentType "application/json" -TimeoutSec 30
        
        Write-Status "   ✅ Upload successful" -Type "Success"
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
            Write-Status "`n   ⚠️  $($Stone.Name) did not respond after restart" -Type "Warning"
            Write-Status "      Check status: ssh stone@$($Stone.Address) 'sudo systemctl status garden-moss.service'" -Type "Warning"
        }
        
        return $online
        
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
    Write-Status "  Push Moss Binary to All Stones"
    Write-Status "═══════════════════════════════════════════════════════════════`n"
    
    # Validate binary
    $binaryData = Test-BinaryFile -Path $BinaryPath
    
    # Discover stones
    $stones = Discover-AllStones -TimeoutSeconds $Timeout
    
    if ($stones.Count -eq 0) {
        Write-Status "`n⚠️  No stones discovered on the network" -Type "Warning"
        Write-Status "   Make sure stones are running and reachable" -Type "Warning"
        exit 1
    }
    
    # Push to all stones
    Write-Status "`n📡 Pushing moss binary to $($stones.Count) stone(s)..."
    
    $results = @()
    
    if ($Parallel) {
        Write-Status "   Mode: Parallel deployment`n"
        
        $jobs = @()
        foreach ($stone in $stones) {
            $jobs += Start-Job -ScriptBlock {
                param($StoneName, $StoneEndpoint, $BinaryData)
                
                $base64 = [Convert]::ToBase64String($BinaryData)
                $payload = @{
                    component = "garden-moss"
                    binary_data = $base64
                } | ConvertTo-Json
                
                $url = "$($StoneEndpoint.TrimEnd('/'))/api/system/refresh"
                
                try {
                    $response = Invoke-RestMethod -Uri $url -Method Post -Body $payload -ContentType "application/json" -TimeoutSec 30
                    
                    # Wait and check health
                    Start-Sleep -Seconds 4
                    $healthUrl = "$($StoneEndpoint.TrimEnd('/'))/health"
                    
                    for ($i = 1; $i -le 10; $i++) {
                        Start-Sleep -Seconds 1
                        try {
                            $health = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 2
                            if ($health.status) {
                                return @{ Success = $true; Name = $StoneName }
                            }
                        }
                        catch {}
                    }
                    
                    return @{ Success = $false; Name = $StoneName; Error = "Did not come back online" }
                }
                catch {
                    return @{ Success = $false; Name = $StoneName; Error = $_.Exception.Message }
                }
            } -ArgumentList $stone.Name, $stone.Endpoint, $binaryData
        }
        
        # Wait for all jobs
        $jobs | Wait-Job | Out-Null
        $results = $jobs | Receive-Job
        $jobs | Remove-Job
        
    }
    else {
        Write-Status "   Mode: Sequential deployment`n"
        
        foreach ($stone in $stones) {
            $success = Push-MossToStone -Stone $stone -BinaryData $binaryData
            $results += @{
                Success = $success
                Name = $stone.Name
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
