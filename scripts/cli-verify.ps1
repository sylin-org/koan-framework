param(
  [string]$ExeName = 'Sora'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$distBin = Join-Path $repoRoot 'dist/bin'

function Find-Cli {
  param([string]$name)
  $candidate = Get-ChildItem -Path $distBin -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.BaseName -like "$name*" -and ($_.Extension -in '.exe','.cmd','') }
  if ($candidate) { return $candidate[0].FullName }
  $which = (Get-Command $name -ErrorAction SilentlyContinue)
  if ($which) { return $which.Source }
  return $null
}

$cli = Find-Cli -name $ExeName
if (-not $cli) { throw "CLI executable not found (looked for $ExeName in dist/bin and PATH)." }

Write-Host "[cli-verify] Using: $cli"
& $cli --help | Select-Object -First 40 | ForEach-Object { $_ }
Write-Host "[cli-verify] OK"
