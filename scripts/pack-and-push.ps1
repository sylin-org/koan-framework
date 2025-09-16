param(
  [switch]$Push,
  [string]$Source = 'https://api.nuget.org/v3/index.json',
  [string]$ApiKey = $env:NUGET_API_KEY,
  [string]$OutDir = 'artifacts',
  [string]$Configuration = 'Release',
  [string]$Version
)

$ErrorActionPreference = 'Stop'

function Resolve-RepoRoot {
  $root = Resolve-Path (Join-Path $PSScriptRoot '..')
  return $root.Path
}

function Get-NuGetVersion {
  param([string]$RepoRoot)
  if ($env:PATH -notlike '*\.dotnet\\tools*') {
    $tools = Join-Path $env:USERPROFILE '.dotnet\\tools'
    if (Test-Path $tools) { $env:PATH = "$tools;$env:PATH" }
  }
  $nbgv = "nbgv"
  try {
    $v = & $nbgv get-version -v NuGetPackageVersion 2>$null
    if ($LASTEXITCODE -eq 0 -and $v) { return $v.Trim() }
  } catch {}
  # Fallback: parse version.json and add -local suffix
  $verFile = Join-Path $RepoRoot 'version.json'
  if (Test-Path $verFile) {
    try {
      $json = Get-Content -Raw $verFile | ConvertFrom-Json
      if ($json.version) { return "$($json.version)-local" }
    } catch {}
  }
  return '0.0.0-local'
}

$repo = Resolve-RepoRoot
New-Item -ItemType Directory -Force -Path (Join-Path $repo $OutDir) | Out-Null

Write-Host "[pack] Restoring solution..." -ForegroundColor Cyan
& dotnet restore (Join-Path $repo 'Koan.sln') | Out-Null

if (-not $Version -or [string]::IsNullOrWhiteSpace($Version)) {
  Write-Host "[pack] Computing NuGet version via NB.GV..." -ForegroundColor Cyan
  $Version = Get-NuGetVersion -RepoRoot $repo
}
Write-Host "[pack] Using version: $Version" -ForegroundColor Green

# Pack all packable csproj to a single output folder
$findScript = Join-Path $repo '.github\\scripts\\find-packable-csproj.ps1'
if (-not (Test-Path $findScript)) { throw "Missing script: $findScript" }

$projects = & pwsh -NoProfile -ExecutionPolicy Bypass -File $findScript
foreach ($p in $projects) {
  Write-Host "[pack] dotnet pack $p -c $Configuration -o $OutDir -p:ContinuousIntegrationBuild=true -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg -p:RepositoryType=git" -ForegroundColor DarkCyan
  $args = @('pack', $p, '-c', $Configuration, '-o', (Join-Path $repo $OutDir), '-p:ContinuousIntegrationBuild=true', '-p:IncludeSymbols=true', '-p:SymbolPackageFormat=snupkg', '-v', 'minimal')
  if ($Version) { $args += "-p:Version=$Version" }
  & dotnet @args
}

# Pack meta packages with aligned dependency ranges
$metaScript = Join-Path $repo '.github\\scripts\\pack-meta.ps1'
if (Test-Path $metaScript) {
  Write-Host "[pack] Packing meta packages..." -ForegroundColor Cyan
  & pwsh -NoProfile -ExecutionPolicy Bypass -File $metaScript -Version $Version -OutDir (Join-Path $repo $OutDir)
}

# Push if requested
if ($Push) {
  if (-not $ApiKey -or [string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "NUGET_API_KEY is not set. Provide -ApiKey or set $env:NUGET_API_KEY."
  }
  Write-Host "[push] Pushing packages to $Source (skip duplicates)..." -ForegroundColor Cyan
  $out = Join-Path $repo $OutDir
  & dotnet nuget push (Join-Path $out '*.nupkg') --api-key $ApiKey --source $Source --skip-duplicate
  & dotnet nuget push (Join-Path $out '*.snupkg') --api-key $ApiKey --source $Source --skip-duplicate
}

Write-Host "[done] Packages in $(Join-Path $repo $OutDir):" -ForegroundColor Green
Get-ChildItem -Path (Join-Path $repo $OutDir) -Filter *.nupkg | Sort-Object LastWriteTime -Descending | Select-Object -First 20 | ForEach-Object { Write-Host " - $($_.Name)" }
