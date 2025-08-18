param()
# Emits a list of packable csproj files under src/** (defaults to true for class library SDK projects)
$roots = @((Resolve-Path "$PSScriptRoot/../../src").Path)
Get-ChildItem -Recurse -Path $roots -Filter *.csproj |
  Where-Object {
    $xml = [xml](Get-Content -Raw $_.FullName)
    $sdk = $xml.Project.Sdk
    $isWebSdk = $sdk -like '*Microsoft.NET.Sdk.Web*'
    if ($isWebSdk) {
      # Only include explicit packable web projects (services) if they opt-in
      $pack = ($xml.Project.PropertyGroup.Pack + ' ' + $xml.Project.PropertyGroup.IsPackable) -join ' '
      return ($pack -match '(?i)true')
    }
    $packable = $xml.Project.PropertyGroup.Pack -or $xml.Project.PropertyGroup.IsPackable
    if ($packable -eq $null -or [string]::IsNullOrWhiteSpace($packable)) { return $true }
    [string]::Equals("$packable", 'true', 'OrdinalIgnoreCase')
  } |
  ForEach-Object { $_.FullName }
