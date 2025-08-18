param([int]$Patch)
$ErrorActionPreference = 'Stop'
if (-not $Patch -and $Patch -ne 0) { Write-Error 'Provide -Patch <int>'; exit 1 }

$path = Join-Path $PSScriptRoot '../../version.json'
$json = Get-Content -Raw $path | ConvertFrom-Json
$parts = $json.version.Split('.')
$parts[2] = "$Patch"
$json.version = ($parts -join '.')
$json | ConvertTo-Json -Depth 10 | Out-File -Encoding UTF8 $path
Write-Host "Bumped version to $($json.version). Commit and tag v$($json.version) to release."
