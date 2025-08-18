param()
# Emits a list of packable csproj files under src/**
Get-ChildItem -Recurse -Path "$PSScriptRoot/../../src" -Filter *.csproj |
  Where-Object {
    $xml = [xml](Get-Content -Raw $_.FullName)
    $packable = $xml.Project.PropertyGroup.Pack -or $xml.Project.PropertyGroup.IsPackable
    if ($packable -eq $null -or [string]::IsNullOrWhiteSpace($packable)) { $packable = 'true' }
    [string]::Equals($packable, 'true', 'OrdinalIgnoreCase')
  } |
  ForEach-Object { $_.FullName }
