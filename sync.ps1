param(
    [string]$SolutionPath = "Sora.sln",
    [string]$SearchRoot = "src"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-RelativePath([string]$BaseDirectory, [string]$Path) {
    $base = Resolve-Path -LiteralPath $BaseDirectory
    $full = Resolve-Path -LiteralPath $Path
    $baseUri = [Uri]::new(($base.ProviderPath.TrimEnd([IO.Path]::DirectorySeparatorChar)) + [IO.Path]::DirectorySeparatorChar)
    $fullUri = [Uri]::new($full.ProviderPath)
    $rel = $baseUri.MakeRelativeUri($fullUri)
    return [Uri]::UnescapeDataString($rel.ToString()).Replace('/', '\\')
}

# Resolve solution and search root
$solutionFullPath = Resolve-Path -LiteralPath $SolutionPath
if (-not (Test-Path -LiteralPath $solutionFullPath)) {
    throw "Solution file not found: $SolutionPath"
}

$solutionDir = Split-Path -Path $solutionFullPath -Parent
$searchRootPath = Join-Path -Path $solutionDir -ChildPath $SearchRoot
if (-not (Test-Path -LiteralPath $searchRootPath)) {
    throw "Search root not found: $searchRootPath"
}

# Discover all projects under the search root
$projectFiles = Get-ChildItem -LiteralPath $searchRootPath -Recurse -Filter '*.csproj' -File

# Read existing projects in the solution (normalize and keep only .csproj lines)
$existingProjects = & dotnet sln $solutionFullPath list 2>$null |
    Where-Object { $_ -match '\.csproj$' } |
    ForEach-Object { ($_.Trim().Replace('/', '\\')).TrimStart('.\\').ToLowerInvariant() }

$existingSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($e in $existingProjects) { [void]$existingSet.Add($e) }

$added = 0
foreach ($proj in $projectFiles) {
    $rel = Get-RelativePath -BaseDirectory $solutionDir -Path $proj.FullName
    $relNorm = $rel.TrimStart('.\\').Replace('/', '\\')

    if (-not $existingSet.Contains($relNorm.ToLowerInvariant())) {
        Write-Host "Adding project: $relNorm"
        & dotnet sln $solutionFullPath add $proj.FullName | Out-Null
        $added++
    } else {
        Write-Host "Already in solution: $relNorm"
    }
}

Write-Host "Done. Added $added project(s)."
