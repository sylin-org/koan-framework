param(
  [string]$Base = 'origin/main~1',
  [string]$Head = 'origin/main'
)
# Builds a matrix JSON of changed packable projects between two refs.
$ErrorActionPreference = 'Stop'

function Get-NearestCsproj($path) {
  $d = Split-Path -Parent $path
  while ($d -and (Split-Path -Leaf $d) -ne '') {
    $c = Join-Path $d '*.csproj'
    $matches = Get-ChildItem -Path $c -ErrorAction SilentlyContinue
    if ($matches) { return $matches[0].FullName }
    $d = Split-Path -Parent $d
  }
  return $null
}

$changed = git diff --name-only $Base $Head -- src/ Directory.Build.props Directory.Packages.props | Where-Object { $_ -like 'src/*' -or $_ -like 'Directory.*.props' }
$projects = @{}
foreach ($f in $changed) {
  $csproj = Get-NearestCsproj $f
  if ($csproj) { $projects[$csproj] = $true }
}

$items = @()
foreach ($p in $projects.Keys) {
  $name = Split-Path -Leaf (Split-Path -Parent $p)
  $items += @{ name = $name; csproj = $p }
}

if ($items.Count -eq 0) { return }

$matrix = @{ include = $items } | ConvertTo-Json -Depth 5
$matrix
