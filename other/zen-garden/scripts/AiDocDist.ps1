<#
.SYNOPSIS
Prepares Zen Garden documentation for AI distribution.

.DESCRIPTION
Dynamically discovers all .md files in docs/ and consolidates them into namespaced files
suitable for flat-folder AI ingestion. Merges related documents by folder while preserving
all essential content.

.PARAMETER OutputDir
Target directory for AI distribution. Default: dist/ai-context

.PARAMETER Validate
Run validation checks after generation.

.EXAMPLE
.\AiDocDist.ps1 -Validate
#>

param(
    [string]$OutputDir = "../dist/ai-context",
    [switch]$Validate
)

$ErrorActionPreference = "Stop"
$SourceRoot = "docs"
$RootDir = Split-Path -Parent $PSScriptRoot
$Namespace = "zen-garden"

Write-Host "=== Zen Garden AI Distribution Generator ===" -ForegroundColor Cyan
Write-Host "Source: $SourceRoot"
Write-Host "Target: $OutputDir"
Write-Host ""

# Create output directory
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

function Strip-Frontmatter {
    param([string]$Content)

    if ($Content -match '(?s)^---\r?\n.*?\r?\n---\r?\n(.*)$') {
        return $Matches[1].TrimStart()
    }
    return $Content
}

function Extract-Title {
    param([string]$Content, [string]$Fallback)

    if ($Content -match '(?m)^#\s+(.+)$') {
        return $Matches[1]
    }

    return $Fallback -replace '-', ' ' | ForEach-Object { (Get-Culture).TextInfo.ToTitleCase($_) }
}

function Get-TargetName {
    param([string]$RelativePath)

    $normalized = $RelativePath -replace '\\', '/' -replace '/', '-'
    $normalized = $normalized -replace '\.md$', '.md'
    $normalized = $normalized.ToLower() -replace '--+', '-'

    return "$Namespace-$normalized"
}

function Merge-FolderFiles {
    param(
        [string]$FolderPath,
        [string]$Output,
        [string]$Title,
        [string]$Description = "",
        [switch]$Recursive
    )

    $fullFolderPath = Join-Path (Join-Path $RootDir $SourceRoot) $FolderPath

    if (-not (Test-Path $fullFolderPath)) {
        Write-Warning "  Folder not found: $FolderPath (skipping)"
        return @()
    }

    $files = Get-ChildItem -Path $fullFolderPath -Filter "*.md" -File -Recurse:$Recursive | Sort-Object FullName

    if ($files.Count -eq 0) {
        Write-Warning "  No .md files in: $FolderPath (skipping)"
        return @()
    }

    Write-Host "  Consolidating $Output ($($files.Count) files)..." -ForegroundColor Yellow

    # First pass: collect titles for TOC
    $tocEntries = @()
    $sections = @()

    foreach ($file in $files) {
        $fileContent = Get-Content $file.FullName -Raw -Encoding UTF8
        $stripped = Strip-Frontmatter $fileContent
        $fileTitle = Extract-Title $stripped $file.BaseName

        $relativePath = $file.FullName.Substring($fullFolderPath.Length).TrimStart('\', '/')
        $subfolderNote = ""
        $tocSubfolder = ""
        if ($relativePath -match '[\\/]') {
            $subfolder = Split-Path $relativePath -Parent
            $subfolderNote = " *(from $subfolder)*"
            $tocSubfolder = " ($subfolder)"
        }

        # Create anchor from title (lowercase, replace spaces with hyphens)
        $anchor = $fileTitle.ToLower() -replace '[^a-z0-9\s-]', '' -replace '\s+', '-'

        $tocEntries += "- [$fileTitle$tocSubfolder](#$anchor)"
        $sections += "`n## $fileTitle$subfolderNote`n`n$stripped`n`n---`n"
    }

    # Build final content with TOC
    $dateStr = Get-Date -Format "MMMM dd, yyyy"
    $content = "# $Title`n`n$Description`n`n**Consolidated from:** $($files.Count) files`n**Last Updated:** $dateStr`n`n"
    $content += "## Table of Contents`n`n"
    $content += ($tocEntries -join "`n")
    $content += "`n`n---`n"
    $content += ($sections -join "`n")

    $targetPath = Join-Path $OutputDir $Output
    Set-Content -Path $targetPath -Value $content -Encoding UTF8
    Write-Host "    Created $Output" -ForegroundColor Green

    return $files | ForEach-Object { $_.FullName }
}

# Track processed files to avoid duplicates
$processedFiles = @()

# ============================================================================
# CONSOLIDATIONS BY FOLDER
# ============================================================================

Write-Host "`n1. Consolidating ADRs (decisions/)..." -ForegroundColor Cyan
$processedFiles += Merge-FolderFiles -FolderPath "decisions" -Output "$Namespace-decisions-all.md" `
    -Title "Zen Garden Architecture Decision Records" `
    -Description "Complete collection of all architectural decisions made for the Zen Garden project."

Write-Host "`n2. Consolidating Proposals (proposals/)..." -ForegroundColor Cyan
$processedFiles += Merge-FolderFiles -FolderPath "proposals" -Output "$Namespace-proposals-all.md" `
    -Title "Zen Garden Proposals" `
    -Description "Collection of all feature proposals, design evaluations, and specifications under consideration." `
    -Recursive

Write-Host "`n3. Consolidating Concepts (concepts/)..." -ForegroundColor Cyan
$processedFiles += Merge-FolderFiles -FolderPath "concepts" -Output "$Namespace-concepts.md" `
    -Title "Zen Garden Core Concepts" `
    -Description "Comprehensive overview of Zen Garden architecture, design philosophy, and core concepts."

Write-Host "`n4. Consolidating Guides (guides/)..." -ForegroundColor Cyan
$processedFiles += Merge-FolderFiles -FolderPath "guides" -Output "$Namespace-guides-all.md" `
    -Title "Zen Garden Guides" `
    -Description "Operational guides for installation, hardware setup, service configuration, and troubleshooting."

Write-Host "`n5. Consolidating Reference (reference/)..." -ForegroundColor Cyan
$processedFiles += Merge-FolderFiles -FolderPath "reference" -Output "$Namespace-reference-all.md" `
    -Title "Zen Garden Reference" `
    -Description "Complete API, configuration, and technical reference documentation." `
    -Recursive

Write-Host "`n6. Consolidating Specifications (specs/)..." -ForegroundColor Cyan
$processedFiles += Merge-FolderFiles -FolderPath "specs" -Output "$Namespace-specs-all.md" `
    -Title "Zen Garden Specifications" `
    -Description "Technical specifications for all Zen Garden components."

Write-Host "`n7. Consolidating Security (security/)..." -ForegroundColor Cyan
$processedFiles += Merge-FolderFiles -FolderPath "security" -Output "$Namespace-security-all.md" `
    -Title "Zen Garden Security" `
    -Description "Security model, threat analysis, and Pond setup documentation."

Write-Host "`n8. Consolidating Operations (ops/)..." -ForegroundColor Cyan
$processedFiles += Merge-FolderFiles -FolderPath "ops" -Output "$Namespace-ops-all.md" `
    -Title "Zen Garden Operations" `
    -Description "Build, deployment, release, and maintenance documentation."

Write-Host "`n9. Consolidating Architecture (architecture/)..." -ForegroundColor Cyan
$processedFiles += Merge-FolderFiles -FolderPath "architecture" -Output "$Namespace-architecture-all.md" `
    -Title "Zen Garden Architecture" `
    -Description "Design philosophy and architectural patterns."

Write-Host "`n10. Consolidating Pillars (pillars/)..." -ForegroundColor Cyan
$processedFiles += Merge-FolderFiles -FolderPath "pillars" -Output "$Namespace-pillars-all.md" `
    -Title "Zen Garden Pillars" `
    -Description "Core philosophical pillars and design principles that guide Zen Garden development."

# ============================================================================
# ROOT-LEVEL DOCS FILES (not in subfolders)
# ============================================================================

Write-Host "`n11. Processing root-level docs files..." -ForegroundColor Cyan

$docsPath = Join-Path $RootDir $SourceRoot
$rootFiles = Get-ChildItem -Path $docsPath -Filter "*.md" -File |
    Where-Object { $processedFiles -notcontains $_.FullName }

foreach ($file in $rootFiles) {
    $targetName = "$Namespace-$($file.BaseName.ToLower()).md"

    $content = Get-Content $file.FullName -Raw -Encoding UTF8
    $content = Strip-Frontmatter $content

    $targetPath = Join-Path $OutputDir $targetName
    Set-Content -Path $targetPath -Value $content -Encoding UTF8
    Write-Host "  $targetName" -ForegroundColor Green
    $processedFiles += $file.FullName
}

# ============================================================================
# SPECIAL FILES (outside docs/)
# ============================================================================

Write-Host "`n12. Processing special files..." -ForegroundColor Cyan

# Main README
$readmePath = Join-Path $RootDir "README.md"
if (Test-Path $readmePath) {
    $content = Get-Content $readmePath -Raw -Encoding UTF8
    Set-Content -Path (Join-Path $OutputDir "$Namespace-readme.md") -Value $content -Encoding UTF8
    Write-Host "  $Namespace-readme.md" -ForegroundColor Green
}

# Rake changelog
$changelogPath = Join-Path $RootDir "src/rake/CHANGELOG.md"
if (Test-Path $changelogPath) {
    $content = Get-Content $changelogPath -Raw -Encoding UTF8
    Set-Content -Path (Join-Path $OutputDir "$Namespace-changelog.md") -Value $content -Encoding UTF8
    Write-Host "  $Namespace-changelog.md" -ForegroundColor Green
}

# Tests README
$testsPath = Join-Path $RootDir "tests/README.md"
if (Test-Path $testsPath) {
    $content = Get-Content $testsPath -Raw -Encoding UTF8
    Set-Content -Path (Join-Path $OutputDir "$Namespace-tests.md") -Value $content -Encoding UTF8
    Write-Host "  $Namespace-tests.md" -ForegroundColor Green
}

# Manifests
$manifestsPath = Join-Path $RootDir "manifests"
if (Test-Path $manifestsPath) {
    $manifestFiles = Get-ChildItem -Path $manifestsPath -Filter "*.md" -File
    if ($manifestFiles.Count -gt 0) {
        $dateStr = Get-Date -Format "MMMM dd, yyyy"
        $manifestContent = "# Zen Garden Service Manifests`n`n**Service template manifests and compatibility information.**`n`n**Consolidated from:** $($manifestFiles.Count) files`n**Last Updated:** $dateStr`n`n---`n"
        foreach ($file in $manifestFiles) {
            $fileContent = Get-Content $file.FullName -Raw -Encoding UTF8
            $title = Extract-Title $fileContent $file.BaseName
            $manifestContent += "`n## $title`n`n$fileContent`n`n---`n"
        }
        Set-Content -Path (Join-Path $OutputDir "$Namespace-manifests.md") -Value $manifestContent -Encoding UTF8
        Write-Host "  $Namespace-manifests.md" -ForegroundColor Green
    }
}

# Installer docs
$installerPath = Join-Path $RootDir "installer"
if (Test-Path $installerPath) {
    $installerFiles = Get-ChildItem -Path $installerPath -Filter "*.md" -File -Recurse
    if ($installerFiles.Count -gt 0) {
        $dateStr = Get-Date -Format "MMMM dd, yyyy"
        $installerContent = "# Zen Garden Installer Documentation`n`n**Installation, branding, and deployment documentation.**`n`n**Last Updated:** $dateStr`n`n---`n"
        foreach ($file in $installerFiles) {
            $fileContent = Get-Content $file.FullName -Raw -Encoding UTF8
            $title = Extract-Title $fileContent $file.BaseName
            $relativePath = $file.FullName.Substring($installerPath.Length).TrimStart('\', '/')
            $installerContent += "`n## $title`n`n*Source: $relativePath*`n`n$fileContent`n`n---`n"
        }
        Set-Content -Path (Join-Path $OutputDir "$Namespace-installer.md") -Value $installerContent -Encoding UTF8
        Write-Host "  $Namespace-installer.md ($($installerFiles.Count) files)" -ForegroundColor Green
    }
}

# ============================================================================
# METADATA FILES
# ============================================================================

Write-Host "`n13. Generating distribution metadata..." -ForegroundColor Cyan

$files = Get-ChildItem -Path $OutputDir -Filter "*.md" | Sort-Object Name

# Distribution README
$dateStr = Get-Date -Format "MMMM dd, yyyy HH:mm"
$distReadme = @"
# Zen Garden AI Distribution

**Optimized documentation set for AI ingestion and understanding.**

## Overview

This distribution contains the complete Zen Garden documentation consolidated by category
for easy AI consumption. All .md files from docs/ are automatically included.

## Quick Start

1. Start here: zen-garden-readme.md - Main project overview
2. Learn terms: zen-garden-glossary.md - Essential terminology
3. Understand concepts: zen-garden-concepts.md - Core architecture
4. Deep dive: zen-garden-specs-all.md - Technical specifications

## File Naming Convention

All files follow the pattern: zen-garden-[category].md

Categories:
- readme - Main project overview
- glossary - Terminology reference
- concepts - Core concepts and architecture
- guides-all - All operational guides
- reference-all - API and configuration reference
- specs-all - Technical specifications
- security-all - Security model and threat analysis
- ops-all - Operations, releases, and build guides
- decisions-all - All architectural decision records (ADRs)
- proposals-all - All feature proposals
- architecture-all - Design philosophy
- pillars-all - Core philosophical pillars

## File Count

Total: $($files.Count) files

## Generated

$dateStr

## Maintenance

This distribution is generated automatically. To update:

    .\scripts\AiDocDist.ps1 -Validate
"@

Set-Content -Path (Join-Path $OutputDir "$Namespace-distribution-readme.md") -Value $distReadme -Encoding UTF8
Write-Host "  $Namespace-distribution-readme.md" -ForegroundColor Green

# Distribution Index
$dateStr = Get-Date -Format "MMMM dd, yyyy"
$distIndex = "# Zen Garden Distribution Index`n`n**Quick reference for all files in this distribution.**`n`n**Generated:** $dateStr`n`n---`n`n## File List`n`n"

foreach ($file in $files) {
    $size = [math]::Round($file.Length / 1KB, 1)
    $name = $file.Name

    $firstLines = Get-Content $file.FullName -First 5 -Encoding UTF8
    $desc = "Documentation file"
    foreach ($line in $firstLines) {
        if ($line -match '^#\s+(.+)$') {
            $desc = $Matches[1]
            break
        }
    }

    $distIndex += "- **$name** ($size KB) - $desc`n"
}

$totalSize = ($files | Measure-Object -Property Length -Sum).Sum / 1MB
$distIndex += "`n---`n`n**Total Size:** $([math]::Round($totalSize, 2)) MB across $($files.Count) files"

Set-Content -Path (Join-Path $OutputDir "$Namespace-distribution-index.md") -Value $distIndex -Encoding UTF8
Write-Host "  $Namespace-distribution-index.md" -ForegroundColor Green

# ============================================================================
# VALIDATION
# ============================================================================

if ($Validate) {
    Write-Host "`n=== Validation ===" -ForegroundColor Cyan

    $finalFiles = Get-ChildItem -Path $OutputDir -Filter "*.md"
    $count = $finalFiles.Count

    Write-Host "`nFile count: $count" -ForegroundColor Green

    # Check naming
    $nonCompliant = $finalFiles | Where-Object { $_.Name -notmatch "^$Namespace-" }
    if ($nonCompliant) {
        Write-Warning "Files not following namespace convention:"
        $nonCompliant | ForEach-Object { Write-Warning "  $_" }
    }
    else {
        Write-Host "All files use namespace prefix" -ForegroundColor Green
    }

    # Size check
    $totalSize = ($finalFiles | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host "Total size: $([math]::Round($totalSize, 2)) MB" -ForegroundColor Green

    # List all source files found
    Write-Host "`nSource coverage:" -ForegroundColor Cyan
    $allSourceFiles = Get-ChildItem -Path (Join-Path $RootDir $SourceRoot) -Filter "*.md" -File -Recurse
    Write-Host "  Found $($allSourceFiles.Count) .md files in docs/"
    Write-Host "  Generated $count distribution files"

    Write-Host "`nValidation complete!" -ForegroundColor Green
}

Write-Host "`n=== Complete ===" -ForegroundColor Cyan
Write-Host "AI distribution generated in: $OutputDir"
$finalCount = (Get-ChildItem -Path $OutputDir -Filter "*.md").Count
Write-Host "Total files: $finalCount"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Review generated files in $OutputDir"
Write-Host "2. Test with AI agent (ingest all files)"
Write-Host "3. Validate comprehension and completeness"
Write-Host ""
