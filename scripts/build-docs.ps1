param(
  [string]$ConfigPath = "docs/api/docfx.json",
  [string]$OutputDir,
  [switch]$Clean,
  [switch]$Serve,
  [int]$Port = 8080,
  [switch]$Strict,
  [switch]$FullSite, # when set, build root docfx.json after API build
  [switch]$RunLint,  # when set, run docs-lint.ps1 against docs folder
  [string[]]$LintExclude = @('docs/archive/**','docs/proposals/**','docs/external/**','docs/migration/**'),
  [switch]$LintFailOnWarning,
  [ValidateSet('Verbose', 'Info', 'Warning', 'Error')]
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
  $repoRootPath = $repoRoot.ProviderPath
  # Resolve config; if default not found, auto-discover under docs/**/docfx.json
  $configFullPath = $null
  try {
    $configFullPath = Resolve-Path $ConfigPath -ErrorAction Stop
  }
  catch {
    # Try discovery, prefer docs/ or documentation/ folders before repo root
    $searchRoots = @()
    foreach ($folder in 'docs', 'documentation') {
      $candidateDir = Join-Path $repoRootPath $folder
      if (Test-Path $candidateDir) { $searchRoots += $candidateDir }
    }
    if (-not $searchRoots) { $searchRoots = @($repoRootPath) }

    $candidate = $null
    foreach ($root in $searchRoots) {
      $candidate = Get-ChildItem -Path $root -Recurse -Filter 'docfx.json' -File -ErrorAction SilentlyContinue |
      Sort-Object FullName |
      Select-Object -First 1
      if ($candidate) { break }
    }

    if (-not $candidate -and (Test-Path (Join-Path $repoRootPath 'docfx.json'))) {
      $candidate = Get-Item (Join-Path $repoRootPath 'docfx.json')
    }

    if ($candidate) {
      $configFullPath = $candidate.FullName
    }
    else {
      throw "DocFX config not found. Tried '$ConfigPath' and discovery under 'docs/**/docfx.json' or repo root."
    }
  }
  $configDir = Split-Path -Parent $configFullPath

  Write-Heading "Koan Docs Build"
  Write-Host "Repo Root: $repoRootPath"
  Write-Host "Config   : $configFullPath"

  # Read config JSON early to honor optional disabled stub files
  $json = $null
  try {
    $json = Get-Content $configFullPath -Raw | ConvertFrom-Json -ErrorAction Stop
  }
  catch { }

  if ($json -and $json.PSObject.Properties.Name -contains 'disabled' -and $json.disabled -eq $true) {
    Write-Host "DocFX config is marked as disabled. Skipping DocFX build." -ForegroundColor Yellow
    # Still generate any pre-build content that other docs might depend on
    $artifactsRoot = Join-Path $repoRootPath 'artifacts/docs'
    if (-not (Test-Path $artifactsRoot)) { New-Item -ItemType Directory -Path $artifactsRoot | Out-Null }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $logFile = Join-Path $artifactsRoot "build-$stamp.log"
    "Docs build skipped due to disabled config at $configFullPath" | Set-Content -Path $logFile -Encoding UTF8
    Write-Heading "Build complete (skipped)"
    Write-Host "Log file: $logFile"
    return
  }

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
    if (-not $json) { $json = Get-Content $configFullPath -Raw | ConvertFrom-Json -ErrorAction Stop }
    $destFromConfig = $json.build.dest
  }
  catch { }

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
    # Also clean generated API YAML to avoid stale UIDs from removed types lingering in the repo
    $apiDir = Join-Path $configDir 'api'
    if (Test-Path $apiDir) {
      Write-Host "Cleaning API metadata: $apiDir" -ForegroundColor DarkGray
      Remove-Item -Recurse -Force $apiDir
    }
  }

  # Logs directory
  $artifactsRoot = Join-Path $repoRootPath 'artifacts/docs'
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
  }
  else {
    $targetCount = (Get-ChildItem -Path $targetDest  -Recurse -Force | Measure-Object).Count
    Write-Host "Items in target: $targetCount" -ForegroundColor DarkGray
  }

  # Quick verification to reduce confusion about publish location (with short retry)
  $candidates = @('index.html', 'toc.html', 'README.html')
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
  }
  else {
    Write-Warning "No top-level doc file (index/toc/README) found under target. Contents may differ from expectation."
  }

  if ($Serve) {
    Write-Heading "Starting DocFX server"
    $serveArgs = @('serve', $targetDest)
    if ($Port -gt 0) { $serveArgs += @('--port', $Port) }
    & $docfx @serveArgs | Write-Host
  }

  # Optional: Build full site using root docfx.json to catch cross-doc issues
  if ($FullSite) {
    Write-Heading "Building FULL docs site (root docfx.json)"
    $rootConfig = Join-Path $repoRootPath 'docfx.json'
    Assert-FileExists $rootConfig
    $fullOutDir = Join-Path (Split-Path -Parent $rootConfig) 'artifacts/other/website/docs'
    if ($Clean -and (Test-Path $fullOutDir)) { Remove-Item -Recurse -Force $fullOutDir }
    $fullLog = Join-Path $artifactsRoot "build-full-$stamp.log"
    $rootArgs = @('build', $rootConfig, '--logLevel', $LogLevel)
    $fullOutput = & $docfx @rootArgs 2>&1 | Tee-Object -FilePath $fullLog
    if ($LASTEXITCODE -ne 0) {
      Write-Error "Full-site DocFX build failed. See log: $fullLog"
      exit 1
    }
    if ($Strict) {
      $sum = $fullOutput | Select-String -Pattern '\\b(\\d+)\\s+warning\\(s\\)'
      $count = 0
      if ($sum) { $count = ($sum.Matches | ForEach-Object { [int]$_.Groups[1].Value } | Measure-Object -Sum).Sum }
      $warnLines = ($fullOutput | Select-String -Pattern '^(\\s*)?warning\\b' -CaseSensitive:$false).Count
      $eff = if ($count -gt 0) { $count } else { $warnLines }
      if ($eff -gt 0) {
        Write-Error "Full-site DocFX reported $eff warning(s) with Strict mode. See log: $fullLog"
        exit 1
      }
    }
    Write-Host "Full-site build complete. Log: $fullLog"
  }

  # Optional: Run docs linter for front-matter, links, and terms
  if ($RunLint) {
    Write-Heading "Running docs linter (docs-lint.ps1)"
    $lintScript = Join-Path $repoRootPath 'scripts/docs-lint.ps1'
    if (Test-Path $lintScript) {
      # Lint only active docs surfaces; exclude ADRs/archives/design/specs by default
      $excludes = @(
        'docs/reference/_generated/**',
        'docs/archive/**',
        'docs/decisions/**',
        'docs/design/**',
        'docs/examples/**',
        'docs/specifications/**',
        'docs/sessions/**',
        'docs/templates/**',
        'docs/architecture/**',
        'docs/migration/**',
        'docs/proposals/**',
        'docs/external/**'
      )
      $lintParams = @{
        Roots   = @('docs/reference', 'docs/support', 'docs/getting-started')
        Exclude = $excludes + @('docs/_inventory.md')
        Output  = 'list'
      }
      # Enable TOC validation; in CI (GitHub Actions), require YAML module or fail
      $inCI = $false
      if ($env:GITHUB_ACTIONS -and $env:GITHUB_ACTIONS -eq 'true') { $inCI = $true }
      $yamlOk = $false
      try {
        if (Get-Command ConvertFrom-Yaml -ErrorAction SilentlyContinue) { $yamlOk = $true }
        else { Import-Module powershell-yaml -ErrorAction Stop | Out-Null; $yamlOk = $true }
      }
      catch { $yamlOk = $false }
      if ($yamlOk) {
        $lintParams['ValidateToc'] = $true
      }
      else {
        if ($inCI) {
          Write-Error "TOC validation prerequisites missing in CI (powershell-yaml). Install the module in CI before running."
          exit 1
        }
        else {
          Write-Host "TOC validation skipped (powershell-yaml not installed)." -ForegroundColor Yellow
        }
      }
      if ($LintFailOnWarning) { $lintParams['FailOnWarning'] = $true }
      & $lintScript @lintParams
      if ($LASTEXITCODE -ne 0) {
        Write-Error "Docs lint failed. See output above."
        exit 1
      }

      # Phase 2: Non-gating lint over Guides for visibility; will be made gating after remediation
      Write-Heading "Running docs linter (Guides, non-gating)"
      $lintGuides = @{
        Roots   = @('docs/guides')
        Exclude = $excludes
        Output  = 'list'
      }
      # TOC validation not relevant for per-root run; skip
      & $lintScript @lintGuides
      if ($LASTEXITCODE -ne 0) {
        Write-Warning "Guides lint reported issues (non-gating). Review output above."
      }
    }
    else {
      Write-Warning "docs-lint.ps1 not found; skipping lint"
    }
  }
}
finally {
  Pop-Location
}
