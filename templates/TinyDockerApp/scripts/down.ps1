param()
$ErrorActionPreference = 'Stop'
Write-Host "Stopping TinyDockerApp via compose..."
docker compose -f ./compose/docker-compose.yml down
