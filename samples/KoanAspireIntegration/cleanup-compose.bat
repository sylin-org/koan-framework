@echo off
REM ==============================================================================
REM Koan Aspire Integration - Docker Compose Cleanup
REM Removes all containers, volumes, and networks created by Docker Compose
REM ==============================================================================

echo.
echo ┌─ Koan Aspire Integration - Cleanup Docker Compose ──────────────────────────
echo │ This will remove ALL containers, volumes, and networks for this project
echo │ WARNING: This will delete all data in the databases!
echo └─────────────────────────────────────────────────────────────────────────────
echo.

REM Navigate to the sample directory
cd /d "%~dp0"

echo [WARNING] This will permanently delete:
echo   - All application containers
echo   - PostgreSQL database and data
echo   - Redis cache and data
echo   - Docker volumes and networks
echo.

set /p confirm="Are you sure you want to continue? (y/N): "
if /i not "%confirm%"=="y" (
    echo [INFO] Cleanup cancelled.
    pause
    exit /b 0
)

echo.
echo [INFO] Stopping and removing Docker Compose stack...

REM Stop and remove containers, networks, and volumes
docker compose down -v --remove-orphans

echo [INFO] Removing any dangling images...
docker image prune -f --filter "label=com.docker.compose.project=koanaspireintegration"

echo.
echo [INFO] Cleanup completed successfully.
echo [INFO] All containers, volumes, and networks have been removed.
pause