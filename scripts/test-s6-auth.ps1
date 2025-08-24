param(
  [string]$ProjectPath = "samples/S6.Auth/S6.Auth.csproj",
  [string]$Url = "http://localhost:5000/.well-known/auth/providers",
  [int]$StartupTimeoutSec = 10
)

$ErrorActionPreference = 'Stop'

Write-Host "Starting S6.Auth from $ProjectPath ..."
$job = Start-Job -ScriptBlock {
  param($proj)
  dotnet run --project $proj --no-build
} -ArgumentList $ProjectPath

try {
  $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSec)
  $started = $false
  while([DateTime]::UtcNow -lt $deadline) {
    try {
      $ping = Invoke-WebRequest -UseBasicParsing -Uri ($Url -replace '/\.well-known/.*$', '/') -TimeoutSec 1
      if ($ping.StatusCode -ge 200 -and $ping.StatusCode -lt 500) { $started = $true; break }
    }
    catch {
      Start-Sleep -Milliseconds 200
    }
  }
  if (-not $started) { throw "Server did not start within $StartupTimeoutSec seconds" }

  $resp = Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 5
  Write-Host "STATUS" $resp.StatusCode
  $resp.Content
}
finally {
  if ($null -ne $job) {
    $job | Stop-Job -ErrorAction SilentlyContinue | Out-Null
    Receive-Job $job -Keep -ErrorAction SilentlyContinue | Out-Null
    $job | Remove-Job -Force -ErrorAction SilentlyContinue | Out-Null
  }
}
