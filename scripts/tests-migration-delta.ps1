param(
    [string]$LegacyRoot = "tests.old",
    [string]$NewRoot = "tests",
    [switch]$IncludeNewOnly,
    [switch]$AsJson,
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

function Resolve-Root([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Path '$path' does not exist."
    }
    return (Resolve-Path -LiteralPath $path).Path
}

$legacyRootPath = Resolve-Root $LegacyRoot
$newRootPath = Resolve-Root $NewRoot

function Get-TestProjects([string]$root) {
    return Get-ChildItem -LiteralPath $root -Filter *.csproj -Recurse | ForEach-Object {
        $relative = $_.FullName.Substring($root.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar)
        [PSCustomObject]@{
            Name = $_.BaseName
            Path = $_.FullName
            RelativePath = $relative
        }
    }
}

$legacyProjects = Get-TestProjects $legacyRootPath
$newProjects = Get-TestProjects $newRootPath

$newMap = $newProjects | Group-Object -Property Name -AsHashTable

$result = @()
foreach ($legacy in $legacyProjects | Sort-Object Name) {
    $status = "Pending"
    $newPath = $null
    if ($newMap.ContainsKey($legacy.Name)) {
        $status = "Migrated"
        $newPath = ($newMap[$legacy.Name] | Select-Object -First 1).RelativePath
    }

    $result += [PSCustomObject]@{
        Project = $legacy.Name
        Status = $status
        LegacyPath = $legacy.RelativePath
        NewPath = $newPath
    }
}

if ($IncludeNewOnly) {
    $legacyNames = $legacyProjects.Name
    foreach ($newProj in $newProjects | Where-Object { $_.Name -notin $legacyNames } | Sort-Object Name) {
        $result += [PSCustomObject]@{
            Project = $newProj.Name
            Status = "NewOnly"
            LegacyPath = $null
            NewPath = $newProj.RelativePath
        }
    }
}

if ($AsJson) {
    $result | Sort-Object Project | ConvertTo-Json -Depth 3
}
else {
    if (-not $Quiet) {
        $pendingCount = ($result | Where-Object { $_.Status -eq "Pending" }).Count
        $migratedCount = ($result | Where-Object { $_.Status -eq "Migrated" }).Count
        $newOnlyCount = ($result | Where-Object { $_.Status -eq "NewOnly" }).Count
        Write-Host "Legacy projects: $($legacyProjects.Count)" -ForegroundColor Cyan
        Write-Host "Migrated:      $migratedCount" -ForegroundColor Green
        Write-Host "Pending:       $pendingCount" -ForegroundColor Yellow
        if ($IncludeNewOnly) {
            Write-Host "New Only:     $newOnlyCount" -ForegroundColor Magenta
        }
        Write-Host
    }
    $result | Sort-Object Project | Format-Table -AutoSize
}
