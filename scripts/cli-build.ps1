param(
  [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
  [string]$Project = 'src/Sora.Orchestration.Cli/Sora.Orchestration.Cli.csproj',
  [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
  Write-Host "[cli-build] dotnet build $Project -c $Configuration"
  $dotnetArgs = @('build', $Project, '-c', $Configuration, '--nologo', '--verbosity:minimal')
  if ($NoRestore) { $dotnetArgs += '--no-restore' }
  dotnet @dotnetArgs
  Write-Host "[cli-build] Build OK" -ForegroundColor Green
}
finally {
  Pop-Location
}
