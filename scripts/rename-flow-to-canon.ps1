param(
    [string]$Root = $null
)

$ErrorActionPreference = "Stop"

if (-not $Root) {
    $Root = Resolve-Path (Join-Path $PSScriptRoot "..")
}

$Root = (Resolve-Path $Root).ProviderPath

function Write-Step([string]$message) {
    Write-Host "[rename] $message" -ForegroundColor Cyan
}

function ShouldSkip([string]$path, [string[]]$exclude) {
    if ([string]::IsNullOrWhiteSpace($path)) { return $false }
    $separator = [regex]::Escape([System.IO.Path]::DirectorySeparatorChar)
    $segments = $path -split $separator
    foreach ($segment in $segments) {
        if ($exclude -contains $segment) {
            return $true
        }
    }
    return $false
}

$excludeDirs = @(
    '.git', 'bin', 'obj', 'artifacts', 'node_modules', '.vs', '.idea', 'packages', 'TestResults', 'logs', '.dccache', '.angular'
)

$pathMap = [ordered]@{
    'src/Koan.Canon.Core'         = 'src/Koan.Canon.Core'
    'src/Koan.Canon.Web'          = 'src/Koan.Canon.Web'
    'src/Koan.Canon.Runtime.Dapr' = 'src/Koan.Canon.Runtime.Dapr'
    'src/Koan.Canon.RabbitMq'     = 'src/Koan.Canon.RabbitMq'
    'tests/Koan.Canon.Core.Tests' = 'tests/Koan.Canon.Core.Tests'
    'docs/reference/flow'         = 'docs/reference/canon'
    'samples/S8.Canon'            = 'samples/S8.Canon'
}

foreach ($entry in $pathMap.GetEnumerator() | Sort-Object { $_.Key.Length } -Descending) {
    $source = Join-Path $Root $entry.Key
    if (-not (Test-Path $source)) {
        Write-Step "skip ${($entry.Key)} (not found)"
        continue
    }
    $destination = Join-Path $Root $entry.Value
    $destParent = Split-Path $destination -Parent
    if (-not (Test-Path $destParent)) {
        New-Item -ItemType Directory -Path $destParent | Out-Null
    }
    Write-Step ("move {0} -> {1}" -f $entry.Key, $entry.Value)
    Move-Item -Path $source -Destination $destination
}

# After top-level moves, ensure nested directories/files containing Koan.Canon are renamed.
$directoryMatches = Get-ChildItem -Path $Root -Recurse -Directory -ErrorAction SilentlyContinue |
Where-Object { $_.Name -like '*Koan.Canon*' -and -not (ShouldSkip $_.FullName $excludeDirs) }
foreach ($dir in $directoryMatches | Sort-Object { $_.FullName.Length } -Descending) {
    $newName = $dir.Name -replace 'Koan.Canon', 'Koan.Canon'
    if ($newName -ne $dir.Name) {
        Write-Step "rename directory ${($dir.FullName.Substring($Root.Length+1))} -> $newName"
        Rename-Item -Path $dir.FullName -NewName $newName
    }
}

$fileMatches = Get-ChildItem -Path $Root -Recurse -File -Filter '*Koan.Canon*' -ErrorAction SilentlyContinue |
Where-Object { -not (ShouldSkip $_.DirectoryName $excludeDirs) }
foreach ($file in $fileMatches) {
    $newName = $file.Name -replace 'Koan.Canon', 'Koan.Canon'
    if ($newName -ne $file.Name) {
        $newPath = Join-Path $file.DirectoryName $newName
        Write-Step "rename file ${($file.FullName.Substring($Root.Length+1))} -> $newName"
        Move-Item -Path $file.FullName -Destination $newPath
    }
}

# Additional directory renames for sample sub-folders containing S8.Canon pattern.
$sampleDirs = Get-ChildItem -Path (Join-Path $Root 'samples/S8.Canon') -Directory -ErrorAction SilentlyContinue |
Where-Object { $_.Name -like '*S8.Canon*' }
foreach ($dir in $sampleDirs) {
    $newName = $dir.Name -replace 'S8.Canon', 'S8.Canon'
    if ($newName -ne $dir.Name) {
        Write-Step "rename sample dir ${($dir.FullName.Substring($Root.Length+1))} -> $newName"
        Rename-Item -Path $dir.FullName -NewName $newName
    }
}

# Rename sample files containing S8.Canon pattern
$sampleFiles = Get-ChildItem -Path (Join-Path $Root 'samples/S8.Canon') -Recurse -File -ErrorAction SilentlyContinue |
Where-Object { $_.Name -like '*S8.Canon*' }
foreach ($file in $sampleFiles) {
    $newName = $file.Name -replace 'S8.Canon', 'S8.Canon'
    if ($newName -ne $file.Name) {
        $newPath = Join-Path $file.DirectoryName $newName
        Write-Step "rename sample file ${($file.FullName.Substring($Root.Length+1))} -> $newName"
        Move-Item -Path $file.FullName -Destination $newPath
    }
}

$replacementPairs = @(
    @{ From = 'Koan.Canon'; To = 'Koan.Canon' },
    @{ From = 'KOAN.CANON'; To = 'KOAN.CANON' },
    @{ From = 'koan.canon'; To = 'koan.canon' },
    @{ From = 'KoanCanon'; To = 'KoanCanon' },
    @{ From = 'KOAN_CANON'; To = 'KOAN_CANON' },
    @{ From = 'koan_canon'; To = 'koan_canon' },
    @{ From = 'S8.Canon'; To = 'S8.Canon' },
    @{ From = 's8.canon'; To = 's8.canon' },
    @{ From = 'S8Canon'; To = 'S8Canon' }
)

$textExtensions = @(
    '.cs', '.csproj', '.fs', '.fsproj', '.vb', '.vbproj',
    '.sln', '.slnf', '.props', '.targets', '.json', '.jsonc',
    '.yml', '.yaml', '.md', '.txt', '.ps1', '.psm1', '.psd1',
    '.cshtml', '.razor', '.html', '.htm', '.css', '.scss', '.less',
    '.ts', '.tsx', '.js', '.jsx', '.config', '.ini', '.xml', '.sql',
    '.bat', '.cmd', '.sh', '.dockerfile', '.graphql', '.gql'
)

$explicitNames = @('Dockerfile', '.editorconfig', 'Makefile', 'README', 'LICENSE')

$files = Get-ChildItem -Path $Root -Recurse -File -ErrorAction SilentlyContinue |
Where-Object {
    -not (ShouldSkip $_.DirectoryName $excludeDirs) -and (
        $textExtensions -contains $_.Extension.ToLower() -or
        $explicitNames -contains $_.Name -or
        $explicitNames -contains ([System.IO.Path]::GetFileNameWithoutExtension($_.Name))
    )
}

foreach ($file in $files) {
    $content = [System.IO.File]::ReadAllText($file.FullName)
    $updated = $false
    foreach ($entry in $replacementPairs) {
        $newContent = $content.Replace($entry.From, $entry.To)
        if ($newContent -ne $content) {
            $content = $newContent
            $updated = $true
        }
    }
    if ($updated) {
        Write-Step "update contents ${($file.FullName.Substring($Root.Length+1))}"
        [System.IO.File]::WriteAllText($file.FullName, $content, [System.Text.Encoding]::UTF8)
    }
}

Write-Step "rename complete"
