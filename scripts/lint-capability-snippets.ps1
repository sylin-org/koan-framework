<#
.SYNOPSIS
    Compiles capability-book code blocks classified as "compilable" in the manifest.
    The manifest (docs/capabilities/snippets.manifest.json) is produced by an LLM
    classification pass (codex) - judgment to the LLM, verification to the compiler.

.PARAMETER Mode
    feed: scratch projects restore from nuget.org like a user application.
    tree: scratch projects use ProjectReferences into src/ (validates docs against dev).
#>
[CmdletBinding()]
param(
    [ValidateSet('feed', 'tree')]
    [string] $Mode = 'tree',
    [switch] $Clean,
    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$capabilitiesRoot = Join-Path $RepoRoot 'docs/capabilities'
$manifestPath = Join-Path $capabilitiesRoot 'snippets.manifest.json'
$scratchRoot = Join-Path (Join-Path $RepoRoot 'tmp') 'snippet-lint'

if (-not (Test-Path $manifestPath)) {
    Write-Error "Manifest not found at $manifestPath. Run the codex classification pass first."
    exit 1
}

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$compilable = @($manifest.blocks | Where-Object { $_.classification -eq 'compilable' })

if ($Clean -and (Test-Path $scratchRoot)) {
    Remove-Item -Recurse -Force $scratchRoot
}

if (Test-Path $scratchRoot) {
    Get-ChildItem $scratchRoot -Directory | Remove-Item -Recurse -Force
} else {
    New-Item -ItemType Directory -Force $scratchRoot | Out-Null
}

$packages = @(
    'Sylin.Koan.App',
    'Sylin.Koan.Data.AI',
    'Sylin.Koan.Data.Hygiene',
    'Sylin.Koan.Jobs',
    'Sylin.Koan.Canon',
    'Sylin.Koan.Canon.Web',
    'Sylin.Koan.Web',
    'Sylin.Koan.Data.Connector.Sqlite',
    'Sylin.Koan.Data.Vector.Connector.InMemory',
    'Sylin.Koan.Data.Connector.InMemory',
    'Sylin.Koan.AI.Connector.Ollama',
    'Sylin.Koan.AI.Eval',
    'Sylin.Koan.AI.Models',
    'Sylin.Koan.AI.Review'
)

$treeProjects = @{
    'Sylin.Koan.App'                               = 'src/Koan.App/Koan.App.csproj'
    'Sylin.Koan.Data.AI'                           = 'src/Koan.Data.AI/Koan.Data.AI.csproj'
    'Sylin.Koan.Data.Hygiene'                      = 'src/Koan.Data.Hygiene/Koan.Data.Hygiene.csproj'
    'Sylin.Koan.Jobs'                              = 'src/Koan.Jobs/Koan.Jobs.csproj'
    'Sylin.Koan.Canon'                             = 'src/Koan.Canon/Koan.Canon.csproj'
    'Sylin.Koan.Canon.Web'                         = 'src/Koan.Canon.Web/Koan.Canon.Web.csproj'
    'Sylin.Koan.Web'                               = 'src/Koan.Web/Koan.Web.csproj'
    'Sylin.Koan.Data.Connector.Sqlite'             = 'src/Connectors/Data/Sqlite/Koan.Data.Connector.Sqlite.csproj'
    'Sylin.Koan.Data.Vector.Connector.InMemory'    = 'src/Connectors/Data/Vector/InMemory/Koan.Data.Vector.Connector.InMemory.csproj'
    'Sylin.Koan.Data.Connector.InMemory'           = 'src/Connectors/Data/InMemory/Koan.Data.Connector.InMemory.csproj'
    'Sylin.Koan.AI.Connector.Ollama'               = 'src/Connectors/AI/Ollama/Koan.AI.Connector.Ollama.csproj'
    'Sylin.Koan.AI.Eval'                           = 'src/Koan.AI.Eval/Koan.AI.Eval.csproj'
    'Sylin.Koan.AI.Models'                         = 'src/Koan.AI.Models/Koan.AI.Models.csproj'
    'Sylin.Koan.AI.Review'                         = 'src/Koan.AI.Review/Koan.AI.Review.csproj'
}

$preamble = @'
using Koan.Core;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.AI;
using Koan.Data.AI.Attributes;
using Koan.Data.Hygiene;
using Koan.Jobs;
using Koan.Canon;
using Koan.Data.Vector;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
'@

function Get-BlockContent([string] $relativePath, [int] $index) {
    $fullPath = Join-Path $capabilitiesRoot $relativePath
    $content = Get-Content -LiteralPath $fullPath -Raw
    $fences = [regex]::Matches($content, '(?s)```csharp\r?\n(.*?)```')
    if ($index -ge $fences.Count) {
        throw "Block $index not found in $relativePath (only $($fences.Count) csharp blocks). Manifest is stale."
    }
    return $fences[$index].Groups[1].Value.TrimEnd()
}

function Get-Slug([string] $relativePath, [int] $index) {
    $base = $relativePath -replace '[^a-zA-Z0-9]', '-'
    return "$base-block$index".ToLowerInvariant()
}

function New-ScratchProject([string] $directory, [string] $block, [string] $slug) {
    New-Item -ItemType Directory -Force $directory | Out-Null

    if ($Mode -eq 'tree') {
        $projectBody = ($packages | ForEach-Object { "    <ProjectReference Include=`"$(Join-Path $RepoRoot $treeProjects[$_])`" />" }) -join "`n"
    } else {
        $projectBody = ($packages | ForEach-Object { "    <PackageReference Include=`"$_`" Version=`"1.*`" />" }) -join "`n"
    }

    # compilable blocks are self-contained top-level programs: compile verbatim as the whole
    # Program.cs. Blocks that start with their own usings are already complete; blocks without
    # get the standard preamble prepended. Blocks that mix type declarations and executable
    # statements get the declarations moved after the statements (the correct top-level-program
    # shape — the doc presents class-first for readability, but C# demands statements first).

    $hasOwnUsings = $block -match '^\s*using\s+'
    $hasExecutableCode = @($block -split "`r?`n" | Where-Object { $_ -match '^\s*(await |var |return |_ =|\w+\.|if\s|foreach|for\s|while\s)' }).Count -gt 0

    # extract type declarations (including their attribute lines) from the block
    $typeLines = [System.Collections.Generic.List[string]]::new()
    $statementLines = [System.Collections.Generic.List[string]]::new()
    $usingLines = [System.Collections.Generic.List[string]]::new()
    $inType = $false
    $braceDepth = 0

    foreach ($line in ($block -split "`r?`n")) {
        if ($line -match '^\s*using\s+') {
            $usingLines.Add($line)
            continue
        }
        if ($line -match '\b(class|record|interface|enum)\s+\w+') {
            $inType = $true
            $braceDepth = 0
        }
        if ($inType) {
            $typeLines.Add($line)
            $openCount = @($line.ToCharArray() | Where-Object { $_ -eq '{' }).Count
            $closeCount = @($line.ToCharArray() | Where-Object { $_ -eq '}' }).Count
            $braceDepth += $openCount - $closeCount
            if ($braceDepth -le 0 -and $line -match '}') { $inType = false }
        } else {
            $statementLines.Add($line)
        }
    }

    if ($typeLines.Count -gt 0 -and $statementLines.Count -gt 0) {
        # mixed: statements first (in method), declarations after (outside method)
        $stmtBlock = ($statementLines | Where-Object { $_ -match '\S' }) -join "`n"
        $typeBlock = ($typeLines | Where-Object { $_ -match '\S' }) -join "`n"
        $usingBlock = if ($usingLines.Count -gt 0) { ($usingLines | ForEach-Object { $_.Trim() }) -join "`n" } else { $preamble }

        $code = @"
$usingBlock

public static class Snippet
{
    public static async System.Threading.Tasks.Task Run()
    {
$stmtBlock
    }
}

$typeBlock
"@
    } elseif ($hasOwnUsings -and $hasExecutableCode) {
        $code = @"
$block
"@
    } elseif ($hasExecutableCode) {
        $code = @"
$preamble

$block
"@
    } elseif ($typeLines.Count -gt 0 -or $block -match '\b(class|record|interface|enum)\b') {
        $code = @"
$preamble

$block

public static class Entry
{
    public static void Main() { }
}
"@
    } else {
        $code = @"
$preamble

$block
"@
    }

    Set-Content -LiteralPath (Join-Path $directory 'Program.cs') -Value $code -Encoding utf8

    $csproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
$projectBody
  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $directory "$slug.csproj") -Value $csproj -Encoding utf8
}

Write-Host "LINT|capability-book|compilable=$($compilable.Count)|mode=$Mode"

$passed = 0
$failed = 0
foreach ($entry in $compilable) {
    $slug = Get-Slug $entry.file $entry.index
    $directory = Join-Path $scratchRoot $slug
    $block = Get-BlockContent $entry.file $entry.index
    New-ScratchProject $directory $block $slug

    $output = dotnet build (Join-Path $directory "$slug.csproj") --nologo -v q 2>&1
    if ($LASTEXITCODE -eq 0) {
        $passed++
        Write-Host "PASS|$($entry.file)#$($entry.index)"
    } else {
        $failed++
        $errors = ($output | Select-String -Pattern 'error \w+' | Select-Object -First 3 | ForEach-Object { $_.Line.Trim() }) -join "`n    "
        Write-Host "FAIL|$($entry.file)#$($entry.index)"
        Write-Host "    $errors"
    }
}

Write-Host "LINT|DONE|passed=$passed|failed=$failed"
if ($failed -gt 0) { exit 1 }
