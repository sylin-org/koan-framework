[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PacketRoot,
    [switch]$AllowPending
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PacketRoot = (Resolve-Path -LiteralPath $PacketRoot).ProviderPath
$failures = New-Object System.Collections.Generic.List[string]

function Read-RequiredJson([string]$RelativePath) {
    $path = Join-Path $PacketRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $failures.Add("missing '$RelativePath'")
        return $null
    }
    try { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -DateKind String }
    catch { $failures.Add("invalid JSON '$RelativePath': $($_.Exception.Message)"); return $null }
}

function Normalize-Path([string]$Path) {
    if ($null -eq $Path) { return '' }
    (($Path -replace '\\', '/').TrimStart('./')).ToLowerInvariant()
}

function Test-PathOverlap([string]$Candidate, [string]$Forbidden) {
    $candidatePath = Normalize-Path $Candidate
    $forbiddenPath = Normalize-Path $Forbidden
    if (-not $candidatePath -or -not $forbiddenPath) { return $false }
    $candidatePath -eq $forbiddenPath -or $candidatePath.StartsWith($forbiddenPath.TrimEnd('/') + '/')
}

$retirement = Read-RequiredJson 'restricted/retirement.json'
$replacement = Read-RequiredJson 'rewrite/replacement.json'
if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Error "GREENFIELD: $failure" -ErrorAction Continue }
    exit 1
}

$documents = @($retirement, $replacement)
$statuses = @($documents | ForEach-Object { [string]$_.status } | Sort-Object -Unique)
if ($AllowPending -and $statuses.Count -eq 1 -and $statuses[0] -eq 'pending') {
    Write-Output "GREENFIELD PENDING provider=$($replacement.provider) schemas=2"
    exit 0
}
foreach ($document in $documents) {
    if ([int]$document.schemaVersion -ne 1) { $failures.Add('unsupported greenfield schemaVersion') }
    if ([string]$document.status -ne 'sealed') { $failures.Add("$($document.provider) document is not sealed") }
}
$providers = @($documents.provider | Sort-Object -Unique)
if ($providers.Count -ne 1 -or $providers[0] -notin @('sqlite', 'mongodb')) { $failures.Add('provider identity is inconsistent or unsupported') }
if (-not $replacement.commonBase) { $failures.Add('commonBase is missing') }
if ($replacement.startedEmpty -ne $true) { $failures.Add('replacement does not prove an empty implementation start') }

$retiredPaths = @($retirement.expected | ForEach-Object { Normalize-Path ([string]$_.path) })

$exportPaths = @($replacement.sourceExport | ForEach-Object { Normalize-Path ([string]$_.path) })
foreach ($duplicate in @($exportPaths | Group-Object | Where-Object Count -ne 1)) { $failures.Add("duplicate selected source path '$($duplicate.Name)'") }
foreach ($source in @($replacement.sourceExport)) {
    if (-not $source.path -or -not $source.sha256) { $failures.Add('source export entry lacks path or sha256'); continue }
    $path = Normalize-Path ([string]$source.path)
    foreach ($retired in $retiredPaths) {
        if (Test-PathOverlap $path $retired) { $failures.Add("source export contains retired path '$retired'") }
    }
}
foreach ($kind in @('compileItems', 'registrations')) {
    $values = @($replacement.$kind | ForEach-Object { ([string]$_).ToLowerInvariant() })
    foreach ($duplicate in @($values | Group-Object | Where-Object Count -ne 1)) { $failures.Add("duplicate $kind value '$($duplicate.Name)'") }
}
if (@($replacement.shadowPaths).Count -gt 0) { $failures.Add('replacement declares shadow/fallback paths') }
if (@($replacement.executionPaths).Count -ne 1) { $failures.Add('replacement must declare exactly one execution path') }

$partIds = @($replacement.movingParts | ForEach-Object { ([string]$_.id).ToLowerInvariant() })
foreach ($duplicate in @($partIds | Group-Object | Where-Object Count -ne 1)) { $failures.Add("duplicate moving-part id '$($duplicate.Name)'") }
foreach ($part in @($replacement.movingParts)) {
    if (-not $part.id -or -not $part.reason) { $failures.Add('moving part lacks id or reason'); continue }
    if ($part.kind -notin @('contract', 'shared-mechanics', 'hot-path')) {
        $failures.Add("moving part '$($part.id)' has unsupported reason kind '$($part.kind)'")
    }
}
if (@($replacement.sourceExport).Count -gt 0 -and @($replacement.movingParts).Count -eq 0) {
    $failures.Add('replacement source has no justified moving parts')
}

$absenceByPath = @{}
foreach ($item in @($retirement.absence)) { $absenceByPath[(Normalize-Path ([string]$item.path))] = [bool]$item.absent }
foreach ($expected in @($retirement.expected)) {
    $path = Normalize-Path ([string]$expected.path)
    if (-not $path) { $failures.Add('retirement expected entry lacks path'); continue }
    if (-not $absenceByPath.ContainsKey($path) -or -not $absenceByPath[$path]) { $failures.Add("unresolved retirement entry '$path'") }
}
$retirementReference = [IO.Path]::GetFullPath((Join-Path (Join-Path $PacketRoot 'rewrite') ([string]$replacement.retirementRef)))
if ($retirementReference -ne [IO.Path]::GetFullPath((Join-Path $PacketRoot 'restricted/retirement.json'))) {
    $failures.Add('replacement retirementRef does not resolve to the packet retirement inventory')
}

if ($failures.Count -gt 0) {
    foreach ($failure in @($failures | Sort-Object -Unique)) { Write-Error "GREENFIELD: $failure" -ErrorAction Continue }
    exit 1
}
Write-Output "GREENFIELD PASS provider=$($providers[0]) source=$(@($replacement.sourceExport).Count) parts=$(@($replacement.movingParts).Count) retired=$(@($retirement.expected).Count)"
