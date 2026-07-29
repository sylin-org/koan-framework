[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$ArtifactRoot,
    [ValidateSet('Initiative', 'Workspace')]
    [string]$Scope = 'Initiative',
    [string]$CheckpointName = 'dac00-current',
    [string]$GateReceiptPath,
    [switch]$SkipReplay
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $RepositoryRoot) { $RepositoryRoot = (& git rev-parse --show-toplevel).Trim() }
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).ProviderPath
if ($CheckpointName -notmatch '^[a-z0-9][a-z0-9.-]*$') {
    throw 'CheckpointName must contain only lowercase letters, digits, dots, and hyphens.'
}

$gateReceipt = $null
if ($Scope -eq 'Workspace') {
    if ([string]::IsNullOrWhiteSpace($GateReceiptPath)) {
        throw 'A Workspace checkpoint requires -GateReceiptPath and cannot seal an uncertified candidate.'
    }
    $resolvedGate = (Resolve-Path -LiteralPath $GateReceiptPath).ProviderPath
    if (-not $resolvedGate.StartsWith($RepositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The Workspace gate receipt must live inside the repository it certifies.'
    }
    $gate = Get-Content -LiteralPath $resolvedGate -Raw | ConvertFrom-Json
    $status = if ($null -ne $gate.PSObject.Properties['status']) { [string]$gate.status } else { '' }
    $gates = @($gate.gates)
    $failedGates = @($gates | Where-Object { [string]$_.outcome -notin @('pass', 'passed', 'green') })
    if ($status -notin @('pass', 'passed', 'green') -or $gates.Count -eq 0 -or $failedGates.Count -ne 0) {
        throw "Workspace checkpoint cannot seal a RED, incomplete, or empty gate receipt '$resolvedGate'."
    }
    $gateReceipt = [ordered]@{
        path = [IO.Path]::GetRelativePath($RepositoryRoot, $resolvedGate).Replace('\', '/')
        sha256 = (Get-FileHash -LiteralPath $resolvedGate -Algorithm SHA256).Hash.ToLowerInvariant()
        gates = $gates.Count
    }
}
if (-not $ArtifactRoot) { $ArtifactRoot = Join-Path $RepositoryRoot 'artifacts/data-adapter-conformance/checkpoints' }
$ArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
if (-not $ArtifactRoot.StartsWith($RepositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Checkpoint artifacts must remain inside the authorized repository workspace.'
}
New-Item -ItemType Directory -Path $ArtifactRoot -Force | Out-Null

$owned = if ($Scope -eq 'Workspace') {
    @('.')
} else {
    @(
        'docs/architecture/data-adapter-development-primer.md',
        'docs/decisions/DATA-0110-compact-data-adapter-language.md',
        'docs/decisions/toc.yml',
        'docs/initiatives/README.md',
        'docs/initiatives/data-adapter-conformance'
    )
}
$baseCommit = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
$statusLines = @(& git -C $RepositoryRoot status --porcelain=v1 --untracked-files=all)

function Normalize-Path([string]$Path) { ($Path -replace '\\', '/').Trim('"') }
function Is-Owned([string]$Path) {
    if ($Scope -eq 'Workspace') { return $true }
    $normalized = Normalize-Path $Path
    foreach ($root in $owned) {
        if ($normalized -eq $root -or $normalized.StartsWith($root.TrimEnd('/') + '/', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    $false
}
function Get-Sha([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Write-Utf8NoBom([string]$Path, [string]$Content) {
    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

$dirtyPaths = New-Object System.Collections.Generic.List[string]
foreach ($line in $statusLines) {
    if ($line.Length -lt 4) { continue }
    $path = Normalize-Path $line.Substring(3)
    if ($path -match ' -> ') { $path = ($path -split ' -> ')[-1] }
    $dirtyPaths.Add($path)
}
$includedDirty = @($dirtyPaths | Where-Object { Is-Owned $_ } | Sort-Object -Unique)
$excludedDirty = @($dirtyPaths | Where-Object { -not (Is-Owned $_) } | Sort-Object -Unique)
if ($includedDirty.Count -eq 0) { throw 'No initiative-owned changes were found to seal.' }

$patchPath = Join-Path $ArtifactRoot ($CheckpointName + '.patch')
$manifestPath = Join-Path $ArtifactRoot ($CheckpointName + '.json')
$patchLines = New-Object System.Collections.Generic.List[string]
$trackedPatch = @(& git -C $RepositoryRoot diff --binary --full-index $baseCommit -- @owned 2>$null)
if ($LASTEXITCODE -ne 0) { throw 'git diff failed while sealing tracked initiative files.' }
foreach ($line in $trackedPatch) { $patchLines.Add([string]$line) }
$untrackedOwned = @($statusLines | Where-Object { $_.StartsWith('?? ') } | ForEach-Object {
        Normalize-Path $_.Substring(3)
    } | Where-Object { Is-Owned $_ } | Sort-Object -Unique)
$stageRoot = Join-Path $ArtifactRoot ('stage-' + [guid]::NewGuid().ToString('n'))
$emptyRoot = Join-Path $stageRoot 'empty'
$newRoot = Join-Path $stageRoot 'new'
New-Item -ItemType Directory -Path $emptyRoot -Force | Out-Null
New-Item -ItemType Directory -Path $newRoot -Force | Out-Null
try {
    foreach ($relative in $untrackedOwned) {
        $destination = Join-Path $newRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $RepositoryRoot $relative) -Destination $destination
    }
    $newFilePatch = @(& git -C $stageRoot diff --no-index --binary --full-index -- empty new 2>$null)
    if ($LASTEXITCODE -ne 1) { throw 'git no-index diff failed for the staged new-file tree.' }
    foreach ($line in $newFilePatch) {
        $normalizedLine = ([string]$line).Replace('a/empty/', 'a/').Replace('b/new/', 'b/')
        $patchLines.Add($normalizedLine)
    }
}
finally {
    $resolvedStage = [IO.Path]::GetFullPath($stageRoot)
    if (-not $resolvedStage.StartsWith($ArtifactRoot, [StringComparison]::OrdinalIgnoreCase) -or $resolvedStage -eq $ArtifactRoot) {
        throw "Unsafe checkpoint staging cleanup target '$resolvedStage'."
    }
    if (Test-Path -LiteralPath $resolvedStage) { Remove-Item -LiteralPath $resolvedStage -Recurse -Force }
}
Write-Utf8NoBom $patchPath (($patchLines -join "`n") + "`n")
$deleted = @($includedDirty | Where-Object { -not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $_)) } | Sort-Object -Unique)
$changed = @($includedDirty | Where-Object { Test-Path -LiteralPath (Join-Path $RepositoryRoot $_) -PathType Leaf } | Sort-Object -Unique)
$bundlePath = Join-Path $ArtifactRoot ($CheckpointName + '.bundle.zip')
$bundleRoot = Join-Path $ArtifactRoot ('bundle-' + [guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Path $bundleRoot -Force | Out-Null
try {
    foreach ($relative in $changed) {
        $destination = Join-Path $bundleRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $RepositoryRoot $relative) -Destination $destination
    }
    if (Test-Path -LiteralPath $bundlePath) { Remove-Item -LiteralPath $bundlePath -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory($bundleRoot, $bundlePath, [IO.Compression.CompressionLevel]::Optimal, $false)
}
finally {
    $resolvedBundleRoot = [IO.Path]::GetFullPath($bundleRoot)
    if (-not $resolvedBundleRoot.StartsWith($ArtifactRoot, [StringComparison]::OrdinalIgnoreCase) -or $resolvedBundleRoot -eq $ArtifactRoot) {
        throw "Unsafe checkpoint bundle cleanup target '$resolvedBundleRoot'."
    }
    if (Test-Path -LiteralPath $resolvedBundleRoot) { Remove-Item -LiteralPath $resolvedBundleRoot -Recurse -Force }
}

$files = New-Object System.Collections.Generic.List[object]
foreach ($relative in $changed) {
    $full = Join-Path $RepositoryRoot $relative
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { throw "Checkpoint file vanished: $relative" }
    $files.Add([ordered]@{ path = $relative; sha256 = Get-Sha $full; length = (Get-Item -LiteralPath $full).Length })
}
$fingerprintText = (@($files | ForEach-Object { "$($_.path)`t$($_.sha256)`t$($_.length)" }) + @($deleted | ForEach-Object { "$_`tDELETED" })) -join "`n"
$fingerprintBytes = [Text.Encoding]::UTF8.GetBytes($fingerprintText)
$sha = [Security.Cryptography.SHA256]::Create()
try { $sourceFingerprint = ([Convert]::ToHexString($sha.ComputeHash($fingerprintBytes))).ToLowerInvariant() }
finally { $sha.Dispose() }

$replay = [ordered]@{ status = 'not-run'; root = $null; verifiedFiles = 0; verifiedDeletions = 0 }
if (-not $SkipReplay) {
    $replayRoot = Join-Path $ArtifactRoot ('replay-' + [guid]::NewGuid().ToString('n'))
    $archivePath = Join-Path $ArtifactRoot ('base-' + [guid]::NewGuid().ToString('n') + '.zip')
    New-Item -ItemType Directory -Path $replayRoot -Force | Out-Null
    try {
        & git -C $RepositoryRoot archive --format=zip --output=$archivePath $baseCommit
        if ($LASTEXITCODE -ne 0) { throw 'git archive failed during checkpoint replay.' }
        Expand-Archive -LiteralPath $archivePath -DestinationPath $replayRoot -Force
        Expand-Archive -LiteralPath $bundlePath -DestinationPath $replayRoot -Force
        foreach ($relative in $deleted) {
            $deletionTarget = [IO.Path]::GetFullPath((Join-Path $replayRoot $relative))
            if (-not $deletionTarget.StartsWith($replayRoot, [StringComparison]::OrdinalIgnoreCase) -or $deletionTarget -eq $replayRoot) {
                throw "Unsafe replay deletion target '$deletionTarget'."
            }
            if (Test-Path -LiteralPath $deletionTarget) { Remove-Item -LiteralPath $deletionTarget -Recurse -Force }
        }
        foreach ($file in $files) {
            $candidate = Join-Path $replayRoot $file.path
            if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "replay is missing '$($file.path)'" }
            if ((Get-Sha $candidate) -ne $file.sha256) { throw "replay hash mismatch for '$($file.path)'" }
        }
        foreach ($relative in $deleted) {
            if (Test-Path -LiteralPath (Join-Path $replayRoot $relative)) { throw "replay retained deleted path '$relative'" }
        }
        $replay.status = 'passed'; $replay.root = 'disposable-clean-archive'; $replay.verifiedFiles = $files.Count; $replay.verifiedDeletions = $deleted.Count
    }
    finally {
        foreach ($target in @($replayRoot, $archivePath)) {
            if (-not $target) { continue }
            $resolvedTarget = [IO.Path]::GetFullPath($target)
            if (-not $resolvedTarget.StartsWith($ArtifactRoot, [StringComparison]::OrdinalIgnoreCase) -or $resolvedTarget -eq $ArtifactRoot) {
                throw "Unsafe checkpoint cleanup target '$resolvedTarget'."
            }
            if (Test-Path -LiteralPath $resolvedTarget) { Remove-Item -LiteralPath $resolvedTarget -Recurse -Force }
        }
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    checkpoint = $CheckpointName
    scope = $Scope.ToLowerInvariant()
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    baseCommit = $baseCommit
    patch = [ordered]@{ path = $patchPath; sha256 = Get-Sha $patchPath; purpose = 'review' }
    bundle = [ordered]@{ path = $bundlePath; sha256 = Get-Sha $bundlePath; binaryCapable = $true; purpose = 'exact replay' }
    ownedRoots = $owned
    includedDirtyPaths = $includedDirty
    excludedDirtyPaths = $excludedDirty
    files = @($files | ForEach-Object { $_ })
    deleted = $deleted
    sourceFingerprint = $sourceFingerprint
    gateReceipt = $gateReceipt
    replay = $replay
    reproduction = "git archive $baseCommit; extract; overlay $bundlePath; remove deleted[]; verify files[] hashes"
    commitCreated = $false
}
$manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Output "CHECKPOINT PASS files=$($files.Count) deleted=$($deleted.Count) excludedDirty=$($excludedDirty.Count) replay=$($replay.status)"
Write-Output "CHECKPOINT_MANIFEST=$manifestPath"
Write-Output "CHECKPOINT_PATCH_SHA256=$($manifest.patch.sha256)"
Write-Output "SOURCE_FINGERPRINT=$sourceFingerprint"
