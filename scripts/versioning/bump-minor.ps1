param([int]$Minor)
$ErrorActionPreference = 'Stop'
if (-not $Minor -and $Minor -ne 0) { Write-Error 'Provide -Minor <int>'; exit 1 }

$path = Join-Path $PSScriptRoot '../../version.json'
$json = Get-Content -Raw $path | ConvertFrom-Json
$parts = $json.version.Split('.')
$parts[1] = "$Minor"
$parts[2] = '0'
$json.version = ($parts -join '.')
$json | ConvertTo-Json -Depth 10 | Out-File -Encoding UTF8 $path
Write-Host "Bumped version to $($json.version). Commit and tag v$($json.version) to release."
