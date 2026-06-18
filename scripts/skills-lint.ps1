<#
.SYNOPSIS
  Lint the .claude/skills/ set against the DX-0048 skill contract.

.DESCRIPTION
  Part of the green ratchet (Leg D). Enforces the card-anchored skill contract:

    ERRORS (always fatal):
      - directory name == frontmatter `name:`   (the loader keys off the directory; a mismatch
        means CLAUDE.md / the catalog invoke a name that does not resolve)
      - frontmatter has both `name:` and `description:`
      - no hardcoded version pins (0.6.x / Version="0.…") — packages float (NBGV)

    WARNINGS (fatal only under -Strict, once the H10 overhaul is complete):
      - a declared `card:` path that does not resolve
      - a relative Markdown link that does not resolve from the skill's directory
      - catalog parity: an on-disk skill missing from .claude/skills/README.md

  Exit 0 when no errors (and, under -Strict, no warnings); otherwise 1.

.EXAMPLE
  pwsh scripts/skills-lint.ps1
  pwsh scripts/skills-lint.ps1 -Strict   # promote warnings to errors (Phase 6 onward)
#>
param(
    [string]$SkillsRoot = ".claude/skills",
    [switch]$Strict
)

$ErrorActionPreference = 'Stop'
Push-Location (Resolve-Path "$PSScriptRoot/..")
try {
    $repoRoot = (Get-Location).ProviderPath
    $skillsDir = Join-Path $repoRoot $SkillsRoot
    if (-not (Test-Path $skillsDir)) { Write-Host "skills-lint: no skills dir at $SkillsRoot"; exit 0 }

    $rows = New-Object System.Collections.Generic.List[object]
    function Add-Row($skill, $level, $detail) {
        $rows.Add([PSCustomObject]@{ Skill = $skill; Level = $level; Detail = $detail }) | Out-Null
    }

    $readmePath = Join-Path $skillsDir "README.md"
    $readme = if (Test-Path $readmePath) { Get-Content -Raw $readmePath } else { "" }

    $skillDirs = Get-ChildItem -Path $skillsDir -Directory | Sort-Object Name
    foreach ($d in $skillDirs) {
        $dir = $d.Name
        $skillFile = Join-Path $d.FullName "SKILL.md"
        if (-not (Test-Path $skillFile)) { Add-Row $dir 'ERROR' "no SKILL.md in directory"; continue }

        $raw = Get-Content -Raw $skillFile
        $fm = if ($raw -match '(?s)^\s*---\s*\r?\n(.*?)\r?\n---') { $Matches[1] } else { "" }
        $name = if ($fm -match '(?m)^\s*name:\s*(.+?)\s*$') { $Matches[1].Trim() } else { $null }
        $desc = if ($fm -match '(?m)^\s*description:\s*(.+?)\s*$') { $Matches[1].Trim() } else { $null }

        if (-not $name) { Add-Row $dir 'ERROR' "frontmatter missing 'name:'" }
        elseif ($name -ne $dir) { Add-Row $dir 'ERROR' "dir != name (frontmatter name: '$name')" }
        if (-not $desc) { Add-Row $dir 'ERROR' "frontmatter missing 'description:'" }

        # Version pins — packages float under NBGV; a hardcoded patch goes stale.
        $pinMatches = [regex]::Matches($raw, '(?m)(Version\s*=\s*"0\.\d+\.\d+"|\b0\.6\.\d+\b)')
        foreach ($m in $pinMatches) { Add-Row $dir 'ERROR' "hardcoded version pin: $($m.Value)" }

        # Declared card: anchor resolves.
        if ($fm -match '(?m)^\s*card:\s*(.+?)\s*$') {
            $card = $Matches[1].Trim()
            if (-not (Test-Path (Join-Path $repoRoot $card))) { Add-Row $dir 'WARN' "card: does not resolve: $card" }
        }

        # Relative markdown links resolve from the skill's directory.
        foreach ($lm in [regex]::Matches($raw, '\]\(([^)]+)\)')) {
            $link = $lm.Groups[1].Value.Trim()
            if ($link -match '^(https?:|mailto:|#)') { continue }
            $path = ($link -split '#')[0]
            if (-not $path) { continue }
            $resolved = Join-Path $d.FullName $path
            if (-not (Test-Path $resolved)) { Add-Row $dir 'WARN' "broken link: $link" }
        }

        # Catalog parity: the skill is listed in README.md.
        if ($readme -and ($readme -notmatch [regex]::Escape($dir))) {
            Add-Row $dir 'WARN' "not listed in skills README.md"
        }
    }

    $errors = @($rows | Where-Object { $_.Level -eq 'ERROR' })
    $warns  = @($rows | Where-Object { $_.Level -eq 'WARN' })

    if ($rows.Count -gt 0) {
        $rows | Sort-Object Level, Skill | Format-Table -AutoSize | Out-String | Write-Host
    }
    Write-Host ("skills-lint: {0} skill(s); Errors: {1}; Warnings: {2}" -f $skillDirs.Count, $errors.Count, $warns.Count)

    if ($errors.Count -gt 0 -or ($Strict -and $warns.Count -gt 0)) { exit 1 }
    exit 0
}
finally {
    Pop-Location
}
