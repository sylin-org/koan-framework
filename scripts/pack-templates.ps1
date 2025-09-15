param(
  [string]$Version = "0.1.0-preview",
  [switch]$Install
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $root "..")
$nuspec = Join-Path $repo "templates/pack/Sylin.Koan.Templates.nuspec"
$artifacts = Join-Path $repo "artifacts"
$templatesRoot = Join-Path $repo "templates"

if (-not (Test-Path $nuspec)) { throw "nuspec not found: $nuspec" }
if (-not (Test-Path $artifacts)) { New-Item -ItemType Directory -Path $artifacts | Out-Null }

# Update version in nuspec (simple replace)
$xml = [xml](Get-Content $nuspec -Raw)
$xml.package.metadata.version = $Version
$xml.Save($nuspec)

# Try to pack with nuget.exe if available; otherwise fall back to folder install when -Install is specified
$nuget = Get-Command nuget -ErrorAction SilentlyContinue
if ($nuget) {
  Write-Host "Packing templates version $Version..."
  nuget pack $nuspec -OutputDirectory $artifacts | Write-Host

  # Find produced nupkg
  $nupkg = Get-ChildItem $artifacts -Filter "Sylin.Koan.Templates.*.nupkg" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $nupkg) { throw "No nupkg produced in $artifacts" }
  Write-Host "Created: $($nupkg.FullName)"

  if ($Install) {
    Write-Host "Installing templates from: $($nupkg.FullName)"
    dotnet new uninstall Sylin.Koan.Templates | Out-Null
    dotnet new install $nupkg.FullName
  }
}
else {
  Write-Warning "nuget.exe not found on PATH. Skipping packing."
  if ($Install) {
    Write-Host "Installing templates directly from folder: $templatesRoot"
    dotnet new uninstall Sylin.Koan.Templates | Out-Null
    dotnet new install $templatesRoot
  }
  else {
    Write-Host "Run this script with -Install to install from the templates folder, or install nuget.exe to enable packing."
  }
}
