#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Test script to discover Zen Garden Stones and query offerings.
    
.DESCRIPTION
    This script:
    1. Sends UDP discovery request to multicast (239.255.42.99:7184)
    2. Collects Stone responses
    3. Queries Moss API for offerings
    4. Tests health endpoint
    
.PARAMETER Timeout
    Discovery timeout in seconds (default: 5)
    
.PARAMETER Offering
    Offering to query (e.g., 'mongodb', 'mongo', 'redis')
    
.EXAMPLE
    .\zen-garden-discover.ps1
    Discover all Stones and list their offerings
    
.EXAMPLE
    .\zen-garden-discover.ps1 -Offering mongodb
    Discover Stones and query the 'mongodb' offering
#>

param(
    [int]$Timeout = 5,
    [string]$Offering = ""
)

$ErrorActionPreference = "Stop"

function Write-Status {
    param([string]$Message, [string]$Type = "Info")
    $color = switch ($Type) {
        "Success" { "Green" }
        "Warning" { "Yellow" }
        "Error" { "Red" }
        default { "Cyan" }
    }
    Write-Host $Message -ForegroundColor $color
}

function Get-LanBindAddress {
    # Find best LAN IP for UDP binding
    $interfaces = [System.Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() |
        Where-Object { $_.OperationalStatus -eq 'Up' -and $_.NetworkInterfaceType -ne 'Loopback' }
    
    foreach ($iface in $interfaces) {
        $props = $iface.GetIPProperties()
        foreach ($addr in $props.UnicastAddresses) {
            if ($addr.Address.AddressFamily -eq 'InterNetwork') {
                $ip = $addr.Address.ToString()
                if ($ip -match '^(192\.168\.|10\.|172\.(1[6-9]|2[0-9]|3[01])\.)') {
                    return $ip
                }
            }
        }
    }
    return $null
}

function Discover-Stones {
    param([int]$TimeoutSeconds)
    
    Write-Status "🔍 Discovering Stones on network (timeout: ${TimeoutSeconds}s)..."
    
    $lanIP = Get-LanBindAddress
    
    if ($lanIP) {
        Write-Status "   Binding to LAN interface: $lanIP"
        $localEndpoint = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Parse($lanIP), 7184)
        $udpClient = New-Object System.Net.Sockets.UdpClient $localEndpoint
    } else {
        Write-Status "   No LAN interface found, using default binding" -Type "Warning"
        $udpClient = New-Object System.Net.Sockets.UdpClient 7184
    }
    
    $udpClient.EnableBroadcast = $true
    
    # Discovery request wrapped in UdpAnnouncement envelope
    $requestId = [guid]::NewGuid().ToString()
    $announcement = @{
        type = "discovery_request"
        data = @{
            discover = "moss"
            request_id = $requestId
            requester = "koan-test-script"
        }
    } | ConvertTo-Json -Compress
    
    Write-Status "   Request envelope: $announcement"
    
    $requestBytes = [System.Text.Encoding]::UTF8.GetBytes($announcement)
    
    # Send to multicast group
    $multicastGroup = [System.Net.IPAddress]::Parse("239.255.42.99")
    $multicastEndpoint = New-Object System.Net.IPEndPoint($multicastGroup, 7184)
    $broadcastEndpoint = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Broadcast, 7184)
    
    $sent1 = $udpClient.Send($requestBytes, $requestBytes.Length, $multicastEndpoint)
    $sent2 = $udpClient.Send($requestBytes, $requestBytes.Length, $broadcastEndpoint)
    
    Write-Status "   Sent: multicast $sent1 bytes + broadcast $sent2 bytes"
    
    # Collect responses
    [System.Collections.ArrayList]$stones = @()
    $seenStones = @{}
    $remoteEP = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, 0)
    
    $udpClient.Client.ReceiveTimeout = 1000
    
    $startTime = Get-Date
    while (((Get-Date) - $startTime).TotalSeconds -lt $TimeoutSeconds) {
        try {
            $responseBytes = $udpClient.Receive([ref]$remoteEP)
            $responseJson = [System.Text.Encoding]::UTF8.GetString($responseBytes)
            
            Write-Status "`n   📨 Raw response from $($remoteEP.Address):" -Type "Info"
            Write-Host $responseJson | ConvertFrom-Json | ConvertTo-Json -Depth 10
            
            $envelope = $responseJson | ConvertFrom-Json
            
            if ($envelope.type -eq "discovery_response") {
                $response = $envelope.data
                
                if ($seenStones.ContainsKey($response.stone_name)) {
                    continue
                }
                $seenStones[$response.stone_name] = $true
                
                $stones.Add([PSCustomObject]@{
                    Name = $response.stone_name
                    Endpoint = $response.stone_endpoint
                    MossVersion = $response.moss_version
                    LanternEndpoint = $response.lantern_endpoint
                    Address = $remoteEP.Address.ToString()
                    RawData = $response
                }) | Out-Null
                
                Write-Status "   ✓ Found: $($response.stone_name) at $($response.stone_endpoint)" -Type "Success"
            }
        }
        catch [System.Net.Sockets.SocketException] {
            continue
        }
        catch {
            Write-Status "   Warning: Parse error from $($remoteEP.Address): $_" -Type "Warning"
        }
    }
    
    $udpClient.Close()
    
    Write-Status "`n✅ Discovery complete: Found $($stones.Count) stone(s)" -Type "Success"
    return $stones
}

function Test-StoneHealth {
    param([string]$Endpoint)
    
    $healthUrl = "$Endpoint/health"
    Write-Status "`n🏥 Testing health: $healthUrl"
    
    try {
        $response = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 5
        Write-Status "   Response:" -Type "Success"
        $response | ConvertTo-Json -Depth 5 | Write-Host
        return $true
    }
    catch {
        Write-Status "   Health check failed: $_" -Type "Error"
        return $false
    }
}

function Get-Offering {
    param([string]$Endpoint, [string]$OfferingName)
    
    $url = "$Endpoint/api/v1/offerings/$OfferingName"
    Write-Status "`n🔎 Querying offering: $url"
    
    try {
        $response = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 10
        Write-Status "   Response:" -Type "Success"
        $response | ConvertTo-Json -Depth 10 | Write-Host
        return $response
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -eq 404) {
            Write-Status "   Offering '$OfferingName' not found (404)" -Type "Warning"
        } else {
            Write-Status "   Request failed: $_" -Type "Error"
        }
        return $null
    }
}

function Search-Offerings {
    param([string]$Endpoint, [string]$Query = "")
    
    $url = if ($Query) { 
        "$Endpoint/api/v1/offerings/search?q=$Query" 
    } else { 
        "$Endpoint/api/v1/offerings" 
    }
    Write-Status "`n📋 Listing offerings: $url"
    
    try {
        $response = Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 10
        Write-Status "   Response:" -Type "Success"
        $response | ConvertTo-Json -Depth 10 | Write-Host
        return $response
    }
    catch {
        Write-Status "   Request failed: $_" -Type "Error"
        return $null
    }
}

# Main execution
Write-Host "`n╔════════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "║           Zen Garden Discovery Test Script                 ║" -ForegroundColor Magenta  
Write-Host "╚════════════════════════════════════════════════════════════╝" -ForegroundColor Magenta

# Step 1: Discover Stones
$stones = Discover-Stones -TimeoutSeconds $Timeout

if ($stones.Count -eq 0) {
    Write-Status "`n❌ No Stones found on network" -Type "Error"
    exit 1
}

# Step 2: For each Stone, test health and query offerings
foreach ($stone in $stones) {
    Write-Host "`n" + "═" * 60 -ForegroundColor DarkGray
    Write-Status "📦 Stone: $($stone.Name)" -Type "Info"
    Write-Status "   Endpoint: $($stone.Endpoint)"
    Write-Status "   Moss Version: $($stone.MossVersion)"
    
    # Resolve 127.0.0.1 to actual address
    $endpoint = $stone.Endpoint
    if ($endpoint -match "127\.0\.0\.1") {
        $endpoint = $endpoint -replace "127\.0\.0\.1", $stone.Address
        Write-Status "   Resolved to: $endpoint" -Type "Warning"
    }
    
    # Test health
    Test-StoneHealth -Endpoint $endpoint
    
    # List all offerings
    Search-Offerings -Endpoint $endpoint
    
    # If specific offering requested, query it
    if ($Offering) {
        Get-Offering -Endpoint $endpoint -OfferingName $Offering
    }
    
    # Also test both 'mongo' and 'mongodb' to check aliasing
    Write-Status "`n🔬 Testing offering aliases..."
    Get-Offering -Endpoint $endpoint -OfferingName "mongo"
    Get-Offering -Endpoint $endpoint -OfferingName "mongodb"
}

Write-Host "`n" + "═" * 60 -ForegroundColor DarkGray
Write-Status "✅ Test complete!" -Type "Success"
