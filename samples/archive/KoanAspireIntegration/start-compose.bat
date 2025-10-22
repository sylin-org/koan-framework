@echo off
REM ==============================================================================
REM Koan Aspire Integration - Docker Compose Mode
REM Uses Docker Compose to orchestrate all services including the application
REM ==============================================================================

echo.
echo ┌─ Koan Aspire Integration - Docker Compose Mode ─────────────────────────────
echo │ Docker Compose will manage Postgres + Redis + Application containers
echo │ Application will be available at: http://localhost:8080
echo │ Swagger UI: http://localhost:8080/swagger
echo └─────────────────────────────────────────────────────────────────────────────
echo.

REM Navigate to the sample directory
cd /d "%~dp0"

REM Check if Docker is running
docker version >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Docker is not running. Please start Docker Desktop and try again.
    pause
    exit /b 1
)

REM Check if Docker Compose is available
docker compose version >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] Docker Compose is not available. Please ensure Docker Desktop includes Compose.
    pause
    exit /b 1
)

echo [INFO] Starting Docker Compose stack...
echo [INFO] This will build the application image and start all services

REM Launch browser after a delay (background task)
start "" powershell -WindowStyle Hidden -Command "Start-Sleep 15; Start-Process 'http://localhost:8080'"

REM Build and start all services
docker compose up --build

echo.
echo [INFO] Docker Compose stack stopped.
echo [INFO] To clean up containers and volumes, run: docker compose down -v
pause