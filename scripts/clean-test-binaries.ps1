# Removes test build outputs (bin/obj) to clear stale/locked artifacts
param(
  [string]$Root = "tests",
  [switch]$WhatIf
)

if (-not (Test-Path $Root)) { return }

$dirs = Get-ChildItem -Path $Root -Recurse -Directory -Force -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -in @('bin','obj') }

foreach ($d in $dirs) {
  if ($WhatIf) {
    Write-Host "Would remove $($d.FullName)" -ForegroundColor Yellow
  } else {
    try {
      # Retry a couple times in case of transient locks
      for ($i=0; $i -lt 3; $i++) {
        try {
          Remove-Item -LiteralPath $d.FullName -Recurse -Force -ErrorAction Stop
          break
        } catch {
          Start-Sleep -Milliseconds 250
          if ($i -eq 2) { throw }
        }
      }
      Write-Host "Removed $($d.FullName)" -ForegroundColor Green
    } catch {
      Write-Warning "Failed to remove $($d.FullName): $_"
    }
  }
}
