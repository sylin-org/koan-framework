param(
  [string]$BaseUrl = "http://localhost:5064"
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Invoke-Gql([string]$Query, $Variables) {
  $headers = @{ 'X-Koan-Debug' = 'true' }
  $payload = @{ query = $Query }
  if ($null -ne $Variables) { $payload.variables = $Variables }
  $json = $payload | ConvertTo-Json -Depth 8
  return Invoke-RestMethod -Uri "$BaseUrl/graphql" -Method Post -Headers $headers -ContentType 'application/json' -Body $json
}

Write-Host "--- upsert ---" -ForegroundColor Cyan
$mutation = @'
mutation {
  upsertItem(input: { name: "alpha" }) { id name display }
}
'@
try {
  $resp1 = Invoke-Gql -Query $mutation -Variables $null
  $resp1 | ConvertTo-Json -Depth 20 | Write-Output
} catch {
  Write-Warning $_.Exception.Message
}

Write-Host "--- items ---" -ForegroundColor Cyan
$query = @'
query {
  items(page: 1, size: 10) {
    totalCount
    items { id name display }
  }
}
'@
try {
  $resp2 = Invoke-Gql -Query $query -Variables $null
  $resp2 | ConvertTo-Json -Depth 20 | Write-Output
} catch {
  Write-Warning $_.Exception.Message
}

Write-Host "--- sdl (first 80 lines) ---" -ForegroundColor Cyan
try {
  $sdl = Invoke-RestMethod -Uri "$BaseUrl/graphql/sdl"
  $sdl -split "\r?\n" | Select-Object -First 80 | ForEach-Object { $_ }
} catch {
  Write-Warning $_.Exception.Message
}
