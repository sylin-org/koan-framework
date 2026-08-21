<#
.SYNOPSIS
  Publish the AotRelational sample under NativeAOT and run it against a real store.

.DESCRIPTION
  The NativeAOT claim is the one capability in this repository that no suite can express: ILC forbids
  things the JIT allows, so a green solution and a green test run say nothing about whether the
  published binary starts. That gap is not theoretical. ARCH-0093 certified the single binary on
  2026-07-17; the mapping compiler began ordering properties by `MemberInfo.MetadataToken` on
  2026-08-06, which ILC does not support; and from that day every AOT-published Koan application died
  on the first entity it mapped. Five weeks passed with the claim standing in an accepted ADR, and it
  was found only because an unrelated question sent someone to re-measure (PMC-049, PMC-050).

  So this check publishes and *runs*. A compile-only proxy was considered and rejected: of the three
  defects PMC-049 found, only the reference manifest fails at publish time. `MetadataToken` throws
  when the first entity is mapped and `Assembly.GetName()` throws during boot discovery -- an ILC
  compile would have caught one of three and certified a binary that dies on startup, which is worse
  than no check at all.

  Each cell publishes the sample for one connector, runs the binary against a store, and requires:

    exit code 0            the binary started, mapped an entity, wrote, read, and shut down
    adapter=<expected>     the connector under test actually took the call. Without this a connector
                           that failed election and fell back to another provider would produce a
                           perfectly green write-then-read, and the proof would be of the fallback
                           rather than of the thing under test.
    OK                     the value read back equalled the value written

  Sqlite needs nothing external. The four server cells each start their own container, so they run
  where Docker is available -- a developer machine or a certification boundary -- rather than in the
  per-PR gate.

.PARAMETER Connectors
  Which cells to run. Defaults to Sqlite: it is the container-free floor, and it is what the daily
  lane runs, because every framework-level AOT regression found so far breaks it too. Use 'All' for
  the full matrix when Docker is present.

.EXAMPLE
  pwsh scripts/aot-verify.ps1                      # the floor cell, no Docker needed
  pwsh scripts/aot-verify.ps1 -Connectors All      # every backend, needs Docker
  pwsh scripts/aot-verify.ps1 -Connectors Postgres,SqlServer
#>
[CmdletBinding()]
param(
    [string[]]$Connectors = @('Sqlite'),
    [string]$Rid = 'win-x64',
    [string]$Configuration = 'Release',
    # Leave the published binaries and containers in place for inspection after a failure.
    [switch]$KeepArtifacts
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'samples/fundamentals/AotRelational/AotRelational.csproj'
if (-not (Test-Path $project)) { throw "AotRelational sample not found at $project" }

# The build regenerates the sample's composition lockfile from whichever connector it just referenced,
# and that file is tracked. Running the matrix would otherwise leave the tree dirty with the last cell's
# connector and trip the ratchet's lockfile-drift leg on a check that has nothing to do with drift.
$lockPath = Join-Path (Split-Path -Parent $project) 'koan.lock.json'
$lockBefore = if (Test-Path $lockPath) { [System.IO.File]::ReadAllBytes($lockPath) } else { $null }

# One row per cell. The adapter name is the receipt: it is what makes a silent fallback fail.
$cells = [ordered]@{
    Sqlite = @{
        Adapter = 'SqliteRepository`2'
        Image   = $null
    }
    Postgres = @{
        Adapter       = 'NpgsqlRepository`2'
        Image         = 'postgres:17'
        Port          = 55432
        ContainerPort = 5432
        Env           = @('POSTGRES_PASSWORD=koanpw', 'POSTGRES_USER=koan', 'POSTGRES_DB=koanaot')
        Ready         = { param($n) docker exec $n pg_isready -U koan 2>$null | Select-String 'accepting' }
        Setting       = 'Koan__Data__Postgres__ConnectionString'
        Conn          = 'Host=localhost;Port=55432;Username=koan;Password=koanpw;Database=koanaot'
    }
    Cockroach = @{
        Adapter       = 'NpgsqlRepository`2'
        Image         = 'cockroachdb/cockroach:latest-v24.3'
        Args          = @('start-single-node', '--insecure')
        Port          = 56257
        ContainerPort = 26257
        Ready         = { param($n) docker exec $n ./cockroach sql --insecure -e 'SELECT 1' 2>$null | Select-String '1' }
        Init          = { param($n) docker exec $n ./cockroach sql --insecure -e 'CREATE DATABASE IF NOT EXISTS koanaot' | Out-Null }
        Setting       = 'Koan__Data__Cockroach__ConnectionString'
        Conn          = 'Host=localhost;Port=56257;Username=root;Database=koanaot;SSL Mode=Disable'
    }
    MySql = @{
        Adapter       = 'MySqlRepository`2'
        Image         = 'mysql:8.4'
        Port          = 53306
        ContainerPort = 3306
        Env           = @('MYSQL_ROOT_PASSWORD=koanpw', 'MYSQL_DATABASE=koanaot')
        Ready         = { param($n) docker exec $n mysqladmin ping -uroot -pkoanpw 2>$null | Select-String 'alive' }
        Setting       = 'Koan__Data__MySql__ConnectionString'
        Conn          = 'Server=localhost;Port=53306;Database=koanaot;User Id=root;Password=koanpw'
    }
    # A cell whose expected outcome is a refusal, and the only guard for the third PMC-049 defect.
    #
    # `AppBootstrapper` used to call Assembly.GetName() on every assembly, which materializes the
    # culture; a globalization-invariant process cannot construct one for a satellite resource
    # assembly, so the eleven Microsoft.Data.SqlClient ships aborted discovery. No other cell reaches
    # that code: Sqlite's graph has no satellites, and the ordinary SqlServer cell publishes with
    # culture data because SqlClient requires it -- so the satellites are nameable there and the bug
    # stays hidden. Forcing the SqlServer build invariant is what puts satellites and invariant mode
    # in the same process.
    #
    # SqlClient then refuses to open the connection, which is correct and is the point: reaching that
    # refusal proves boot discovery survived the satellites. So this cell asserts the failure it
    # should get and the failure it must not. No container -- the refusal precedes any network.
    SqlServerInvariant = @{
        Connector    = 'SqlServer'
        Image        = $null
        PublishArgs  = @('-p:InvariantGlobalization=true')
        Expect       = 'refusal'
        MustMatch    = 'Globalization Invariant Mode is not supported'
        MustNotMatch = 'CultureNotFoundException'
        Setting      = 'Koan__Data__SqlServer__ConnectionString'
        Conn         = 'Server=localhost,51433;Database=koanaot;User Id=sa;Password=Koan_Pw123!;TrustServerCertificate=True'
    }
    SqlServer = @{
        Adapter       = 'SqlServerRepository`2'
        Image         = 'mcr.microsoft.com/mssql/server:2022-latest'
        Port          = 51433
        ContainerPort = 1433
        Env           = @('ACCEPT_EULA=Y', 'MSSQL_SA_PASSWORD=Koan_Pw123!', 'MSSQL_PID=Developer')
        Ready         = { param($n) docker exec $n /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Koan_Pw123!' -C -Q "SELECT 'PING_OK'" 2>$null | Select-String 'PING_OK' }
        Init          = { param($n) docker exec $n /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Koan_Pw123!' -C -Q "IF DB_ID('koanaot') IS NULL CREATE DATABASE koanaot;" | Out-Null }
        Setting       = 'Koan__Data__SqlServer__ConnectionString'
        Conn          = 'Server=localhost,51433;Database=koanaot;User Id=sa;Password=Koan_Pw123!;TrustServerCertificate=True'
    }
}

if ($Connectors.Count -eq 1 -and $Connectors[0] -eq 'All') { $Connectors = @($cells.Keys) }
foreach ($c in $Connectors) {
    if (-not $cells.Contains($c)) { throw "Unknown connector '$c'. Known: $($cells.Keys -join ', '), All" }
}

# ILC links with MSVC on Windows. Its own toolchain probe corrupts the linker path when vswhere is off
# PATH, so import the developer environment here and publish with -p:IlcUseEnvironmentalTools=true.
function Import-VcEnvironment {
    if (-not $IsWindows) { return }
    if ($env:VSCMD_ARG_TGT_ARCH -eq 'x64') { return }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw 'vswhere.exe not found. Install the Desktop development with C++ workload.' }
    $install = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $install) { throw 'No Visual Studio installation carries the C++ tools ILC links with.' }
    $batch = Join-Path $install 'VC/Auxiliary/Build/vcvars64.bat'
    if (-not (Test-Path $batch)) { throw "vcvars64.bat not found under $install" }
    cmd /c "`"$batch`" >nul 2>&1 && set" | ForEach-Object {
        if ($_ -match '^([^=]+)=(.*)$') { Set-Item -Path "env:$($matches[1])" -Value $matches[2] }
    }
}

function Start-CellContainer {
    param($Name, $Cell)

    docker rm -f $Name 2>&1 | Out-Null
    $runArgs = @('run', '-d', '--name', $Name, '-p', "$($Cell.Port):$($Cell.ContainerPort)")
    if ($Cell.Contains('Env')) { foreach ($e in $Cell.Env) { $runArgs += @('-e', $e) } }
    $runArgs += $Cell.Image
    if ($Cell.Contains('Args')) { $runArgs += $Cell.Args }
    docker @runArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "could not start container $Name from $($Cell.Image)" }

    $deadline = (Get-Date).AddMinutes(4)
    while ((Get-Date) -lt $deadline) {
        try { if (& $Cell.Ready $Name) { break } } catch { }
        Start-Sleep -Seconds 4
    }
    if ((Get-Date) -ge $deadline) { throw "$Name never became ready within four minutes" }
    if ($Cell.Contains('Init')) { & $Cell.Init $Name }
}

$failures = New-Object System.Collections.Generic.List[string]
Import-VcEnvironment

foreach ($connector in $Connectors) {
    $cell = $cells[$connector]
    $outDir = Join-Path $repoRoot "artifacts/aot/$connector"
    $container = "koan-aot-$($connector.ToLowerInvariant())"
    Write-Host "== $connector ==================================================" -ForegroundColor Cyan

    try {
        if ($cell.Image) { Start-CellContainer -Name $container -Cell $cell }

        # The reference-manifest defect fires only on the *first* RID publish of a project: the target
        # wrote into obj/<cfg>/<tfm>/<rid>/ without creating it, and a second publish then succeeded on
        # the directory the failed one left behind. A warm intermediate tree would therefore hide it on
        # a developer machine while CI's fresh checkout caught it. Drop the sample's obj so every run
        # is a first publish and the cell means the same thing everywhere.
        $sampleObj = Join-Path (Split-Path -Parent $project) 'obj'
        if (Test-Path $sampleObj) { Remove-Item -Recurse -Force $sampleObj }

        # A cell may publish a connector under a different name (the invariant SqlServer variant).
        $buildConnector = $(if ($cell.Contains('Connector')) { $cell.Connector } else { $connector })

        $publishArgs = @(
            'publish', $project, '-c', $Configuration, '-r', $Rid,
            '-p:KoanAot=true', "-p:Connector=$buildConnector", '-o', $outDir
        )
        if ($cell.Contains('PublishArgs')) { $publishArgs += $cell.PublishArgs }
        if ($IsWindows) { $publishArgs += '-p:IlcUseEnvironmentalTools=true' }
        $publishLog = & dotnet @publishArgs 2>&1
        if ($LASTEXITCODE -ne 0) {
            $publishLog | Select-Object -Last 25 | ForEach-Object { Write-Host $_ }
            $failures.Add("$connector : NativeAOT publish failed (exit $LASTEXITCODE)")
            continue
        }

        $exe = Join-Path $outDir ($(if ($IsWindows) { 'AotRelational.exe' } else { 'AotRelational' }))
        if (-not (Test-Path $exe)) {
            $failures.Add("$connector : publish reported success but produced no executable at $exe")
            continue
        }

        if ($cell.Contains('Setting')) { Set-Item -Path "env:$($cell.Setting)" -Value $cell.Conn }
        try {
            $runLog = & $exe 2>&1
            $runExit = $LASTEXITCODE
        } finally {
            if ($cell.Contains('Setting')) { Remove-Item -Path "env:$($cell.Setting)" -ErrorAction SilentlyContinue }
        }

        $text = ($runLog | Out-String)

        if ($cell.Contains('Expect') -and $cell.Expect -eq 'refusal') {
            if ($text -match [regex]::Escape($cell.MustNotMatch)) {
                $failures.Add("$connector : boot died on '$($cell.MustNotMatch)' before reaching the driver's own refusal")
            } elseif ($text -notmatch [regex]::Escape($cell.MustMatch)) {
                $runLog | Select-Object -Last 20 | ForEach-Object { Write-Host $_ }
                $failures.Add("$connector : expected the refusal '$($cell.MustMatch)' and did not see it")
            } else {
                Write-Host "   $connector OK - reached the driver's refusal, boot survived the satellites" -ForegroundColor Green
            }
            continue
        }

        $expected = "adapter=$($cell.Adapter)"
        if ($runExit -ne 0) {
            $runLog | Select-Object -Last 25 | ForEach-Object { Write-Host $_ }
            $failures.Add("$connector : the published binary exited $runExit")
        } elseif ($text -notmatch [regex]::Escape($expected)) {
            $failures.Add("$connector : expected '$expected'; another provider took the call, so this proves nothing about $connector")
        } elseif ($text -notmatch '(?m)^OK\s*$') {
            $failures.Add("$connector : the binary ran but never reported OK")
        } else {
            $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
            Write-Host "   $connector OK - $expected, $size MB" -ForegroundColor Green
        }
    } catch {
        $failures.Add("$connector : $($_.Exception.Message)")
    } finally {
        if ($cell.Image -and -not $KeepArtifacts) { docker rm -f $container 2>&1 | Out-Null }
    }
}

if ($null -ne $lockBefore) {
    $lockNow = if (Test-Path $lockPath) { [System.IO.File]::ReadAllBytes($lockPath) } else { $null }
    if ($null -eq $lockNow -or [System.Convert]::ToBase64String($lockNow) -ne [System.Convert]::ToBase64String($lockBefore)) {
        [System.IO.File]::WriteAllBytes($lockPath, $lockBefore)
    }
    # The lockfile writer emits "\n" while a Windows checkout holds CRLF, so a byte-exact restore can
    # still leave the path stat-dirty against the index. Let git settle the working-tree form when the
    # content already matches HEAD; a genuine drift is left alone for the ratchet's lockfile leg to see.
    Push-Location $repoRoot
    try {
        git diff --quiet -- $lockPath 2>$null
        if ($LASTEXITCODE -eq 0) { git checkout -- $lockPath 2>$null | Out-Null }
    } catch { } finally { Pop-Location }
}

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host 'AOT verification RED' -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}

Write-Host "AOT verification GREEN - $($Connectors -join ', ') on $Rid" -ForegroundColor Green
exit 0
