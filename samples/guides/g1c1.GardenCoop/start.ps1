param(
    [string[]]$AppArgs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$workingDir = $PSScriptRoot
$dotnetArgs = @("run", "--project", "g1c1.GardenCoop.csproj")

if ($AppArgs.Count -gt 0) {
    $dotnetArgs += "--"
    $dotnetArgs += $AppArgs
}

Start-Process -FilePath "dotnet" -ArgumentList $dotnetArgs -WorkingDirectory $workingDir -WindowStyle Normal | Out-Null

Start-Sleep -Seconds 2

Start-Process -FilePath "https://localhost:5001/" -ErrorAction SilentlyContinue
Start-Process -FilePath "http://localhost:5000/"
