[CmdletBinding()]
param(
    [ValidateSet("Fast", "Pillars", "Infrastructure", "All")]
    [string]$Lane = "Fast",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [ValidateRange(1, 3600)]
    [int]$BuildTimeoutSeconds,

    [ValidateRange(1, 3600)]
    [int]$RunTimeoutSeconds
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

$lanes = @(
    [pscustomobject]@{
        Name = "Fast"
        Project = "tests/Suites/Integration/Bootstrap/Koan.Tests.Integration.Bootstrap/Koan.Tests.Integration.Bootstrap.csproj"
        BuildTimeout = 60
        RunTimeout = 30
        Explicit = $false
    },
    [pscustomobject]@{
        Name = "Pillars"
        Project = "tests/Suites/Integration/Bootstrap/Koan.Tests.Integration.Bootstrap.Pillars/Koan.Tests.Integration.Bootstrap.Pillars.csproj"
        BuildTimeout = 120
        RunTimeout = 120
        Explicit = $false
    },
    [pscustomobject]@{
        Name = "Infrastructure"
        Project = "tests/Suites/Integration/Bootstrap/Koan.Tests.Integration.Bootstrap.Infrastructure/Koan.Tests.Integration.Bootstrap.Infrastructure.csproj"
        BuildTimeout = 180
        RunTimeout = 180
        Explicit = $true
    }
)

function Invoke-BoundedDotnet {
    param(
        [Parameter(Mandatory)] [string]$LaneName,
        [Parameter(Mandatory)] [string]$Phase,
        [Parameter(Mandatory)] [string]$Project,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [int]$TimeoutSeconds
    )

    $displayCommand = "dotnet " + ($Arguments -join " ")
    Write-Host "[$LaneName/$Phase] $displayCommand"
    Write-Host "[$LaneName/$Phase] deadline: ${TimeoutSeconds}s"

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = "dotnet"
    $startInfo.WorkingDirectory = $repoRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "[$LaneName/$Phase] could not start: $displayCommand"
    }

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $finished = $process.WaitForExit($TimeoutSeconds * 1000)

    if (-not $finished) {
        try { $process.Kill($true) } catch { }
        $process.WaitForExit()
    }

    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $failed = -not $finished -or $process.ExitCode -ne 0
    if ($failed) {
        if ($stdout) { Write-Host $stdout.TrimEnd() }
        if ($stderr) { Write-Host $stderr.TrimEnd() }
    }
    elseif ($stdout) {
        $signalPattern = if ($Phase -eq "build") {
            "Build succeeded\.|^\s+\d+ Warning\(s\)|^\s+\d+ Error\(s\)|^Time Elapsed"
        }
        else {
            "Discovered:|Long Running Test|TEST EXECUTION SUMMARY|^\s+\S.*\sTotal:\s*\d+"
        }

        foreach ($line in ($stdout -split "\r?\n" | Where-Object { $_ -match $signalPattern })) {
            Write-Host $line
        }
    }

    if (-not $finished) {
        throw "[$LaneName/$Phase] timed out after ${TimeoutSeconds}s. Project: $Project. Command: $displayCommand"
    }

    if ($process.ExitCode -ne 0) {
        throw "[$LaneName/$Phase] exited with code $($process.ExitCode). Project: $Project. Command: $displayCommand"
    }

    return $stdout
}

$selected = if ($Lane -eq "All") { $lanes } else { $lanes | Where-Object Name -eq $Lane }

foreach ($current in $selected) {
    $buildDeadline = if ($PSBoundParameters.ContainsKey("BuildTimeoutSeconds")) { $BuildTimeoutSeconds } else { $current.BuildTimeout }
    $runDeadline = if ($PSBoundParameters.ContainsKey("RunTimeoutSeconds")) { $RunTimeoutSeconds } else { $current.RunTimeout }

    $buildArguments = @("build", $current.Project, "--configuration", $Configuration, "--nologo")
    [void](Invoke-BoundedDotnet -LaneName $current.Name -Phase "build" -Project $current.Project -Arguments $buildArguments -TimeoutSeconds $buildDeadline)

    $runArguments = @(
        "run", "--no-build", "--configuration", $Configuration, "--project", $current.Project, "--",
        "-longRunning", "10", "-diagnostics", "-reporter", "verbose", "-noColor"
    )
    if ($current.Explicit) {
        $runArguments += @("-explicit", "only")
    }

    $output = Invoke-BoundedDotnet -LaneName $current.Name -Phase "run" -Project $current.Project -Arguments $runArguments -TimeoutSeconds $runDeadline
    if ($output -notmatch "TEST EXECUTION SUMMARY" -or $output -notmatch "Total:\s*([1-9][0-9]*)") {
        throw "[$($current.Name)/run] process succeeded without a nonzero xUnit execution summary. Project: $($current.Project)"
    }
}

Write-Host "Bootstrap lane '$Lane' passed."
