param(
    [string]$DocsPath = "docs",
    [string]$WebsitePath = "other/website",
    [string]$ReadmePath = "README.md",
    [string]$Base = "",                 # git ref: validate only instructional docs changed vs this ref (+ uncommitted). Empty = full sweep.
    [string[]]$Files = @(),             # explicit .md paths to validate (overrides -Base/-Full)
    [switch]$Full,                      # validate ALL instructional docs (the manual full-surface sweep)
    [switch]$Fix,
    [switch]$Verbose,
    [string]$FrameworkVersion = "net10.0",
    [string]$TempDir = "artifacts/code-validation"
)

# Leg C of the green ratchet (docs/architecture/foundation-consolidation-plan.md).
# Only INSTRUCTIONAL docs are in scope; decision/design/proposal/archive docs legitimately contain
# aspirational or historical code and are out of scope by design.
#
# OPT-IN model: within those docs, a C# block is compiled ONLY if it is marked `<!-- validate -->`
# (a complete, self-contained example the author asserts must compile). Everything else is prose-grade
# and is not compiled — documentation is teaching material, mostly fragments, so "compile every block"
# is the wrong default. The marked set is the gate's real signal: those examples must never go stale.
$script:InstructionalRoots = @(
    'docs/guides', 'docs/how-to', 'docs/reference', 'docs/getting-started',
    'docs/examples', 'docs/workbooks', 'docs/patterns', 'docs/api',
    '.claude/skills'   # DX-0048: each skill's canonical pattern is a `<!-- validate -->` block.
)
function Test-Instructional {
    param([string]$RelativePath)
    $rp = ($RelativePath -replace '\\', '/')
    if ($rp -ieq 'README.md') { return $true }
    foreach ($r in $script:InstructionalRoots) { if ($rp -like "$r/*") { return $true } }
    return $false
}

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
        # OPT-IN (documentation is prose-first): a C# block is compile-validated ONLY when an author marks
        # it a complete, self-contained example with `<!-- validate -->` on the line(s) just before the
        # fence. Unmarked blocks are illustrative by default — fragments, partial snippets, multi-pillar
        # montages, snippets that reference types defined in prose — and are NOT compiled. Trying to compile
        # every teaching fragment as a standalone program is a category error (it is what made the old gate
        # validate nothing reliably). Legacy `<!-- validate:skip -->` markers are now redundant and ignored.
        $preStart = [Math]::Max(0, $match.Index - 120)
        $preceding = $content.Substring($preStart, $match.Index - $preStart)
        if ($preceding -notmatch '<!--\s*validate\s*-->') { continue }
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

# Curated using set that a real Koan app would have on hand. Every entry MUST resolve to a real
# namespace in the temp project's referenced assemblies (Koan.Core/Data.Core/Data.Abstractions/Web/AI
# + Microsoft.AspNetCore.App) — a using for a non-existent namespace is itself a CS0246 that would fail
# every snippet. This removes the false-failure class where a correct snippet just lacked a using
# (EntityContext, DI, the host builder) that the doc reader obviously has. It does NOT mask genuine
# staleness: a removed pillar (e.g. the old Koan.Flow) has no namespace here, so its references still fail.
$script:RichUsings = @(
    'using System;'
    'using System.Collections.Generic;'
    'using System.Linq;'
    'using System.Threading;'
    'using System.Threading.Tasks;'
    'using Microsoft.AspNetCore.Builder;'
    'using Microsoft.AspNetCore.Http;'
    'using Microsoft.AspNetCore.Mvc;'
    'using Microsoft.Extensions.Configuration;'
    'using Microsoft.Extensions.DependencyInjection;'
    'using Microsoft.Extensions.Hosting;'
    'using Microsoft.Extensions.Logging;'
    'using Koan.Core;'
    'using Koan.Core.Capabilities;'
    'using Koan.Data;'
    'using Koan.Data.Abstractions;'
    'using Koan.Data.Abstractions.Capabilities;'
    'using Koan.Data.Core;'
    'using Koan.Data.Core.Model;'   # Entity<T>/Entity<T,TKey> — the base class nearly every entity example uses
    'using Koan.Web;'
    'using Koan.Web.Controllers;'   # EntityController<T> — the documented REST base controller
)

# Pull the snippet's own leading using-directives out of the body so they can sit at file scope
# (a using inside a wrapper method/class is a compile error) and be deduped against the rich set.
function Split-Usings {
    param([string]$Code)
    $usings = New-Object System.Collections.Generic.List[string]
    $bodyLines = New-Object System.Collections.Generic.List[string]
    foreach ($line in ($Code -split "`r?`n")) {
        if ($line -match '^\s*(global\s+)?using\s+(static\s+)?[\w\.]+\s*(=\s*[\w\.<>,\s]+)?;\s*$') {
            $usings.Add(($line.Trim())) | Out-Null
        } else {
            $bodyLines.Add($line) | Out-Null
        }
    }
    return [PSCustomObject]@{ Usings = $usings; Body = ($bodyLines -join "`n") }
}

# top-level (host entrypoint) | declaration (its own namespace/type) | fragment (statements).
function Get-BlockKind {
    param([string]$Code)
    if ($Code -match '\bWebApplication\s*\.\s*Create' -or $Code -match '\bHost\s*\.\s*CreateApplicationBuilder\b') {
        return 'toplevel'
    }
    if ($Code -match '(?m)^\s*namespace\s+\w' -or
        $Code -match '(?m)^\s*(\[[^\]]*\]\s*)*((public|internal|sealed|abstract|static|partial|file)\s+)*(class|record|struct|interface|enum)\s+\w') {
        return 'declaration'
    }
    return 'fragment'
}

function Test-CodeBlock {
    param(
        [PSCustomObject]$Block,
        [string]$TempProjectDir
    )

    try {
        # Create a test file
        $testFile = Join-Path $TempProjectDir "TestCode$([System.IO.Path]::GetRandomFileName().Replace('.', '')).cs"

        $split = Split-Usings -Code $Block.Code
        $kind = Get-BlockKind -Code $Block.Code
        $body = $split.Body

        # Dedup the curated set with the snippet's own usings (preserve order, rich first).
        $allUsings = [System.Collections.Generic.List[string]]::new()
        foreach ($u in $script:RichUsings) { if (-not $allUsings.Contains($u)) { $allUsings.Add($u) | Out-Null } }
        foreach ($u in $split.Usings) { if (-not $allUsings.Contains($u)) { $allUsings.Add($u) | Out-Null } }
        $usingBlock = ($allUsings -join "`n")

        switch ($kind) {
            'declaration' {
                # Body already declares its own namespace/types; just give it the usings.
                $wrappedCode = "$usingBlock`n`n$body"
            }
            'toplevel' {
                # Host-entrypoint snippet: wrap in Main(args) so `args` + the builder/app locals resolve
                # (a method body, not true top-level statements, so it composes in the Library temp project).
                $wrappedCode = @"
$usingBlock

namespace CodeValidation
{
    public static class Program_$([System.IO.Path]::GetRandomFileName().Replace('.', ''))
    {
        public static async Task Main(string[] args)
        {
$body
        }
    }
}
"@
            }
            default {
                # Statement fragment.
                $wrappedCode = @"
$usingBlock

namespace CodeValidation
{
    public class TestCode
    {
        public async Task TestMethod()
        {
$body
        }
    }
}
"@
            }
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
    <!-- DX-0048: pillar refs so each skill's canonical pattern compiles under the gate. -->
    <ProjectReference Include="..\..\src\Koan.Jobs\Koan.Jobs.csproj" />
    <ProjectReference Include="..\..\src\Koan.Cache\Koan.Cache.csproj" />
    <ProjectReference Include="..\..\src\Koan.Data.Vector\Koan.Data.Vector.csproj" />
    <ProjectReference Include="..\..\src\Koan.Data.AI\Koan.Data.AI.csproj" />
    <ProjectReference Include="..\..\src\Koan.Storage\Koan.Storage.csproj" />
    <ProjectReference Include="..\..\src\Koan.Communication\Koan.Communication.csproj" />
    <ProjectReference Include="..\..\src\Koan.Messaging.Core\Koan.Messaging.Core.csproj" />
    <ProjectReference Include="..\..\src\Koan.Web.Auth\Koan.Web.Auth.csproj" />
    <ProjectReference Include="..\..\src\Koan.Observability\Koan.Observability.csproj" />
    <ProjectReference Include="..\..\src\Koan.Media.Core\Koan.Media.Core.csproj" />
    <ProjectReference Include="..\..\src\Koan.Mcp\Koan.Mcp.csproj" />
    <ProjectReference Include="..\..\src\Koan.Tenancy\Koan.Tenancy.csproj" />
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

    # Collect documentation files in scope (instructional surfaces only).
    $repoRootPath = $repoRoot.ProviderPath
    $candidates = New-Object System.Collections.Generic.List[string]

    if ($Files.Count -gt 0) {
        foreach ($f in $Files) { if (Test-Instructional $f) { $candidates.Add($f) | Out-Null } }
        Write-Host "Scope: $($candidates.Count) explicitly-provided instructional file(s)"
    }
    elseif ($Base -and -not $Full) {
        # Diff-scoped: docs changed vs $Base, plus uncommitted (staged + working tree).
        $changed = @()
        $changed += (& git diff --name-only "$Base...HEAD" 2>$null)
        $changed += (& git diff --name-only 2>$null)
        $changed += (& git diff --name-only --cached 2>$null)
        $changed = $changed | Where-Object { $_ -and ($_ -match '\.md$') } | Sort-Object -Unique
        foreach ($c in $changed) { if (Test-Instructional $c) { $candidates.Add($c) | Out-Null } }
        Write-Host "Scope: diff vs '$Base' -> $($candidates.Count) changed instructional doc(s)"
    }
    else {
        # Full sweep across instructional surfaces.
        if (Test-Path $DocsPath) {
            Get-ChildItem -Path $DocsPath -Recurse -Filter '*.md' | ForEach-Object {
                $rel = [System.IO.Path]::GetRelativePath($repoRootPath, $_.FullName)
                if (Test-Instructional $rel) { $candidates.Add(($rel -replace '\\', '/')) | Out-Null }
            }
        }
        if (Test-Path $ReadmePath) { $candidates.Add('README.md') | Out-Null }
        # DX-0048: skill canonical-pattern blocks are in scope for the full sweep too.
        $skillsRoot = Join-Path $repoRootPath '.claude/skills'
        if (Test-Path $skillsRoot) {
            Get-ChildItem -Path $skillsRoot -Recurse -Filter '*.md' | ForEach-Object {
                $rel = [System.IO.Path]::GetRelativePath($repoRootPath, $_.FullName)
                if (Test-Instructional $rel) { $candidates.Add(($rel -replace '\\', '/')) | Out-Null }
            }
        }
        Write-Host "Scope: full instructional sweep -> $($candidates.Count) doc(s)"
    }

    # NOTE: do NOT name these locals $full / $files. PowerShell variable names are
    # case-insensitive, so $full aliases the typed [switch]$Full parameter (string -> bool
    # coercion throws a MetadataError) and $files aliases the typed [string[]]$Files
    # parameter (Get-Item FileInfo objects get flattened to strings, so .FullName is empty).
    # Either collision silently kills the sweep, degrading the gate to "validate nothing".
    $scopedFiles = @()
    foreach ($c in $candidates) {
        $fullPath = Join-Path $repoRootPath $c
        if (Test-Path $fullPath) { $scopedFiles += Get-Item $fullPath }
    }

    if ($scopedFiles.Count -eq 0) {
        Write-Success "No instructional docs in scope to validate."
        exit 0
    }

    Write-Host "Found $($scopedFiles.Count) documentation file(s) in scope"

    # Extract all code blocks
    $allBlocks = @()
    foreach ($file in $scopedFiles) {
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
        exit 0
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
    exit 0
}
finally {
    Pop-Location
}
