#!/bin/bash
# ==============================================================================
# Koan Aspire Integration - Docker Compose Mode (Linux/Mac)
# Uses Docker Compose to orchestrate all services including the application
# ==============================================================================

echo ""
echo "┌─ Koan Aspire Integration - Docker Compose Mode ─────────────────────────────"
echo "│ Docker Compose will manage Postgres + Redis + Application containers"
echo "│ Application will be available at: http://localhost:8080"
echo "│ Swagger UI: http://localhost:8080/swagger"
echo "└─────────────────────────────────────────────────────────────────────────────"
echo ""

# Navigate to the script directory
cd "$(dirname "$0")"

# Check if Docker is running
if ! docker version >/dev/null 2>&1; then
    echo "[ERROR] Docker is not running. Please start Docker and try again."
    exit 1
fi

# Check if Docker Compose is available
if ! docker compose version >/dev/null 2>&1; then
    echo "[ERROR] Docker Compose is not available. Please ensure Docker includes Compose."
    exit 1
fi

echo "[INFO] Starting Docker Compose stack..."
echo "[INFO] This will build the application image and start all services"

# Launch browser after a delay (background task)
(sleep 15 && {
    if command -v open >/dev/null 2>&1; then
        open "http://localhost:8080"
    elif command -v xdg-open >/dev/null 2>&1; then
        xdg-open "http://localhost:8080"
    fi
}) &

# Build and start all services
docker compose up --build

echo ""
echo "[INFO] Docker Compose stack stopped."
echo "[INFO] To clean up containers and volumes, run: docker compose down -v"