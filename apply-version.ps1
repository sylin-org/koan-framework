#!/usr/bin/env pwsh

$ErrorActionPreference = "Stop"

$versionFile = Join-Path $PSScriptRoot "version.json"
$srcFolder = Join-Path $PSScriptRoot "src"

if (-not (Test-Path $versionFile)) {
    Write-Error "version.json not found at: $versionFile"
    exit 1
}

$versionData = Get-Content $versionFile | ConvertFrom-Json
$version = $versionData.version

Write-Host "Version from version.json: $version" -ForegroundColor Cyan

$projectFiles = Get-ChildItem -Path $srcFolder -Filter "*.csproj" -Recurse

if ($projectFiles.Count -eq 0) {
    Write-Warning "No .csproj files found in $srcFolder"
    exit 0
}

Write-Host "Found $($projectFiles.Count) projects" -ForegroundColor Green

foreach ($project in $projectFiles) {
    Write-Host "Processing: $($project.Name)" -ForegroundColor Yellow

    [xml]$csproj = Get-Content $project.FullName

    $propertyGroup = $csproj.Project.PropertyGroup | Select-Object -First 1
    if ($null -eq $propertyGroup) {
        $propertyGroup = $csproj.CreateElement("PropertyGroup")
        $csproj.Project.AppendChild($propertyGroup) | Out-Null
    }

    $versionNode = $propertyGroup.SelectSingleNode("Version")
    if ($null -eq $versionNode) {
        $versionNode = $csproj.CreateElement("Version")
        $propertyGroup.AppendChild($versionNode) | Out-Null
    }
    $versionNode.InnerText = $version

    $assemblyVersionNode = $propertyGroup.SelectSingleNode("AssemblyVersion")
    if ($null -eq $assemblyVersionNode) {
        $assemblyVersionNode = $csproj.CreateElement("AssemblyVersion")
        $propertyGroup.AppendChild($assemblyVersionNode) | Out-Null
    }
    $assemblyVersionNode.InnerText = $version

    $fileVersionNode = $propertyGroup.SelectSingleNode("FileVersion")
    if ($null -eq $fileVersionNode) {
        $fileVersionNode = $csproj.CreateElement("FileVersion")
        $propertyGroup.AppendChild($fileVersionNode) | Out-Null
    }
    $fileVersionNode.InnerText = $version

    $csproj.Save($project.FullName)
    Write-Host "  ✓ Updated to version $version" -ForegroundColor Green
}

Write-Host "`nAll projects updated successfully!" -ForegroundColor Green