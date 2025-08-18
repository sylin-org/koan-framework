param([int]$Port = __PORT__)
$ErrorActionPreference = 'Stop'
Write-Host "Running TinyApi on port $Port"
dotnet run --no-build --urls "http://localhost:$Port"
