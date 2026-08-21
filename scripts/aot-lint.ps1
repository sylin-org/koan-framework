<#
.SYNOPSIS
  Reject the constructs ILC cannot compile, in the pull request that introduces them.

.DESCRIPTION
  `scripts/aot-verify.ps1` publishes and runs the real binary, and it is the authority -- but it costs
  minutes and so it runs daily rather than per-PR. That leaves a window: a change that breaks every
  AOT-published application can merge and sit in `dev` until the next morning's lane. This lint closes
  the window for the two constructs whose damage is already documented, at the cost of a grep.

  It is deliberately tiny. A static check cannot decide whether an application publishes under
  NativeAOT -- if it could, PMC-050 would not have needed a publish-and-run -- so it does not try. It
  bans two specific things that have already cost this repository real time:

    MetadataToken   ILC keeps no metadata tokens. `MemberInfo.MetadataToken` throws
                    InvalidOperationException("There is no metadata token available for the given
                    member"). Four sites used it to recover declaration order; the mapping compiler
                    adopted it on 2026-08-06 and from that day every AOT-published Koan application
                    died on the first entity it mapped, five weeks before anyone noticed (PMC-049).
                    Ask Koan.Core.Reflection.DeclarationOrder instead -- it returns the token where
                    the runtime has one, and a stable constant where it does not.

    (dynamic)       The Microsoft.CSharp runtime binder cannot dispatch under AOT. ARCH-0093 4 records
                    that the failure surfaces two ways -- a RuntimeBinderException for a generic
                    argument, and an opaque ArgumentNullException("key") from the binder's own
                    ExpressionTreeCallRewriter for a non-generic one -- which is precisely why the
                    grep, and not the error text, is the reliable tripwire.

  Both are at zero in src/ today, so this starts green and only ever fires on a reintroduction.
  Prose is not code: lines that are comments are skipped, so an ADR reference or an XML doc remark may
  still name either construct.

.PARAMETER Path
  Root to scan. Defaults to src/ -- the framework, which is what ends up inside somebody's binary.

.EXAMPLE
  pwsh scripts/aot-lint.ps1
#>
[CmdletBinding()]
param(
    [string]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Path) { $Path = Join-Path $repoRoot 'src' }
if (-not (Test-Path $Path)) { Write-Host "aot-lint: nothing to scan at $Path"; exit 0 }

# The one file allowed to ask for a metadata token: it owns the fallback every other caller goes through.
$sanctioned = @('DeclarationOrder.cs')

$rules = @(
    @{
        Name    = 'MetadataToken'
        Pattern = '\bMetadataToken\b'
        Fix     = 'Use Koan.Core.Reflection.DeclarationOrder.Of(member) - ILC keeps no metadata tokens (PMC-049).'
    },
    @{
        Name    = '(dynamic)'
        Pattern = '\(\s*dynamic\s*\)'
        Fix     = 'Call the method directly - the C# runtime binder cannot dispatch under AOT (ARCH-0093 4).'
    }
)

$violations = New-Object System.Collections.Generic.List[string]
$scanned = 0

foreach ($file in Get-ChildItem -Path $Path -Recurse -Filter *.cs -File) {
    if ($file.FullName -match '[\\/](bin|obj)[\\/]') { continue }
    if ($sanctioned -contains $file.Name) { continue }
    $scanned++

    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadAllLines($file.FullName)) {
        $lineNumber++
        $trimmed = $line.TrimStart()
        # Prose is not code. A doc comment or an ADR reference may name either construct.
        if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('*') -or $trimmed.StartsWith('/*')) { continue }

        foreach ($rule in $rules) {
            if ($line -match $rule.Pattern) {
                $relative = $file.FullName.Substring($repoRoot.Length).TrimStart('\', '/')
                $violations.Add("$relative`:$lineNumber  $($rule.Name)  $($rule.Fix)`n      $($line.Trim())")
            }
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host "aot-lint: $($violations.Count) AOT-hostile construct(s) in $scanned file(s)" -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Red }
    Write-Host ''
    Write-Host 'These compile and pass every test on the JIT, and break the published binary at runtime.' -ForegroundColor Red
    Write-Host 'scripts/aot-verify.ps1 reproduces the real failure.' -ForegroundColor Red
    exit 1
}

Write-Host "aot-lint: OK - no MetadataToken or (dynamic) in $scanned source file(s) under $Path"
exit 0
