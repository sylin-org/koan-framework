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

  $explicitOutput = $PSBoundParameters.ContainsKey('OutputDir')
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

  if ($Clean) {
    Write-Heading "Cleaning target directory"
    if (Test-Path $targetDest) { Remove-Item -Recurse -Force $targetDest }
  }

  # Logs directory
  $artifactsRoot = Join-Path $repoRoot 'artifacts/docs'
  if (-not (Test-Path $artifactsRoot)) { New-Item -ItemType Directory -Path $artifactsRoot | Out-Null }
  $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
  $logFile = Join-Path $artifactsRoot "build-$stamp.log"

  Write-Heading "Building docs with DocFX ($LogLevel)"
  $docfxArgs = @('build', $configFullPath, '--logLevel', $LogLevel)
  # Only override output when user explicitly asked, otherwise let docfx.json 'dest' control
  if ($explicitOutput -or -not $destFromConfig) {
    $docfxArgs += @('-o', $targetDest)
  }

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

  # Output verification
  Write-Heading "Output verification"
  if (-not (Test-Path $targetDest)) {
    Write-Warning "Target path not found after build: $targetDest"
  } else {
    $targetCount  = (Get-ChildItem -Path $targetDest  -Recurse -Force | Measure-Object).Count
    Write-Host "Items in target: $targetCount" -ForegroundColor DarkGray
  }

  # Quick verification to reduce confusion about publish location (with short retry)
  $candidates = @('index.html','toc.html','README.html')
  $found = $false
  $hit = $null
  for ($i = 0; $i -lt 12 -and -not $found; $i++) {
    foreach ($name in $candidates) {
      $p = Join-Path $targetDest $name
      if (Test-Path $p) { $found = $true; $hit = $p; break }
    }
    if (-not $found) { Start-Sleep -Milliseconds 250 }
  }
  if ($found) {
    Write-Host "Published root doc: $hit" -ForegroundColor Green
  } else {
    Write-Warning "No top-level doc file (index/toc/README) found under target. Contents may differ from expectation."
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
