# Stop and remove the running container
param(
  [string]$Name = "s6-socialcreator"
)

# Ensure Docker Desktop/daemon is running
docker info 1>$null 2>$null
if ($LASTEXITCODE -ne 0) {
  Write-Warning "Docker Desktop is not running; nothing to stop."
  exit 0
}

$exists = docker ps -a --format '{{.Names}}' | Where-Object { $_ -eq $Name }
if ($exists) {
  Write-Host "Stopping container $Name ..."
  docker stop $Name | Out-Null
} else {
  Write-Host "Container $Name not running."
}
