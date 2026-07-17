#!/bin/bash
set -e

# Ensure we run from the script's directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Configuration
COMPOSE_FILE="docker/compose.yml"
PROJECT_NAME="koan-s14-adapterbench"
API_URL="http://localhost:5174/swagger/index.html"
OPEN_URL="http://localhost:5174"

# Check for Docker
if ! command -v docker &> /dev/null; then
    echo "Docker is required but not found in PATH."
    exit 1
fi

# Check for docker compose
if docker compose version &> /dev/null; then
    echo "Using 'docker compose' CLI"
    COMPOSE_CMD="docker compose"
elif command -v docker-compose &> /dev/null; then
    echo "Using legacy 'docker-compose' CLI"
    COMPOSE_CMD="docker-compose"
else
    echo "docker-compose is not available. Please update Docker or install docker-compose."
    exit 1
fi

# Build and start services
echo "Building and starting services..."
$COMPOSE_CMD -p "$PROJECT_NAME" -f "$COMPOSE_FILE" build || { echo "Failed to build"; exit 1; }
$COMPOSE_CMD -p "$PROJECT_NAME" -f "$COMPOSE_FILE" up -d || { echo "Failed to start services"; exit 1; }

# Wait for API to be ready
echo "Waiting for API to be ready at $API_URL ..."
for i in {1..60}; do
    if curl -f -s -o /dev/null "$API_URL"; then
        break
    fi
    sleep 2
done

# Open browser
if command -v xdg-open &> /dev/null; then
    xdg-open "$OPEN_URL"
elif command -v open &> /dev/null; then
    open "$OPEN_URL"
else
    echo "Please open $OPEN_URL in your browser"
fi

echo "Stack started. Application available at $OPEN_URL"
