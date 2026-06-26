<#
.SYNOPSIS
  Lint blueprints/ against the ARCH-0094 Adapter Blueprint contract — the GROUNDING gate.

.DESCRIPTION
  Part of the green ratchet (Leg F). Blueprints are the EXTEND-a-pillar parallel to the .claude/skills
  cards (USE-a-pillar). Unlike a skill — which marks a `<!-- validate -->` usage block that is COMPILED —
  a blueprint asserts OBLIGATIONS that must trace to real shipped first-party adapter source. So this gate
  GREP-VERIFIES every citation is alive in the cited file: a blueprint cannot drift into fiction (the exact
  rot that retired skills in the 2026-06-18 audit), and it cannot teach an agent a member that no longer exists.

    ERRORS (always fatal):
      - directory leaf == frontmatter `name:`            (the loader/catalogue key)
      - frontmatter has `name:` and `description:`
      - frontmatter has the EXTEND-required fields `pillar:`, `type:`, and a non-empty `grounded-in:` list
      - no hardcoded version pins (0.6.x / Version="0.…") — packages float (NBGV)
      - every `grounded-in:` path resolves on disk (these ARE the exemplars; a dead path = ungrounded)
      - every `<!-- obligation: Type.Member @ relpath -->` : relpath resolves AND both the Type leaf and the
        Member identifier grep-hit in that file (the anti-drift core — the cited member is real and alive)

    WARNINGS (fatal under -Strict, the green-ratchet default):
      - declared `conformance:` / `card:` path does not resolve
      - a relative Markdown link does not resolve from the blueprint's directory
      - catalogue parity: the blueprint is missing from blueprints/BLUEPRINTS.md

  Exit 0 when no errors (and, under -Strict, no warnings); otherwise 1.

.EXAMPLE
  pwsh scripts/blueprint-lint.ps1
  pwsh scripts/blueprint-lint.ps1 -Strict   # promote warnings to errors (green-ratchet default)
#>
param(
    [string]$BlueprintsRoot = "blueprints",
    [switch]$Strict
)

$ErrorActionPreference = 'Stop'
Push-Location (Resolve-Path "$PSScriptRoot/..")
try {
    $repoRoot = (Get-Location).ProviderPath
    $root = Join-Path $repoRoot $BlueprintsRoot
    if (-not (Test-Path $root)) { Write-Host "blueprint-lint: no blueprints dir at $BlueprintsRoot"; exit 0 }

    $rows = New-Object System.Collections.Generic.List[object]
    function Add-Row($bp, $level, $detail) {
        $rows.Add([PSCustomObject]@{ Blueprint = $bp; Level = $level; Detail = $detail }) | Out-Null
    }

    $cataloguePath = Join-Path $root "BLUEPRINTS.md"
    $catalogue = if (Test-Path $cataloguePath) { Get-Content -Raw $cataloguePath } else { "" }

    $files = Get-ChildItem -Path $root -Recurse -Filter "BLUEPRINT.md" | Sort-Object FullName
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($repoRoot.Length + 1).Replace('\', '/')
        # Expected name = the path segments under blueprints/ (e.g. blueprints/data/sql -> data-sql), so the
        # name is globally unique AND traces to its location (the loader/catalogue key).
        $relDir = $f.Directory.FullName.Substring($root.Length).Trim('\', '/').Replace('\', '/')
        $expectedName = ($relDir -split '/') -join '-'
        $raw = Get-Content -Raw $f.FullName
        $fm = if ($raw -match '(?s)^\s*---\s*\r?\n(.*?)\r?\n---') { $Matches[1] } else { "" }

        $name   = if ($fm -match '(?m)^\s*name:\s*(.+?)\s*$') { $Matches[1].Trim() } else { $null }
        $desc   = if ($fm -match '(?m)^\s*description:\s*(.+?)\s*$') { $Matches[1].Trim() } else { $null }
        $pillar = if ($fm -match '(?m)^\s*pillar:\s*(.+?)\s*$') { $Matches[1].Trim() } else { $null }
        $type   = if ($fm -match '(?m)^\s*type:\s*(.+?)\s*$') { $Matches[1].Trim() } else { $null }

        if (-not $name) { Add-Row $rel 'ERROR' "frontmatter missing 'name:'" }
        elseif ($name -ne $expectedName) { Add-Row $rel 'ERROR' "name != path ('$name' vs expected '$expectedName' from blueprints/$relDir)" }
        if (-not $desc) { Add-Row $rel 'ERROR' "frontmatter missing 'description:'" }
        if (-not $pillar) { Add-Row $rel 'ERROR' "frontmatter missing 'pillar:' (EXTEND-required)" }
        if (-not $type) { Add-Row $rel 'ERROR' "frontmatter missing 'type:' (EXTEND-required)" }

        # grounded-in: YAML list — the shipped exemplars the obligations trace to.
        $grounded = New-Object System.Collections.Generic.List[string]
        $inGrounded = $false
        foreach ($ln in ($fm -split "\r?\n")) {
            if ($ln -match '^\s*grounded-in:\s*$') { $inGrounded = $true; continue }
            if ($inGrounded) {
                if ($ln -match '^\s*-\s*(.+?)\s*$') { $grounded.Add($Matches[1].Trim()) | Out-Null }
                elseif ($ln -match '^\S') { break }              # next top-level key ends the list
            }
        }
        if ($grounded.Count -eq 0) { Add-Row $rel 'ERROR' "frontmatter missing a non-empty 'grounded-in:' list (EXTEND-required)" }
        foreach ($g in $grounded) {
            if (-not (Test-Path (Join-Path $repoRoot $g))) { Add-Row $rel 'ERROR' "grounded-in path does not resolve: $g" }
        }

        # Version pins — packages float under NBGV; a hardcoded patch goes stale.
        foreach ($m in [regex]::Matches($raw, '(?m)(Version\s*=\s*"0\.\d+\.\d+"|\b0\.6\.\d+\b)')) {
            Add-Row $rel 'ERROR' "hardcoded version pin: $($m.Value)"
        }

        # The anti-drift core: every obligation token's cited member must be alive in the cited file.
        foreach ($m in [regex]::Matches($raw, '<!--\s*obligation:\s*([^@]+?)\s*@\s*([^>]+?)\s*-->')) {
            $sym = $m.Groups[1].Value.Trim()
            $path = $m.Groups[2].Value.Trim()
            $full = Join-Path $repoRoot $path
            if (-not (Test-Path $full)) { Add-Row $rel 'ERROR' "obligation '$sym' cites a path that does not resolve: $path"; continue }
            $src = Get-Content -Raw $full
            $dot = $sym.LastIndexOf('.')
            $typePart = if ($dot -ge 0) { $sym.Substring(0, $dot) } else { $sym }
            $typeLeaf = ($typePart -split '\.')[-1]
            $member = if ($dot -ge 0) { $sym.Substring($dot + 1) } else { $sym }
            if ($src -notmatch [regex]("\b" + [regex]::Escape($typeLeaf) + "\b")) { Add-Row $rel 'ERROR' "obligation type '$typeLeaf' not found in $path (for '$sym')" }
            if ($src -notmatch [regex]("\b" + [regex]::Escape($member) + "\b")) { Add-Row $rel 'ERROR' "obligation member '$member' not found in $path (for '$sym')" }
        }

        # Declared conformance: / card: anchors resolve (WARN).
        foreach ($key in @('conformance', 'card')) {
            if ($fm -match "(?m)^\s*${key}:\s*(.+?)\s*$") {
                $p = $Matches[1].Trim()
                if (-not (Test-Path (Join-Path $repoRoot $p))) { Add-Row $rel 'WARN' "${key}: does not resolve: $p" }
            }
        }

        # Relative markdown links resolve from the blueprint's directory (WARN).
        foreach ($lm in [regex]::Matches($raw, '\]\(([^)]+)\)')) {
            $link = $lm.Groups[1].Value.Trim()
            if ($link -match '^(https?:|mailto:|#)') { continue }
            $p = ($link -split '#')[0]
            if (-not $p) { continue }
            if (-not (Test-Path (Join-Path $f.Directory.FullName $p))) { Add-Row $rel 'WARN' "broken link: $link" }
        }

        # Catalogue parity: the blueprint name is listed in BLUEPRINTS.md (WARN).
        if ($name -and $catalogue -and ($catalogue -notmatch [regex]::Escape($name))) {
            Add-Row $rel 'WARN' "not listed in blueprints/BLUEPRINTS.md"
        }
    }

    $errors = @($rows | Where-Object { $_.Level -eq 'ERROR' })
    $warns = @($rows | Where-Object { $_.Level -eq 'WARN' })

    if ($rows.Count -gt 0) {
        $rows | Sort-Object Level, Blueprint | Format-Table -AutoSize | Out-String | Write-Host
    }
    Write-Host ("blueprint-lint: {0} blueprint(s); Errors: {1}; Warnings: {2}" -f $files.Count, $errors.Count, $warns.Count)

    if ($errors.Count -gt 0 -or ($Strict -and $warns.Count -gt 0)) { exit 1 }
    exit 0
}
finally {
    Pop-Location
}
