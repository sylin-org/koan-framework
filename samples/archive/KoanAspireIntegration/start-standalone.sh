#!/bin/bash
# ==============================================================================
# Koan Aspire Integration - Standalone Mode (Linux/Mac)
# Uses self-orchestration to automatically provision dependencies
# ==============================================================================

echo ""
echo "┌─ Koan Aspire Integration - Standalone Mode ─────────────────────────────────"
echo "│ Self-orchestration will automatically provision Postgres + Redis containers"
echo "│ Application will be available at: http://localhost:8080"
echo "└─────────────────────────────────────────────────────────────────────────────"
echo ""

# Navigate to the script directory
cd "$(dirname "$0")"

# Set environment for development
export ASPNETCORE_ENVIRONMENT=Development

echo "[INFO] Starting Koan Aspire Integration in standalone mode..."
echo "[INFO] Self-orchestration will provision dependencies automatically"

# Launch browser after a delay (background task)
(sleep 5 && {
    if command -v open >/dev/null 2>&1; then
        open "http://localhost:8080"
    elif command -v xdg-open >/dev/null 2>&1; then
        xdg-open "http://localhost:8080"
    fi
}) &

# Start the application - self-orchestration will handle Docker containers
dotnet run --urls http://localhost:8080

echo ""
echo "[INFO] Application stopped. Self-orchestration will clean up containers."