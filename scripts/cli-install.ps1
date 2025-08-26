param(
  [string]$Source = '',
  [string]$Dest = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$distBin = if ($Dest) { $Dest } else { Join-Path $repoRoot 'dist/bin' }
New-Item -ItemType Directory -Force -Path $distBin | Out-Null

if (-not $Source -or $Source.Trim() -eq '') {
    $Source = Join-Path $repoRoot 'artifacts/cli'
}

Write-Host "[cli-install] Source: $Source"
Write-Host "[cli-install] Dest:   $distBin"

if (Test-Path $Source) {
  Copy-Item -Path (Join-Path $Source '*') -Destination $distBin -Recurse -Force
  Write-Host "[cli-install] Files copied"
} else {
  Write-Host "[cli-install] Source not found: $Source (skipping copy)"
}

# Ensure dist/bin is on PATH for current user
$currentPath = [System.Environment]::GetEnvironmentVariable('Path', 'User')
if (-not $currentPath) { $currentPath = '' }
$distBinResolved = (Resolve-Path $distBin).Path
$pathParts = $currentPath -split ';' | Where-Object { $_ -and $_.Trim() -ne '' }
$already = $pathParts | Where-Object { $_.Trim().TrimEnd('\') -ieq $distBinResolved.TrimEnd('\') }
if (-not $already) {
    $newPath = ($pathParts + $distBinResolved) -join ';'
    [System.Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
    Write-Host "[cli-install] Added to PATH for current user: $distBinResolved"
    Write-Host "[cli-install] You may need to open a new terminal for PATH changes to take effect."
} else {
    Write-Host "[cli-install] PATH already contains: $distBinResolved"
}

