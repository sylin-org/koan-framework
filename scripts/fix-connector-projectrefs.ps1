param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path,
    [switch]$WhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$srcRoot = Join-Path $RepoRoot 'src'
$connectorsRoot = Join-Path $srcRoot 'Connectors'

if (-not (Test-Path $connectorsRoot)) {
    throw "Connectors root not found at '$connectorsRoot'"
}

# Build a lookup of all project files keyed by file name for quick resolution.
$projectIndex = @{}
Get-ChildItem -Path $srcRoot -Recurse -Filter '*.csproj' | ForEach-Object {
    $name = $_.Name
    if ($projectIndex.ContainsKey($name)) {
        $projectIndex[$name] = @($projectIndex[$name]) + $_
    }
    else {
        $projectIndex[$name] = $_
    }
}

$csprojFiles = Get-ChildItem -Path $connectorsRoot -Recurse -Filter '*.csproj'

foreach ($csproj in $csprojFiles) {
    $doc = [System.Xml.Linq.XDocument]::Load($csproj.FullName)
    $ns = $doc.Root.Name.Namespace
    $changed = $false

    foreach ($projRef in $doc.Descendants($ns + 'ProjectReference')) {
        $include = $projRef.Attribute('Include')
    if ($null -eq $include) { continue }
        $includeValue = $include.Value
        if ([string]::IsNullOrWhiteSpace($includeValue)) { continue }

        $fileName = [System.IO.Path]::GetFileName($includeValue)
        if (-not $projectIndex.ContainsKey($fileName)) { continue }

        $targets = $projectIndex[$fileName]
        $chosen = if ($targets -is [System.Array]) { $targets[0] } else { $targets }
        $targetPath = $chosen.FullName

        # Skip self-references.
        if ($targetPath -eq $csproj.FullName) { continue }

        $newRelativePath = [System.IO.Path]::GetRelativePath($csproj.Directory.FullName, $targetPath)
        $newRelativePath = $newRelativePath -replace '/', '\\'

        if ($newRelativePath -ne $includeValue) {
            if ($WhatIf) {
                Write-Host "Would update $(Resolve-Path $csproj.FullName -Relative) :: $includeValue -> $newRelativePath"
            }
            else {
                $include.Value = $newRelativePath
            }
            $changed = $true
        }
    }

    if ($changed -and -not $WhatIf) {
        $settings = New-Object System.Xml.XmlWriterSettings
        $settings.Indent = $true
    $settings.OmitXmlDeclaration = $null -eq $doc.Declaration
        $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
        $writer = [System.Xml.XmlWriter]::Create($csproj.FullName, $settings)
        try {
            $doc.Save($writer)
        }
        finally {
            $writer.Dispose()
        }
    }
}
