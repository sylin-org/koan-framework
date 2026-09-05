[CmdletBinding()]
param()

# Contributor preparation while the ordinary-foundation manifest repair awaits publication.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$feed = Join-Path $repository ('artifacts/application-evolution/framework-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($feed) | Out-Null
$core = Join-Path $repository 'src/Koan.Core/Koan.Core.csproj'
if (-not (Test-Path -LiteralPath $core)) { throw 'Run this preparation inside the Koan repository containing the Core repair.' }
& dotnet pack $core -c Release -p:PublicRelease=true -o $feed
if ($LASTEXITCODE -ne 0) { throw "Core packing failed ($LASTEXITCODE)." }
$packages = @(Get-ChildItem -LiteralPath $feed -Filter 'Sylin.Koan.Core.*.nupkg')
if ($packages.Count -ne 1) { throw 'Expected exactly one computed Core package.' }
$package = $packages[0]
$archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $entry = $archive.Entries | Where-Object FullName -Like '*.nuspec' | Select-Object -First 1
    $reader = [IO.StreamReader]::new($entry.Open())
    try { $metadata = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    $version = [string]$metadata.package.metadata.version
} finally { $archive.Dispose() }
$local = Join-Path $PSScriptRoot '.local'
[IO.Directory]::CreateDirectory($local) | Out-Null
$escapedFeed = [Security.SecurityElement]::Escape($feed)
$props = '<Project><PropertyGroup><KoanCoreVersion>' + $version + '</KoanCoreVersion><RestoreAdditionalProjectSources>' + $escapedFeed + '</RestoreAdditionalProjectSources></PropertyGroup></Project>'
[IO.File]::WriteAllText((Join-Path $local 'Framework.props'), $props)
$receipt = @{ version = $version; package = $package.FullName; sha256 = (Get-FileHash -LiteralPath $package.FullName).Hash; published = $false }
[IO.File]::WriteAllText((Join-Path $local 'framework.json'), ($receipt | ConvertTo-Json))
Write-Output "Prepared local Sylin.Koan.Core $version. No package was published. Run either application or verify.ps1."
