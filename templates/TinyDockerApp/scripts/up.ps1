param()
$ErrorActionPreference = 'Stop'
Write-Host "Starting TinyDockerApp via compose..."
docker compose -f ./compose/docker-compose.yml up -d
