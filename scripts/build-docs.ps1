param(
  [string]$ConfigPath = "docs/api/docfx.json",
  [string]$OutputDir,
  [switch]$Clean,
  [switch]$Serve,
  [int]$Port = 8080,
  [switch]$Strict,
  [ValidateSet('Verbose','Info','Warning','Error')]
  [string]$LogLevel = 'Warning'
)

$ErrorActionPreference = 'Stop'

function Write-Heading($text) {
  Write-Host "==> $text" -ForegroundColor Cyan
}

function Assert-FileExists($path) {
  if (-not (Test-Path $path)) {
    throw "File not found: $path"
  }
}

function Get-DocfxCommand() {
  if (Get-Command docfx -ErrorAction SilentlyContinue) {
    return 'docfx'
  }
  return $null
}

Push-Location (Resolve-Path "$PSScriptRoot\..\")
try {
  $repoRoot = Get-Location
  $configFullPath = Resolve-Path $ConfigPath -ErrorAction Stop
  $configDir = Split-Path -Parent $configFullPath
  Assert-FileExists $configFullPath

  Write-Heading "Sora Docs Build"
  Write-Host "Repo Root: $repoRoot"
  Write-Host "Config   : $configFullPath"

  $docfx = Get-DocfxCommand
  if (-not $docfx) {
    Write-Warning "'docfx' CLI not found in PATH."
    Write-Host "Install options:" -ForegroundColor Yellow
    Write-Host "  - winget install dotnetfoundation.docfx" -ForegroundColor Yellow
    Write-Host "  - choco install docfx -y" -ForegroundColor Yellow
    throw "DocFX is required to build the docs."
  }

  # Determine output directory (optional override)
  $destFromConfig = $null
  try {
    $json = Get-Content $configFullPath -Raw | ConvertFrom-Json -ErrorAction Stop
    $destFromConfig = $json.build.dest
  } catch { }

  if (-not $OutputDir) { $OutputDir = if ($destFromConfig) { $destFromConfig } else { "_site" } }
  $targetDest = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($configDir, $OutputDir))
  Write-Host "Target  : $targetDest"
  # Also print repo-relative path for clarity
  $repoRootPath = $repoRoot.Path
  $targetRel = $targetDest
  if ($targetRel.StartsWith($repoRootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    $targetRel = $targetRel.Substring($repoRootPath.Length).TrimStart('\\')
  }
  Write-Host "Target (repo-relative): $targetRel"

  # Staging directory for clean build output
  $artifactsRoot = Join-Path $repoRoot 'artifacts/docs'
  if (-not (Test-Path $artifactsRoot)) { New-Item -ItemType Directory -Path $artifactsRoot | Out-Null }
  $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
  $stagingDir = Join-Path $artifactsRoot "site-$stamp"
  if (Test-Path $stagingDir) { Remove-Item -Recurse -Force $stagingDir }
  New-Item -ItemType Directory -Path $stagingDir | Out-Null

  if ($Clean) {
    Write-Heading "Cleaning target directory"
    if (Test-Path $targetDest) { Remove-Item -Recurse -Force $targetDest }
  }

  $logFile = Join-Path $artifactsRoot "build-$stamp.log"

  Write-Heading "Building docs with DocFX ($LogLevel)"
  $docfxArgs = @(
    'build', $configFullPath,
    '--logLevel', $LogLevel,
    '--output', $stagingDir
  )

  $buildSucceeded = $false
  $output = & $docfx @docfxArgs 2>&1 | Tee-Object -FilePath $logFile
  if ($LASTEXITCODE -eq 0) { $buildSucceeded = $true }

  # Strict mode: fail if warnings are present
  if ($Strict) {
    # Prefer DocFX summary lines like "    2 warning(s)"; fallback to counting warning-prefixed lines
    $summaryMatches = $output | Select-String -Pattern '\b(\d+)\s+warning\(s\)' -AllMatches
    $summaryCount = 0
    if ($summaryMatches) {
      $summaryCount = ($summaryMatches.Matches | ForEach-Object { [int]$_.Groups[1].Value } | Measure-Object -Sum).Sum
    }
    $warningLineCount = ($output | Select-String -Pattern '^(\s*)?warning\b' -CaseSensitive:$false).Count
    $effectiveWarnings = if ($summaryCount -gt 0) { $summaryCount } else { $warningLineCount }
    if ($effectiveWarnings -gt 0) {
      Write-Error "DocFX reported $effectiveWarnings warning(s) with Strict mode enabled. See log: $logFile"
      exit 1
    }
  }

  if (-not $buildSucceeded) {
    Write-Error "DocFX build failed. See log: $logFile"
    exit 1
  }

  Write-Heading "Build complete"
  Write-Host "Log file: $logFile"

  # Wipe target and copy staging output
  Write-Heading "Publishing to target"
  if (Test-Path $targetDest) {
    Write-Host "Wiping: $targetDest" -ForegroundColor Yellow
    Remove-Item -Recurse -Force $targetDest
  }
  New-Item -ItemType Directory -Path $targetDest | Out-Null
  Write-Host "Copying from staging: $stagingDir" -ForegroundColor Cyan
  Copy-Item -Path (Join-Path $stagingDir '*') -Destination $targetDest -Recurse -Force

  # Quick verification to reduce confusion about publish location
  $indexPath = Join-Path $targetDest 'index.html'
  if (Test-Path $indexPath) {
    Write-Host "Published index: $indexPath" -ForegroundColor Green
  } else {
    Write-Warning "index.html not found under target. Contents may differ from expectation."
  }

  if ($Serve) {
  Write-Heading "Starting DocFX server"
  $serveArgs = @('serve', $targetDest)
    if ($Port -gt 0) { $serveArgs += @('--port', $Port) }
    & $docfx @serveArgs | Write-Host
  }
}
finally {
  Pop-Location
}
