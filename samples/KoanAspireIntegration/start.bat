@echo off
REM ==============================================================================
REM Koan Aspire Integration - Standalone Mode
REM Uses self-orchestration to automatically provision dependencies
REM ==============================================================================

echo.
echo ┌─ Koan Aspire Integration - Standalone Mode ─────────────────────────────────
echo │ Self-orchestration will automatically provision Postgres + Redis containers
echo │ Application will be available at: http://localhost:8080
echo └─────────────────────────────────────────────────────────────────────────────
echo.

REM Navigate to the sample directory
cd /d "%~dp0"

REM Set environment for development
set ASPNETCORE_ENVIRONMENT=Development

REM Clean up any existing containers from previous runs (self-orchestration handles this)
echo [INFO] Starting Koan Aspire Integration in standalone mode...
echo [INFO] Self-orchestration will provision dependencies automatically

REM Launch browser after a delay - open Aspire dashboard (background task)
start "" powershell -WindowStyle Hidden -Command "Start-Sleep 5; Start-Process 'http://localhost:15888'"

REM Start the application - self-orchestration will handle Docker containers
dotnet run --urls http://localhost:8080

echo.
echo [INFO] Application stopped. Self-orchestration will clean up containers.
pause