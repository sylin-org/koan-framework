param(
  [ValidateSet('win-x64','linux-x64','osx-x64','osx-arm64')]
  [string]$Runtime = 'win-x64',
  [ValidateSet('Debug','Release')]
  [string]$Configuration = 'Release',
  [string]$Project = 'src/Koan.Orchestration.Cli/Koan.Orchestration.Cli.csproj'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$distBin = Join-Path $repoRoot 'dist/bin'
New-Item -ItemType Directory -Force -Path $distBin | Out-Null

Write-Host "[cli-all] Build"
& "$PSScriptRoot/cli-build.ps1" -Configuration $Configuration

Write-Host "[cli-all] Publish → dist/bin"
& "$PSScriptRoot/cli-publish.ps1" -Runtime $Runtime -Configuration $Configuration -Project $Project -Output $distBin

Write-Host "[cli-all] Install (ensure PATH only)"
& "$PSScriptRoot/cli-install.ps1" -Source '' -Dest $distBin

Write-Host "[cli-all] Verify"
& "$PSScriptRoot/cli-verify.ps1"

Write-Host "[cli-all] Done"
