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

# Generate docs/reference/_generated/adapter-matrix.md from docs/reference/_data/adapters.yml
function Write-AdapterMatrixMarkdown {
  param(
    [Parameter(Mandatory=$true)][string]$RepoRoot
  )
  try {
    if (-not (Get-Command ConvertFrom-Yaml -ErrorAction SilentlyContinue)) {
      Write-Warning "ConvertFrom-Yaml is not available; attempting simple YAML parse fallback."
    }

    $dataPath = Join-Path $RepoRoot 'docs/reference/_data/adapters.yml'
    if (-not (Test-Path $dataPath)) {
      Write-Warning "Adapter data YAML not found: $dataPath"
      return
    }

    $adapters = @()
    if (Get-Command ConvertFrom-Yaml -ErrorAction SilentlyContinue) {
      $yamlRaw = Get-Content -Path $dataPath -Raw
      $yaml = ConvertFrom-Yaml -Yaml $yamlRaw -ErrorAction Stop
      if (-not $yaml.adapters) {
        Write-Warning "No 'adapters' node found in $dataPath; skipping generation."
        return
      }
      $adapters = $yaml.adapters
    }
    else {
      # Very simple parser for the specific structure in adapters.yml
      $current = $null
      $inGuard = $false
      Get-Content -Path $dataPath | ForEach-Object {
        $line = $_.TrimEnd()
        if ($line -match '^\s*-\s+name:\s*(.+)$') {
          if ($current) { $adapters += $current }
          $name = $Matches[1].Trim()
          $current = [ordered]@{ name = $name }
          $inGuard = $false
          return
        }
        if (-not $current) { return }
        if ($line -match '^\s*guardrails:\s*$') { $inGuard = $true; return }
        if ($line -match '^\s*[A-Za-z]') { $inGuard = $false }

        if ($inGuard) {
          if ($line -match '^\s*defaultPageSize:\s*(\d+)') { $current.defaultPageSize = [int]$Matches[1] }
          elseif ($line -match '^\s*maxPageSize:\s*(\d+)') { $current.maxPageSize = [int]$Matches[1] }
          elseif ($line -match '^\s*defaultTopK:\s*(\d+)') { $current.defaultTopK = [int]$Matches[1] }
          return
        }

        if ($line -match '^\s*(storage|pagingPushdown|filterPushdown|schemaTools|instructionApi|vector):\s*(.+)$') {
          $key = $Matches[1]; $val = $Matches[2].Trim()
          $current[$key] = $val
        }
        elseif ($line -match '^\s*(transactions|batching):\s*(.+)$') {
          $key = $Matches[1]; $val = $Matches[2].Trim()
          if ($val -match '^(true|false)$') { $current[$key] = [bool]::Parse($val) }
          else { $current[$key] = $val }
        }
        elseif ($line -match '^\s*notes:\s*"?(.+?)"?$') {
          $current.notes = $Matches[1]
        }
      }
      if ($current) { $adapters += $current }
    }

    $genDir = Join-Path $RepoRoot 'docs/reference/_generated'
    if (-not (Test-Path $genDir)) { New-Item -ItemType Directory -Path $genDir | Out-Null }
    $outFile = Join-Path $genDir 'adapter-matrix.md'

    $lines = @()
    $lines += "<!-- Auto-generated from docs/reference/_data/adapters.yml. Do not edit manually. -->"
    $lines += ""
    $lines += "| Adapter | Storage | Tx | Batching | Paging | Filter | Schema | Instruction | Vector | Guardrails | Notes |"
    $lines += "|---|---|---:|---:|---|---|---|---|---|---|---|"

    foreach ($a in $adapters) {
      $name   = "$($a.name)"
      $stor   = "$($a.storage)"
      $tx     = if ($a.transactions) { if ($a.transactions -is [string]) { $a.transactions } elseif ($a.transactions) { 'Yes' } else { 'No' } } else { 'No' }
      $batch  = if ($a.batching) { if ($a.batching -is [string]) { $a.batching } elseif ($a.batching) { 'Yes' } else { 'No' } } else { 'No' }
      $paging = if ($a.pagingPushdown) { $a.pagingPushdown } else { 'n/a' }
      $filter = if ($a.filterPushdown) { $a.filterPushdown } else { 'n/a' }
      $schema = if ($a.schemaTools) { $a.schemaTools } else { 'n/a' }
      $instr  = if ($a.instructionApi) { $a.instructionApi } else { 'n/a' }
      $vector = if ($a.vector) { $a.vector } else { 'none' }
      $guard  = ''
      $gps = $null; $gmps = $null; $gtk = $null
      if ($a.guardrails) { $gps = $a.guardrails.defaultPageSize; $gmps = $a.guardrails.maxPageSize; $gtk = $a.guardrails.defaultTopK }
      else { $gps = $a.defaultPageSize; $gmps = $a.maxPageSize; $gtk = $a.defaultTopK }
      if ($gps -or $gmps) { $guard = "page $gps/$gmps" }
      elseif ($gtk) { $guard = "topK $gtk" }
      $notes  = ($a.notes ?? '') -replace '\r?\n', ' '

      $lines += "| $name | $stor | $tx | $batch | $paging | $filter | $schema | $instr | $vector | $guard | $notes |"
    }

    Set-Content -Path $outFile -Value ($lines -join "`n") -Encoding UTF8
    Write-Host "Generated adapter matrix: $outFile" -ForegroundColor DarkGray
  }
  catch {
    Write-Warning "Failed to generate adapter matrix: $($_.Exception.Message)"
  }
}

Push-Location (Resolve-Path "$PSScriptRoot\..\")
try {
  $repoRoot = Get-Location
  $repoRootPath = $repoRoot.ProviderPath
  # Resolve config; if default not found, auto-discover under docs/**/docfx.json
  $configFullPath = $null
  try {
    $configFullPath = Resolve-Path $ConfigPath -ErrorAction Stop
  } catch {
    # Try discovery, prefer docs/ or documentation/ folders before repo root
    $searchRoots = @()
    foreach ($folder in 'docs','documentation') {
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
    } else {
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
  } catch { }

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

  # Pre-build content generation
  Write-Heading "Generating docs content"
  Write-AdapterMatrixMarkdown -RepoRoot $repoRootPath

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
