[CmdletBinding()]
param(
    [string]$Solution = "Koan.sln"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path -Path (Join-Path $PSScriptRoot "..")
$solutionPath = Join-Path $repoRoot $Solution
$solutionName = [System.IO.Path]::GetFileNameWithoutExtension($solutionPath)

Write-Host "Rebuilding solution '$Solution' at $repoRoot" -ForegroundColor Cyan

Push-Location $repoRoot
try {
    if (Test-Path $solutionPath) {
        Write-Host "Removing existing solution file $solutionPath" -ForegroundColor DarkYellow
        Remove-Item $solutionPath -Force
    }

    Write-Host "Creating empty solution $solutionName" -ForegroundColor Cyan
    dotnet new sln --name $solutionName --force | Out-Null

    Write-Host "Discovering projects..." -ForegroundColor Cyan
    $projects = Get-ChildItem -Path $repoRoot -Filter *.csproj -Recurse | Where-Object {
        $_.FullName -notlike '*\bin\*' -and $_.FullName -notlike '*\obj\*'
    } | Sort-Object FullName

    if (-not $projects) {
        throw "No projects (.csproj) found under $repoRoot"
    }

    foreach ($project in $projects) {
        $relativePath = Resolve-Path -Path $project.FullName -Relative
        Write-Host "Adding project $relativePath" -ForegroundColor Gray
        dotnet sln $solutionPath add $relativePath | Out-Null
    }

    Write-Host "Solution rebuilt. Added $($projects.Count) projects." -ForegroundColor Green
}
finally {
    Pop-Location
}
