param(
    [string]$Url,
    [string[]]$AppArgs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($env:DOTNET_ENVIRONMENT)) {
    $env:DOTNET_ENVIRONMENT = 'Development'
}

if ([string]::IsNullOrWhiteSpace($env:ASPNETCORE_ENVIRONMENT)) {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
}

if (-not [string]::IsNullOrWhiteSpace($Url)) {
    $env:ASPNETCORE_URLS = $Url
}
elseif ([string]::IsNullOrWhiteSpace($env:ASPNETCORE_URLS)) {
    $env:ASPNETCORE_URLS = 'http://localhost:4998'
}

$dotnetArgs = @('run', '--project', 'S1.Web.csproj', '--no-launch-profile')

if ($AppArgs.Length -gt 0) {
    $dotnetArgs += '--'
    $dotnetArgs += $AppArgs
}

Start-Process -FilePath 'dotnet' -ArgumentList $dotnetArgs -WorkingDirectory $PSScriptRoot -WindowStyle Normal | Out-Null
