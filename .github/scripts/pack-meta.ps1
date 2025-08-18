param(
  [Parameter(Mandatory=$true)][string]$Version,
  [string]$OutDir = "artifacts"
)

$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

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

nuget pack ./packaging/Sora.nuspec -Version $Version -OutputDirectory $OutDir -Properties "depVersionRange=$depRange"
nuget pack ./packaging/Sora.App.nuspec -Version $Version -OutputDirectory $OutDir -Properties "depVersionRange=$depRange"
