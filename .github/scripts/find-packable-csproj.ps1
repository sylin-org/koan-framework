param()
# Emits a list of packable csproj files under src/**
# Defaults to true for SDK-style class libraries; Web SDK must opt-in via Pack/IsPackable=true
$roots = @((Resolve-Path "$PSScriptRoot/../../src").Path)
Get-ChildItem -Recurse -Path $roots -Filter *.csproj |
  Where-Object {
    $xml = [xml](Get-Content -Raw $_.FullName)
    $sdk = [string]$xml.Project.Sdk
    $isWebSdk = $sdk -like '*Microsoft.NET.Sdk.Web*'
    # Collect explicit Pack/IsPackable values across all PropertyGroups (may be multiple)
    $vals = @()
    foreach ($pg in @($xml.Project.PropertyGroup)) {
      if ($null -ne $pg.Pack) { $vals += [string]$pg.Pack }
      if ($null -ne $pg.IsPackable) { $vals += [string]$pg.IsPackable }
    }
    $joined = ($vals -join ' ').Trim()

    if ($isWebSdk) {
      # Only include web projects if they explicitly opt in
      return ($joined -match '(?i)\btrue\b')
    }

    # For non-web projects, default include when not specified
    if ([string]::IsNullOrWhiteSpace($joined)) { return $true }
    return ($joined -match '(?i)\btrue\b')
  } |
  ForEach-Object { $_.FullName }
