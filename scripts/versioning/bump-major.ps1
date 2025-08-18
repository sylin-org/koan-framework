param([int]$Major)
$ErrorActionPreference = 'Stop'
if (-not $Major -and $Major -ne 0) { Write-Error 'Provide -Major <int>'; exit 1 }

$path = Join-Path $PSScriptRoot '../../version.json'
$json = Get-Content -Raw $path | ConvertFrom-Json
$parts = $json.version.Split('.')
$parts[0] = "$Major"
$parts[1] = '0'
$parts[2] = '0'
$json.version = ($parts -join '.')
$json | ConvertTo-Json -Depth 10 | Out-File -Encoding UTF8 $path
Write-Host "Bumped version to $($json.version). Commit and tag v$($json.version) to release."
