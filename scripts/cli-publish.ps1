param(
  [ValidateSet('win-x64','linux-x64','osx-x64','osx-arm64')]
  [string]$Runtime = 'win-x64',
  [ValidateSet('Debug','Release')]
  [string]$Configuration = 'Release',
  [string]$Project = 'src/Koan.Orchestration.Cli/Koan.Orchestration.Cli.csproj',
  [string]$Output = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Output -or $Output.Trim() -eq '') { $Output = Join-Path $repoRoot 'dist/bin' }

# Resolve absolute output path
if ([System.IO.Path]::IsPathRooted($Output)) {
  $outPath = [System.IO.Path]::GetFullPath($Output)
} else {
  $outPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Output))
}

New-Item -ItemType Directory -Force -Path $outPath | Out-Null

# Clean target directory (guarded to repo-root)
if ($outPath.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
  Write-Host "[cli-publish] Clean: $outPath"
  Get-ChildItem -Path $outPath -Force -ErrorAction SilentlyContinue | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
} else {
  Write-Warning "[cli-publish] Skipping clean: output path outside repo ($outPath)"
}

Push-Location $repoRoot
try {
  Write-Host "[cli-publish] $Project -> $outPath ($Runtime/$Configuration)"
  dotnet publish $Project -c $Configuration -r $Runtime --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=false -o $outPath --nologo --verbosity:minimal
  # Create friendly name 'Koan' beside the published artifact
  $published = Get-ChildItem -Path $outPath -File | Where-Object { $_.BaseName -eq 'Koan.Orchestration.Cli' -or $_.Name -eq 'Koan.Orchestration.Cli' } | Select-Object -First 1
  if (-not $published) {
    # fallback: best-effort search for cli exe in folder
  $published = Get-ChildItem -Path $outPath -File | Where-Object { $_.Name -like 'Koan.Orchestration.Cli*' -or $_.Name -like 'Koan.Orchestration.*Cli*' } | Select-Object -First 1
  }
  if ($published) {
    $targetName = if ($published.Extension) { 'Koan' + $published.Extension } else { 'Koan' }
  $target = Join-Path -Path $outPath -ChildPath $targetName
    Copy-Item -Path $published.FullName -Destination $target -Force
    Write-Host "[cli-publish] Wrote alias: $target"
  } else {
    Write-Warning "[cli-publish] Could not locate main binary to alias as 'Koan'."
  }
  Write-Host "[cli-publish] Publish OK" -ForegroundColor Green
}
finally {
  Pop-Location
}
