param(
  [Parameter(Mandatory=$true)][string]$Version,
  [string]$OutDir = "artifacts"
)

$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path

# Build a dependency range like [MAJOR.MINOR.0,MAJOR.(MINOR+1).0)
function Get-DepRange([string]$v) {
  if (-not ($v -match '^(\d+)\.(\d+)\.(\d+)')) { return "[$v,$v)" }
  $maj = [int]$Matches[1]
  $min = [int]$Matches[2]
  $nextMin = $min + 1
  return "[$maj.$min.0,$maj.$nextMin.0)"
}

$depRange = Get-DepRange $Version

Write-Host "Packing meta packages with version=$Version and depRange=$depRange"

# Ensure icon asset exists at repo root (prefer existing icon.png; fallback to resources/image/0_2.jpg)
$iconPng = Join-Path $repoRoot 'icon.png'
if (-not (Test-Path $iconPng)) {
  $fallbackJpg = Join-Path $repoRoot 'resources/image/0_2.jpg'
  if (Test-Path $fallbackJpg) {
    Copy-Item -Path $fallbackJpg -Destination $iconPng -Force
  }
}

# Pack with BasePath at repo root so nuspec file entries resolve correctly
nuget pack ./packaging/Koan.nuspec -Version $Version -OutputDirectory $OutDir -BasePath $repoRoot -Properties "depVersionRange=$depRange"
nuget pack ./packaging/Koan.App.nuspec -Version $Version -OutputDirectory $OutDir -BasePath $repoRoot -Properties "depVersionRange=$depRange"
