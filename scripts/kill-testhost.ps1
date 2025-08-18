<#
 Kills common locked test host processes to unblock dotnet test on Windows.
 Default: targets testhost/vstest/xunit/datacollector only. Use -IncludeDotnet to also kill dotnet processes.
#>
param(
  [switch]$WhatIf,
  [switch]$IncludeDotnet
)

$names = @(
  'testhost', 'testhost.x86', 'testhost.net*',
  'vstest.console*', 'vstest.executionengine*',
  'datacollector*',
  'xunit.console*'
)
if ($IncludeDotnet) { $names += 'dotnet' }

foreach ($n in $names) {
  $procs = Get-Process -Name $n -ErrorAction SilentlyContinue
  foreach ($p in $procs) {
    if ($WhatIf) {
      Write-Host "Would kill PID $($p.Id) Name $($p.ProcessName)" -ForegroundColor Yellow
    } else {
      try {
        Write-Host "Killing PID $($p.Id) Name $($p.ProcessName)" -ForegroundColor Red
        $p.Kill()
      } catch {
        Write-Warning "Failed to kill $($p.Id): $_"
      }
    }
  }
}
