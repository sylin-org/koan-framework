param(
  [int]$Port = 5086,
  [string]$Image = "s6-socialcreator:dev",
  [string]$Volume = "s6-socialcreator-data"
)

# Ensure Docker Desktop/daemon is running
docker info 1>$null 2>$null
if ($LASTEXITCODE -ne 0) {
  Write-Error "Docker Desktop does not appear to be running. Start Docker Desktop and re-run this script."
  exit 1
}

# Run from script directory so relative paths resolve
Push-Location $PSScriptRoot
try {
  # Build image
  Write-Host "Building $Image..."
  docker build -t $Image -f ./Dockerfile ../..
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Create volume if missing
if (-not (docker volume ls --format '{{.Name}}' | Select-String -SimpleMatch $Volume)) {
  Write-Host "Creating volume $Volume..."
  docker volume create $Volume | Out-Null
}

  # Run detached, map port and mount volume
  Write-Host "Running container on http://localhost:$Port"
  docker run -d --rm `
    -p "${Port}:5086" `
    -v "${Volume}:/app/data" `
    --name s6-socialcreator `
    $Image | Out-Null
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

  Write-Host "Container started: s6-socialcreator"
}
finally {
  Pop-Location
}
