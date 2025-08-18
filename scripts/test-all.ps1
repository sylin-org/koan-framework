param(
  [string]$Configuration = "Debug",
  [string]$Filter = ""
)

$ErrorActionPreference = "Stop"

Write-Host "[test-all] Killing stale test hosts..." -ForegroundColor Cyan
pwsh -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot/kill-testhost.ps1" -IncludeDotnet

Write-Host "[test-all] Cleaning test bin/obj..." -ForegroundColor Cyan
pwsh -NoProfile -ExecutionPolicy Bypass -File "$PSScriptRoot/clean-test-binaries.ps1"

Write-Host "[test-all] Building solution..." -ForegroundColor Cyan
& dotnet build "$PSScriptRoot/../Sora.sln" -c $Configuration

Write-Host "[test-all] Running tests..." -ForegroundColor Cyan
$testArgs = @("test", "$PSScriptRoot/../Sora.sln", "-c", $Configuration, "--no-build")
if ($Filter -and $Filter.Trim().Length -gt 0) { $testArgs += @("--filter", $Filter) }
& dotnet @testArgs
