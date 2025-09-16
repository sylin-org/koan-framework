param(
  [switch]$Clean,
  [switch]$NoCache
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Ensure we run from the script's directory
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $here
try {
  $composeFile = "S8.Compose/docker-compose.yml"
  $project = "koan-s8-flow"
  $apiUrl = "http://localhost:4903"

  if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker is required but not found in PATH.'
  }

  $composeCmd = { param([string[]]$rest) docker compose -p $project -f $composeFile @rest }

  if ($Clean) {
    & $composeCmd @('down','-v')
  }

  $buildArgs = @('build')
  if ($NoCache) { $buildArgs += '--no-cache' }
  & $composeCmd $buildArgs
  & $composeCmd @('up','-d')

  Write-Host "Waiting for API to be ready at $apiUrl ..."
  $ok = $false
  for ($i=0; $i -lt 60; $i++) {
    try {
      Invoke-WebRequest -Uri $apiUrl -TimeoutSec 2 -UseBasicParsing | Out-Null
      $ok = $true
      break
    } catch {}
    Start-Sleep -Seconds 2
  }
  if (-not $ok) {
    Write-Warning "Timed out waiting for $apiUrl"
  }
  Start-Process $apiUrl | Out-Null
  Write-Host "Stack started. Opened $apiUrl in your default browser."
}
finally {
  Pop-Location
}
