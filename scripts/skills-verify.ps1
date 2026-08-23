<#
.SYNOPSIS
  Verify the Koan agent skills are true, reachable, and complete.

.DESCRIPTION
  One script, six checks, each justified by a failure that is silent -- nobody notices a skill has
  gone wrong, they just get worse results:

    routing    frontmatter name and description, because a broken one disables the skill entirely
    links      relative and pinned targets resolve, because progressive disclosure failing silently
               is the worst case: the agent does not know it is missing anything
    shelf      the capability shelf matches the product actually shipped, because capability drift is
               invisible until an agent hand-rolls something the framework provides
    bootstrap  every scaffold, sample, and this repository carries a portable AGENTS.md that names no
               capability, because the skills reach one harness and a bootstrap that lists packages
               becomes a second inventory nobody remembers to update (DX-0050)
    recipes    every recipe declares what it gets you, what must already be true, and what it costs;
               every ingredient names a package that ships; the generated recipe index and connector
               matrix still match their sources -- a stale index recommends confidently and wrongly
               rather than failing (DX-0051)
    truth      every package identifier restores and every taught construct compiles, against the
               packages a developer installs -- not a repository ProjectReference

  Only 'truth' needs network. Use -Structure for the per-PR gate and the full run on a schedule.

  This does not invoke a model. Forward evaluation judges real responses against evals/koan/rubric.md
  and has no substitute here.

.EXAMPLE
  pwsh scripts/skills-verify.ps1 -Structure
  pwsh scripts/skills-verify.ps1 -PackageVersion 1.*
#>
[CmdletBinding()]
param(
    # A range, not a fixed version. Each package owns its own version, so pinning one number would
    # verify whatever that number happens to name rather than what a developer resolves today. This
    # matches the reference the template itself emits.
    [string]$PackageVersion = '1.*',
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
    # DX-0050. The capability map is a public document, not skill internals: any agent fetches it
    # directly. It is also the one repository link that must NOT be pinned -- a frozen map hides every
    # capability shipped since that tag, which is the opposite of what "what can I add?" needs. So it
    # is verified against the local tree (it must exist and ship) rather than against the tag.
    $shelfPath = 'docs/reference/capability-map.md'
    $recipeIndexPath = 'docs/recipes/index.md'
    $connectorMatrixPath = 'docs/reference/connector-matrix.md'
    # Channel documents answer "what exists now", so pinning them would hide everything shipped since
    # the tag. They track the release branch and are therefore verified against the local tree -- they
    # must exist here and ship -- rather than against an immutable revision.
    $channelDocs = @($shelfPath, $recipeIndexPath, $connectorMatrixPath,
    'docs/capabilities/index.md', 'docs/capabilities/ai.md',
    'docs/capabilities/ai/semantic-search.md', 'docs/capabilities/ai/embedding/portable.md')
    $channelPrefix = 'https://github.com/sylin-org/koan-framework/blob/main/'
    $capabilityMapUrl = "$channelPrefix$shelfPath"
    function Test-ChannelLink([string]$Url, [ref]$Reason) {
        if (-not $Url.StartsWith($channelPrefix, [StringComparison]::Ordinal)) { return $false }
        $path = $Url.Substring($channelPrefix.Length)
        if ($path -notin $channelDocs) {
            $Reason.Value = "unpinned repository link is not a channel document: $path"
            return $true
        }
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            $Reason.Value = "channel link has no local target: $path"
            return $true
        }
        $Reason.Value = $null
        return $true
    }
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

            # A scaffolded project pre-registers the marketplace and enables the plugin, so a new
            # application has the skills without anyone running a command. That reference is written
            # as a name, so renaming the marketplace or plugin would break every scaffold silently.
            $qualified = "$($entries[0].name)@$($marketplace.name)"
            foreach ($settings in @(Get-ChildItem -Path 'templates' -Recurse -File -Filter 'settings.json' -ErrorAction SilentlyContinue |
                Where-Object { $_.DirectoryName -match '\.claude$' })) {
                $rel = [IO.Path]::GetRelativePath($repoRoot, $settings.FullName).Replace('\', '/')
                $declared = Get-Content -Raw -LiteralPath $settings.FullName | ConvertFrom-Json
                $known = @($declared.extraKnownMarketplaces.PSObject.Properties.Name)
                if ($marketplace.name -notin $known) {
                    Fail "$rel does not pre-register the '$($marketplace.name)' marketplace"
                }
                if (($declared.enabledPlugins.PSObject.Properties.Name) -notcontains $qualified) {
                    Fail "$rel does not enable '$qualified'"
                }
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
    foreach ($file in @(Get-ChildItem -LiteralPath $skillsRoot -Recurse -File -Filter '*.md' -ErrorAction SilentlyContinue | Where-Object { ($_.FullName -replace '\\', '/') -notmatch '/references/generated/' })) {
        # references/generated/ holds verbatim doc snapshots (see scripts/update-agent-skills.ps1).
        # Their links are repo-relative text from the source document's location and are linted
        # there; re-linting the copy here would only flag path shifts the snapshot intentionally carries.
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
                $reason = $null
                if (Test-ChannelLink $target ([ref]$reason)) {
                    if ($reason) { Fail "$rel : $reason" }
                    continue
                }
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

        # A capability row is an outcome, not a package: one outcome can need several packages. Without
        # the companion column an agent installs the one named package and ships something that composes
        # and does nothing, so a table that drops the column is a defect, not a formatting choice.
        # Keyed on the Package column so the prose decision-aid tables (which compare choices rather
        # than name pieces) are not swept in.
        foreach ($header in @($shelfLines | Where-Object { $_ -match '^\|\s*(Outcome|Store)\s*\|\s*Package\s*\|' })) {
            if ($header -notmatch '\|\s*Also needs\s*\|') {
                Fail "capability map has a table without an 'Also needs' column: $($header.Trim())"
            }
        }

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

    # --- recipes -------------------------------------------------------------------------------
    # DX-0051. Recipes are keyed on what a person asks for; the index is generated from their
    # frontmatter and is the one fetch an agent makes for a vague request. Two things rot silently
    # here: an ingredient naming a package that no longer ships, and an index that stopped matching
    # the recipes. Both produce confident, wrong recommendations rather than a visible failure.
    $recipeRoot = 'docs/recipes'
    $recipeRequired = @('type', 'recipe', 'title', 'gets_you', 'works_if', 'costs')
    if (-not (Test-Path -LiteralPath $recipeRoot -PathType Container)) {
        Fail "recipe directory is missing: $recipeRoot"
    }
    else {
        $recipeShipped = @()
        if (Test-Path -LiteralPath $inventoryPath -PathType Leaf) {
            $recipeShipped = @((Get-Content -Raw -LiteralPath $inventoryPath | ConvertFrom-Json).packages |
                ForEach-Object { [string]$_.packageId })
        }

        $recipeFiles = @(Get-ChildItem -LiteralPath $recipeRoot -File -Filter '*.md' |
            Where-Object { $_.Name -ne 'index.md' } | Sort-Object Name)
        if ($recipeFiles.Count -eq 0) { Fail "no recipes found under $recipeRoot" }

        foreach ($recipe in $recipeFiles) {
            $rel = [IO.Path]::GetRelativePath($repoRoot, $recipe.FullName).Replace('\', '/')
            $raw = Get-Content -Raw -LiteralPath $recipe.FullName
            $fm = [regex]::Match($raw, '(?s)^---\s*\r?\n(?<body>.*?)\r?\n---\s*(\r?\n|$)')
            if (-not $fm.Success) { Fail "$rel : no frontmatter"; continue }
            $body = $fm.Groups['body'].Value

            foreach ($key in $recipeRequired) {
                if ($body -notmatch "(?m)^$key\s*:\s*\S") { Fail "$rel : frontmatter is missing '$key'" }
            }
            # The index cannot help an agent decide without these, so an empty one is a silent defect.
            if ($body -notmatch '(?m)^ingredients\s*:') { Fail "$rel : frontmatter is missing 'ingredients'" }

            # Every identifier an ingredient names must be a package that actually ships.
            foreach ($named in @([regex]::Matches($body, 'Sylin\.Koan(?:\.[A-Za-z0-9]+)+') |
                ForEach-Object { $_.Value } | Sort-Object -Unique)) {
                if ($recipeShipped.Count -gt 0 -and $named -notin $recipeShipped) {
                    Fail "$rel : ingredient names a package outside the shipped set: $named"
                }
            }

            # An absent ingredient without today's alternative is a dead end, which is the failure this
            # whole decision exists to prevent.
            $absentBlock = [regex]::Match($body, '(?s)(?m)^absent:\s*\r?\n(?<items>(?:\s+-.*\r?\n?)+)')
            if ($absentBlock.Success) {
                foreach ($item in @($absentBlock.Groups['items'].Value -split '\r?\n' |
                    Where-Object { $_ -match '^\s+-' })) {
                    if (@($item -split '\s*\|\s*').Count -lt 3) {
                        Fail "$rel : absent ingredient must name today's alternative: $($item.Trim())"
                    }
                }
            }

            foreach ($m in [regex]::Matches($raw, '\[[^\]]*\]\((?<t>[^)\r\n]+)\)')) {
                $target = $m.Groups['t'].Value.Trim()
                if ($target -match '^(?:[a-zA-Z][a-zA-Z0-9+.-]*:|#)') { continue }
                $pathPart = ($target -split '#', 2)[0]
                if ([string]::IsNullOrWhiteSpace($pathPart)) { continue }
                if (-not (Test-Path -LiteralPath ([IO.Path]::GetFullPath((Join-Path $recipe.DirectoryName $pathPart))))) {
                    Fail "$rel : broken link: $target"
                }
            }
        }

        # A generated artifact that is edited by hand is a lie with a timestamp.
        & "$repoRoot/scripts/build-recipe-index.ps1" -Check *> $null
        if ($LASTEXITCODE -ne 0) {
            Fail "docs/recipes/index.md is stale -- run: pwsh scripts/build-recipe-index.ps1"
        }

        # The connector matrix is the cheap "can Koan talk to X?" fetch, derived from the evaluated
        # package graph. A new connector that never appears there is invisible to anyone asking that
        # question, so drift is a failure rather than a formatting nit.
        & "$repoRoot/scripts/build-connector-matrix.ps1" -Check *> $null
        if ($LASTEXITCODE -ne 0) {
            Fail "$connectorMatrixPath is stale -- run: pwsh scripts/build-connector-matrix.ps1"
        }
    }

    # --- bootstrap -----------------------------------------------------------------------------
    # DX-0050. The skills reach one harness; AGENTS.md is the portable floor beneath them, and it is
    # a router rather than a catalog. Naming a capability in a bootstrap would create a second
    # inventory that rots on every release, so a package identifier there is a failure by itself --
    # the property is cheap to state and impossible to keep by review alone.
    $bootstrapName = 'AGENTS.md'
    $retrievalMap = 'llms.txt'
    $bootstraps = [System.Collections.Generic.List[string]]::new()
    $bootstraps.Add($bootstrapName) | Out-Null

    # The scaffold roster comes from the template configs that actually ship, so it cannot drift from
    # the package.
    foreach ($config in @(Get-ChildItem -Path 'templates' -Recurse -File -Filter 'template.json' -ErrorAction SilentlyContinue |
        Where-Object { $_.DirectoryName -match '\.template\.config$' -and $_.FullName -notmatch '[/\\](bin|obj)[/\\]' })) {
        $templateRoot = Split-Path -Parent $config.DirectoryName
        $templateRel = [IO.Path]::GetRelativePath($repoRoot, $templateRoot).Replace('\', '/')
        $bootstraps.Add("$templateRel/$bootstrapName") | Out-Null

        # A file nobody is told about is a file nobody reads: creation must name it.
        $templateJson = Get-Content -Raw -LiteralPath $config.FullName | ConvertFrom-Json
        $announced = $false
        if ($templateJson.PSObject.Properties.Name -contains 'postActions') {
            foreach ($action in @($templateJson.postActions)) {
                foreach ($instruction in @($action.manualInstructions)) {
                    if ([string]$instruction.text -match [regex]::Escape($bootstrapName)) { $announced = $true }
                }
            }
        }
        if (-not $announced) { Fail "$templateRel does not announce $bootstrapName after creation" }
    }

    # A graduated sample is one that both builds and documents itself. The stack cards send developers
    # to these as the working version of a composition, so each must reach the same bootstrap.
    foreach ($project in @(Get-ChildItem -Path 'samples' -Recurse -File -Filter '*.csproj' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[/\\](archive|bin|obj)[/\\]' })) {
        $sampleReadme = Join-Path $project.DirectoryName 'README.md'
        if (-not (Test-Path -LiteralPath $sampleReadme -PathType Leaf)) { continue }
        $sampleRel = [IO.Path]::GetRelativePath($repoRoot, $sampleReadme).Replace('\', '/')
        $sampleText = Get-Content -Raw -LiteralPath $sampleReadme
        $pointers = @([regex]::Matches($sampleText, '\[[^\]]*\]\((?<t>[^)\r\n]*AGENTS\.md)\)'))
        if ($pointers.Count -eq 0) {
            Fail "$sampleRel does not point a coding agent at $bootstrapName"
            continue
        }
        foreach ($pointer in $pointers) {
            $resolved = [IO.Path]::GetFullPath((Join-Path $project.DirectoryName $pointer.Groups['t'].Value))
            if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
                Fail "$sampleRel : bootstrap pointer does not resolve: $($pointer.Groups['t'].Value)"
            }
        }
    }

    foreach ($bootstrap in $bootstraps) {
        if (-not (Test-Path -LiteralPath $bootstrap -PathType Leaf)) { Fail "bootstrap is missing: $bootstrap"; continue }
        $bootstrapText = Get-Content -Raw -LiteralPath $bootstrap
        $bootstrapDir = Split-Path -Parent ([IO.Path]::GetFullPath((Join-Path $repoRoot $bootstrap)))

        foreach ($named in @([regex]::Matches($bootstrapText, 'Sylin\.Koan(?:\.[A-Za-z0-9]+)*') |
            ForEach-Object { $_.Value } | Sort-Object -Unique)) {
            Fail "$bootstrap names a package ('$named'); a bootstrap routes to the capability map instead"
        }

        # The map is the primary route -- outcome to package to recipe in one hop. Matching the file
        # name covers both the in-repo relative link and the fetchable URL a scaffold carries.
        if ($bootstrapText -notmatch 'capability-map\.md') {
            Fail "$bootstrap does not link the capability map"
        }
        # A stated need routes to the map; a vague one has to reach the recipe index or the agent
        # answers "add AI" with a package name (DX-0051).
        if ($bootstrapText -notmatch 'recipes/index\.md') {
            Fail "$bootstrap does not link the recipe index"
        }

        if ($bootstrapText -notmatch [regex]::Escape($retrievalMap)) {
            Fail "$bootstrap does not link the agent retrieval map ($retrievalMap)"
        }

        # Repository links must be pinned and must resolve at the tag. Matched over raw text so an
        # autolink is covered as well as a markdown link. The capability map is the one deliberate
        # exception: it tracks the release branch, so it is checked against the local tree instead.
        foreach ($m in [regex]::Matches($bootstrapText, 'https://github\.com/sylin-org/koan-framework/blob/(?<rev>[^/\s>)]+)/(?<p>[^\s>)]+)')) {
            $reason = $null
            if (Test-ChannelLink $m.Value ([ref]$reason)) {
                if ($reason) { Fail "$bootstrap : $reason" }
                continue
            }
            if ($m.Groups['rev'].Value -ne $pinnedTag) {
                Fail "$bootstrap : repository link must use the pinned revision ${pinnedTag}: $($m.Value)"
                continue
            }
            $objectPath = (($m.Groups['p'].Value -split '#', 2)[0]).TrimEnd('.', ',')
            & git -C $repoRoot cat-file -e "${pinnedTag}:$objectPath" 2>$null
            if ($LASTEXITCODE -ne 0) { Fail "$bootstrap : pinned link does not resolve at ${pinnedTag}: $objectPath" }
        }

        foreach ($m in [regex]::Matches($bootstrapText, '\[[^\]]*\]\((?<t>[^)\r\n]+)\)')) {
            $target = $m.Groups['t'].Value.Trim()
            if ($target -match '^(?:[a-zA-Z][a-zA-Z0-9+.-]*:|#)') { continue }
            $pathPart = ($target -split '#', 2)[0]
            if ([string]::IsNullOrWhiteSpace($pathPart)) { continue }
            $resolved = [IO.Path]::GetFullPath((Join-Path $bootstrapDir $pathPart))
            if (-not (Test-Path -LiteralPath $resolved)) { Fail "$bootstrap : broken link: $target" }
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
    if ($Structure) { Write-Host 'skills-verify: structure passed (distribution, routing, links, shelf, recipes, bootstrap).' }
    else { Write-Host "skills-verify: passed. Shelf identifiers restore; $journeyCount journey(s) compile against published $PackageVersion." }
    exit 0
}
finally {
    Pop-Location
}
