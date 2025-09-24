param(
    [string]$DocsPath = "documentation",
    [string]$WebsitePath = "other/website",
    [string]$ReadmePath = "README.md",
    [switch]$Fix,
    [switch]$Verbose,
    [string]$FrameworkVersion = "net9.0",
    [string]$TempDir = "artifacts/code-validation"
)

$ErrorActionPreference = 'Stop'

function Write-Heading($text) {
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Write-Success($text) {
    Write-Host "✓ $text" -ForegroundColor Green
}

function Write-Warning($text) {
    Write-Host "⚠ $text" -ForegroundColor Yellow
}

function Write-Error($text) {
    Write-Host "✗ $text" -ForegroundColor Red
}

function Extract-CodeBlocks {
    param(
        [string]$FilePath,
        [string[]]$Languages = @('csharp', 'c#', 'cs')
    )

    $content = Get-Content -Path $FilePath -Raw
    $blocks = @()

    # Regex to match fenced code blocks with language specification
    $pattern = '```(?:' + ($Languages -join '|') + ')\s*\r?\n(.*?)\r?\n```'
    $matches = [regex]::Matches($content, $pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline -bor [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)

    foreach ($match in $matches) {
        $code = $match.Groups[1].Value.Trim()
        if ($code -and $code.Length -gt 10) { # Skip trivial examples
            $blocks += [PSCustomObject]@{
                File = $FilePath
                Code = $code
                LineNumber = ($content.Substring(0, $match.Index) -split "`n").Length
            }
        }
    }

    return $blocks
}

function Test-CodeBlock {
    param(
        [PSCustomObject]$Block,
        [string]$TempProjectDir
    )

    try {
        # Create a test file
        $testFile = Join-Path $TempProjectDir "TestCode$([System.IO.Path]::GetRandomFileName().Replace('.', '')).cs"

        # Wrap the code in a class if it's not already wrapped
        $wrappedCode = $Block.Code

        if (-not ($wrappedCode -match 'namespace\s+|class\s+|public\s+class\s+')) {
            $wrappedCode = @"
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Koan.Core;
using Koan.Data;
using Koan.Data.Abstractions;
using Koan.Web;

namespace CodeValidation
{
    public class TestCode
    {
        public async Task TestMethod()
        {
$($Block.Code)
        }
    }
}
"@
        }

        Set-Content -Path $testFile -Value $wrappedCode -Encoding UTF8

        # Try to compile
        $output = dotnet build $TempProjectDir --verbosity quiet --nologo 2>&1
        $success = $LASTEXITCODE -eq 0

        return [PSCustomObject]@{
            Success = $success
            Output = ($output -join "`n")
            TestFile = $testFile
        }
    }
    catch {
        return [PSCustomObject]@{
            Success = $false
            Output = $_.Exception.Message
            TestFile = $testFile
        }
    }
}

function Create-TestProject {
    param([string]$ProjectDir)

    if (Test-Path $ProjectDir) {
        Remove-Item -Recurse -Force $ProjectDir
    }

    New-Item -ItemType Directory -Path $ProjectDir -Force | Out-Null

    # Create project file with Koan references
    $projectContent = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>$FrameworkVersion</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <OutputType>Library</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Koan.Core\Koan.Core.csproj" />
    <ProjectReference Include="..\..\src\Koan.Data.Core\Koan.Data.Core.csproj" />
    <ProjectReference Include="..\..\src\Koan.Data.Abstractions\Koan.Data.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\Koan.Web\Koan.Web.csproj" />
    <ProjectReference Include="..\..\src\Koan.AI\Koan.AI.csproj" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
</Project>
"@

    Set-Content -Path (Join-Path $ProjectDir "CodeValidation.csproj") -Value $projectContent -Encoding UTF8

    return $ProjectDir
}

Push-Location (Resolve-Path "$PSScriptRoot\..")
try {
    $repoRoot = Get-Location
    Write-Heading "Koan Code Examples Validation"
    Write-Host "Repo Root: $repoRoot"
    Write-Host "Framework: $FrameworkVersion"

    # Create temp project for compilation testing
    $tempProjectDir = Join-Path $repoRoot $TempDir
    Create-TestProject -ProjectDir $tempProjectDir
    Write-Host "Test project: $tempProjectDir"

    # Collect all documentation files
    $files = @()

    # Main README
    if (Test-Path $ReadmePath) {
        $files += Get-Item $ReadmePath
    }

    # Documentation folder
    if (Test-Path $DocsPath) {
        $files += Get-ChildItem -Path $DocsPath -Recurse -Filter "*.md"
    }

    # Website content
    if (Test-Path $WebsitePath) {
        $files += Get-ChildItem -Path $WebsitePath -Recurse -Filter "*.html"
        $files += Get-ChildItem -Path $WebsitePath -Recurse -Filter "*.md"
    }

    Write-Host "Found $($files.Count) documentation files"

    # Extract all code blocks
    $allBlocks = @()
    foreach ($file in $files) {
        if ($Verbose) {
            Write-Host "Processing: $($file.FullName)" -ForegroundColor DarkGray
        }

        $blocks = Extract-CodeBlocks -FilePath $file.FullName
        if ($blocks.Count -gt 0) {
            Write-Host "  Found $($blocks.Count) code blocks in $($file.Name)" -ForegroundColor DarkGray
            $allBlocks += $blocks
        }
    }

    Write-Host "Total code blocks to validate: $($allBlocks.Count)"

    if ($allBlocks.Count -eq 0) {
        Write-Warning "No code blocks found to validate"
        return
    }

    # Validate each code block
    $results = @()
    $successCount = 0
    $errorCount = 0

    Write-Heading "Validating code examples"

    for ($i = 0; $i -lt $allBlocks.Count; $i++) {
        $block = $allBlocks[$i]
        $progress = "[$($i + 1)/$($allBlocks.Count)]"

        Write-Host "$progress Testing code from $($block.File):$($block.LineNumber)" -ForegroundColor DarkGray

        $result = Test-CodeBlock -Block $block -TempProjectDir $tempProjectDir
        $result | Add-Member -NotePropertyName "Block" -NotePropertyValue $block
        $results += $result

        if ($result.Success) {
            $successCount++
            if ($Verbose) {
                Write-Success "$progress Compilation successful"
            }
        } else {
            $errorCount++
            Write-Error "$progress Compilation failed: $($block.File):$($block.LineNumber)"
            if ($Verbose) {
                Write-Host "Code:" -ForegroundColor DarkGray
                Write-Host $block.Code -ForegroundColor DarkGray
                Write-Host "Error:" -ForegroundColor DarkGray
                Write-Host $result.Output -ForegroundColor DarkGray
            }
        }

        # Clean up test file
        if ($result.TestFile -and (Test-Path $result.TestFile)) {
            Remove-Item $result.TestFile -Force
        }
    }

    # Summary
    Write-Heading "Validation Summary"
    Write-Success "Successful: $successCount"
    if ($errorCount -gt 0) {
        Write-Error "Failed: $errorCount"
    } else {
        Write-Success "Failed: $errorCount"
    }

    # Detailed error report
    if ($errorCount -gt 0 -and -not $Verbose) {
        Write-Heading "Failed Examples"
        $failedResults = $results | Where-Object { -not $_.Success }
        foreach ($failed in $failedResults) {
            Write-Host ""
            Write-Host "File: $($failed.Block.File):$($failed.Block.LineNumber)" -ForegroundColor Yellow
            Write-Host "Error: $($failed.Output)" -ForegroundColor Red

            # Show a snippet of the problematic code
            $codeLines = $failed.Block.Code -split "`n"
            if ($codeLines.Length -le 5) {
                Write-Host "Code:" -ForegroundColor DarkGray
                Write-Host $failed.Block.Code -ForegroundColor DarkGray
            } else {
                Write-Host "Code (first 5 lines):" -ForegroundColor DarkGray
                Write-Host (($codeLines[0..4] -join "`n") + "`n...") -ForegroundColor DarkGray
            }
        }
    }

    # Generate validation report
    $reportPath = Join-Path $repoRoot "artifacts/docs/code-validation-$(Get-Date -Format 'yyyyMMdd-HHmmss').json"
    $reportDir = Split-Path $reportPath -Parent
    if (-not (Test-Path $reportDir)) {
        New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
    }

    $report = [PSCustomObject]@{
        Timestamp = Get-Date -Format "o"
        FrameworkVersion = $FrameworkVersion
        TotalBlocks = $allBlocks.Count
        SuccessCount = $successCount
        ErrorCount = $errorCount
        SuccessRate = [math]::Round(($successCount / $allBlocks.Count) * 100, 2)
        Results = $results | ForEach-Object {
            [PSCustomObject]@{
                File = $_.Block.File
                LineNumber = $_.Block.LineNumber
                Success = $_.Success
                Error = if (-not $_.Success) { $_.Output } else { $null }
                CodeSnippet = if ($_.Block.Code.Length -le 200) { $_.Block.Code } else { $_.Block.Code.Substring(0, 200) + "..." }
            }
        }
    }

    $report | ConvertTo-Json -Depth 10 | Set-Content -Path $reportPath -Encoding UTF8
    Write-Host "Validation report: $reportPath" -ForegroundColor DarkGray

    # Clean up temp project
    if (Test-Path $tempProjectDir) {
        Remove-Item -Recurse -Force $tempProjectDir
    }

    # Exit with error code if validation failed
    if ($errorCount -gt 0) {
        Write-Host ""
        Write-Error "Code validation failed with $errorCount errors"
        exit 1
    }

    Write-Host ""
    Write-Success "All code examples validated successfully!"
}
finally {
    Pop-Location
}