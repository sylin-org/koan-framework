<#
.SYNOPSIS
  Verify the Koan agent skills are true, reachable, and complete.

.DESCRIPTION
  One script, four checks, each justified by a failure that is silent -- nobody notices a skill has
  gone wrong, they just get worse results:

    routing    frontmatter name and description, because a broken one disables the skill entirely
    links      relative and pinned targets resolve, because progressive disclosure failing silently
               is the worst case: the agent does not know it is missing anything
    shelf      the capability shelf matches the product actually shipped, because capability drift is
               invisible until an agent hand-rolls something the framework provides
    truth      every package identifier restores and every taught construct compiles, against the
               packages a developer installs -- not a repository ProjectReference

  Only 'truth' needs network. Use -Structure for the per-PR gate and the full run on a schedule.

  This does not invoke a model. Forward evaluation judges real responses against evals/koan/rubric.md
  and has no substitute here.

.EXAMPLE
  pwsh scripts/skills-verify.ps1 -Structure
  pwsh scripts/skills-verify.ps1 -PackageVersion 1.0.0
#>
[CmdletBinding()]
param(
    [string]$PackageVersion = '1.0.0',
    [switch]$Structure
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Push-Location (Resolve-Path "$PSScriptRoot/..")
try {
    $repoRoot = (Get-Location).ProviderPath
    $failures = [System.Collections.Generic.List[string]]::new()

    $skillsRoot = '.agents/skills'
    $expectedSkills = @('koan', 'koan-explain', 'koan-upgrade')
    $shelfPath = "$skillsRoot/koan/references/capabilities.md"
    # Product truth is the claim ledger plus the generated package inventory; there is no single
    # combined projection, so read both.
    $claimsPath = 'product/claims.json'
    $inventoryPath = 'docs/reference/package-quality.json'
    $journeyRoot = 'evals/koan/journeys'
    $legacyRoot = '.claude/skills'
    $pinnedTag = 'v1.0.0'
    # A package carrying no product claim is unassessed. It may appear on the shelf, but never as an
    # ordinary choice -- its row has to say so.
    $unassessedMarkers = @('**not assessed**', '**Not assessed**')
    # Foundation internals arrive through the bundles; a developer never adds them by hand.
    $shelfExclusions = @('Sylin.Koan.Core', 'Sylin.Koan.Data.Core', 'Sylin.Koan.ZenGarden.Contracts')

    function Fail([string]$Message) { $failures.Add($Message) | Out-Null }

    # --- distribution --------------------------------------------------------------------------
    # The skills reach a developer as a plugin. `.agents` is the plugin directory: Claude Code looks
    # for <plugin>/skills/<name>/SKILL.md, which is the layout already there, so nothing is copied
    # or moved. These checks keep the shipped catalog and the source roster from drifting apart.
    $marketplacePath = '.claude-plugin/marketplace.json'
    $pluginManifestPath = "$skillsRoot/../.claude-plugin/plugin.json"
    if (-not (Test-Path -LiteralPath $marketplacePath -PathType Leaf)) {
        Fail "marketplace catalog is missing: $marketplacePath"
    }
    elseif (-not (Test-Path -LiteralPath $pluginManifestPath -PathType Leaf)) {
        Fail "plugin manifest is missing: $pluginManifestPath"
    }
    else {
        $marketplace = Get-Content -Raw -LiteralPath $marketplacePath | ConvertFrom-Json
        $plugin = Get-Content -Raw -LiteralPath $pluginManifestPath | ConvertFrom-Json
        $entries = @($marketplace.plugins)
        if ($entries.Count -ne 1) {
            Fail "marketplace must list exactly one plugin; found $($entries.Count)"
        }
        else {
            if ($entries[0].name -ne $plugin.name) {
                Fail "marketplace entry '$($entries[0].name)' does not match plugin manifest name '$($plugin.name)'"
            }
            # The source must resolve to the directory that actually holds the skills, or an install
            # succeeds and delivers nothing.
            $sourceDir = [IO.Path]::GetFullPath((Join-Path $repoRoot ([string]$entries[0].source)))
            if ($sourceDir -ne [IO.Path]::GetFullPath((Join-Path $repoRoot (Split-Path -Parent $skillsRoot)))) {
                Fail "marketplace plugin source '$($entries[0].source)' does not point at the skill root"
            }
            if ($entries[0].version -ne $plugin.version) {
                Fail "marketplace entry version '$($entries[0].version)' does not match plugin manifest '$($plugin.version)'"
            }
        }
    }

    # --- routing -------------------------------------------------------------------------------
    $skillDirs = @()
    if (Test-Path -LiteralPath $skillsRoot -PathType Container) {
        $skillDirs = @(Get-ChildItem -LiteralPath $skillsRoot -Directory | Sort-Object Name)
    }
    $actual = @($skillDirs.Name)
    foreach ($missing in @($expectedSkills | Where-Object { $_ -notin $actual })) { Fail "skill is missing: $missing" }
    foreach ($extra in @($actual | Where-Object { $_ -notin $expectedSkills })) { Fail "unexpected skill directory: $extra" }

    foreach ($dir in $skillDirs) {
        $skillFile = Join-Path $dir.FullName 'SKILL.md'
        if (-not (Test-Path -LiteralPath $skillFile -PathType Leaf)) { Fail "$($dir.Name): SKILL.md is missing"; continue }
        $raw = Get-Content -Raw -LiteralPath $skillFile
        $fm = [regex]::Match($raw, '(?s)^---\s*\r?\n(?<body>.*?)\r?\n---(?:\r?\n|$)')
        if (-not $fm.Success) { Fail "$($dir.Name): SKILL.md has no frontmatter"; continue }
        $body = $fm.Groups['body'].Value
        $name = [regex]::Match($body, '(?m)^name:\s*(?<v>[^\r\n]+?)\s*$')
        $desc = [regex]::Match($body, '(?m)^description:\s*(?<v>[^\r\n]+?)\s*$')
        if (-not $name.Success -or $name.Groups['v'].Value.Trim('"''') -ne $dir.Name) {
            Fail "$($dir.Name): frontmatter name must match the directory"
        }
        if (-not $desc.Success -or [string]::IsNullOrWhiteSpace($desc.Groups['v'].Value)) {
            Fail "$($dir.Name): frontmatter needs a description -- it is what routes the skill"
        }
    }

    # --- links ---------------------------------------------------------------------------------
    $pinnedPrefix = "https://github.com/sylin-org/koan-framework/blob/$pinnedTag/"
    foreach ($file in @(Get-ChildItem -LiteralPath $skillsRoot -Recurse -File -Filter '*.md' -ErrorAction SilentlyContinue)) {
        $content = Get-Content -Raw -LiteralPath $file.FullName
        if ($null -eq $content) { continue }
        $rel = [IO.Path]::GetRelativePath($repoRoot, $file.FullName).Replace('\', '/')

        foreach ($m in [regex]::Matches($content, '!??\[[^\]]*\]\((?<t>[^)\r\n]+)\)')) {
            $target = $m.Groups['t'].Value.Trim()
            if ($target.StartsWith($pinnedPrefix, [StringComparison]::Ordinal)) {
                $objectPath = ($target.Substring($pinnedPrefix.Length) -split '#', 2)[0]
                & git -C $repoRoot cat-file -e "${pinnedTag}:$objectPath" 2>$null
                if ($LASTEXITCODE -ne 0) { Fail "$rel : pinned link does not resolve at ${pinnedTag}: $objectPath" }
                continue
            }
            if ($target -match '^(?:[a-zA-Z][a-zA-Z0-9+.-]*:|#)') {
                if ($target -match '^https://github\.com/sylin-org/koan-framework/blob/') {
                    Fail "$rel : repository link must use the pinned revision ${pinnedTag}: $target"
                }
                continue
            }
            $pathPart = ($target -split '#', 2)[0]
            if ([string]::IsNullOrWhiteSpace($pathPart)) { continue }
            $resolved = [IO.Path]::GetFullPath((Join-Path $file.DirectoryName $pathPart))
            if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { Fail "$rel : broken link: $target" }
        }
    }

    # --- shelf ---------------------------------------------------------------------------------
    if (-not (Test-Path -LiteralPath $shelfPath -PathType Leaf)) { Fail "capability shelf is missing: $shelfPath" }
    elseif (-not (Test-Path -LiteralPath $claimsPath -PathType Leaf)) { Fail "claim ledger is missing: $claimsPath" }
    elseif (-not (Test-Path -LiteralPath $inventoryPath -PathType Leaf)) { Fail "package inventory is missing: $inventoryPath" }
    else {
        $claims = @((Get-Content -Raw -LiteralPath $claimsPath | ConvertFrom-Json).claims)
        $shippedIds = @((Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json).packages |
            ForEach-Object { [string]$_.packageId })
        $claimedIds = @($claims | ForEach-Object { $_.packages } | ForEach-Object { [string]$_ } | Sort-Object -Unique)
        $soleIds = @($claims | Where-Object { @($_.packages).Count -eq 1 } | ForEach-Object { [string]@($_.packages)[0] })
        $shelfRaw = Get-Content -Raw -LiteralPath $shelfPath
        $shelfLines = @($shelfRaw -split '\r?\n')

        foreach ($id in $shippedIds) {
            # Only assessed pieces are required on the shelf; an unassessed package may be listed
            # deliberately, but is never owed a row.
            $selectable = ($id -like '*.Connector.*' -or $id -like '*.Adapter.*' -or $id -in $soleIds) -and
                $id -in $claimedIds -and $id -notin $shelfExclusions
            if ($selectable -and -not $shelfRaw.Contains($id, [StringComparison]::Ordinal)) {
                Fail "capability shelf omits selectable package: $id"
            }
        }

        $mentions = @([regex]::Matches($shelfRaw, 'Sylin\.Koan(?:\.[A-Za-z0-9]+)+') | ForEach-Object { $_.Value } | Sort-Object -Unique)
        foreach ($mention in $mentions) {
            if ($mention -notin $shippedIds) { Fail "capability shelf names a package outside the shipped set: $mention"; continue }
            if ($mention -in $claimedIds) { continue }
            # Prose may discuss an unassessed package freely; only rows carry disposition.
            foreach ($row in @($shelfLines | Where-Object {
                $_.TrimStart().StartsWith('|', [StringComparison]::Ordinal) -and $_.Contains($mention, [StringComparison]::Ordinal)
            })) {
                if (-not @($unassessedMarkers | Where-Object { $row.Contains($_, [StringComparison]::Ordinal) })) {
                    Fail "capability shelf row presents unassessed package '$mention' as an ordinary choice"
                }
            }
        }
    }

    # The migration is not finished until the removal ships; delete this check once it has.
    foreach ($legacy in @(Get-ChildItem -LiteralPath $legacyRoot -Recurse -File -Filter 'SKILL.md' -ErrorAction SilentlyContinue)) {
        Fail "legacy skill is still loader-discoverable: $([IO.Path]::GetRelativePath($repoRoot, $legacy.FullName).Replace('\','/'))"
    }

    # --- truth ---------------------------------------------------------------------------------
    $journeyCount = 0
    if (-not $Structure) {
        # Every identifier the shelf names must resolve, assessed or not -- a developer can install
        # any of them, and a wrong identifier is wrong regardless of maturity.
        $shelfRaw = Get-Content -Raw -LiteralPath $shelfPath
        $probeIds = @(
            [regex]::Matches($shelfRaw, 'Sylin\.Koan(?:\.[A-Za-z0-9]+)+') |
                ForEach-Object { $_.Value } | Sort-Object -Unique
        )

        Write-Host "probe: restoring $($probeIds.Count) shelf package identifier(s) at $PackageVersion"
        $probeDir = Join-Path ([IO.Path]::GetTempPath()) "koan-shelf-probe-$([Guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $probeDir -Force | Out-Null
        try {
            $refs = ($probeIds | ForEach-Object { "    <PackageReference Include=`"$_`" Version=`"$PackageVersion`" />" }) -join "`n"
            @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
$refs
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $probeDir 'ShelfProbe.csproj')
            Push-Location $probeDir
            try {
                $out = @(& dotnet restore --nologo 2>&1)
                if ($LASTEXITCODE -ne 0) {
                    Fail 'capability shelf names a package identifier that does not restore'
                    $out | Where-Object { $_ -match 'NU\d+' } | ForEach-Object { Write-Host "  $_" }
                }
            }
            finally { Pop-Location }
        }
        finally { Remove-Item -Recurse -Force -LiteralPath $probeDir -ErrorAction SilentlyContinue }

        $journeys = @(Get-ChildItem -LiteralPath $journeyRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name)
        if ($journeys.Count -eq 0) { Fail "no journeys found under $journeyRoot" }
        foreach ($journey in $journeys) {
            $project = @(Get-ChildItem -LiteralPath $journey.FullName -Filter *.csproj -File)
            if ($project.Count -ne 1) { Fail "journey '$($journey.Name)' needs exactly one project"; continue }
            Write-Host "journey: building $($journey.Name)"
            $out = @(& dotnet build $project[0].FullName -p:KoanEvalPackageVersion=$PackageVersion --nologo 2>&1)
            if ($LASTEXITCODE -ne 0) {
                Fail "journey '$($journey.Name)' does not build against published packages"
                $out | Where-Object { $_ -match ': error ' } | ForEach-Object { Write-Host "  $_" }
            }
            else { $journeyCount++ }
        }
    }

    if ($failures.Count -gt 0) {
        Write-Host ''
        Write-Host "skills-verify: FAILED with $($failures.Count) finding(s)." -ForegroundColor Red
        $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        exit 1
    }

    Write-Host ''
    if ($Structure) { Write-Host 'skills-verify: structure passed (distribution, routing, links, shelf).' }
    else { Write-Host "skills-verify: passed. Shelf identifiers restore; $journeyCount journey(s) compile against published $PackageVersion." }
    exit 0
}
finally {
    Pop-Location
}
