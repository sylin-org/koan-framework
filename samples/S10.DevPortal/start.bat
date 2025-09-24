@echo off
echo S10.DevPortal - Koan Framework Capabilities Demo
echo ================================================

echo.
echo Starting multi-provider demo stack...
echo - API: http://localhost:5090
echo - MongoDB: localhost:5091
echo - PostgreSQL: localhost:5092
echo - Redis: localhost:5093
echo.

REM Check if Docker is running
docker version >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Docker is not running or not installed.
    echo Please start Docker Desktop and try again.
    pause
    exit /b 1
)

REM Navigate to docker directory
cd docker

REM Stop any existing containers
echo Stopping existing containers...
docker compose down

REM Pull latest images
echo Pulling latest images...
docker compose pull

REM Build and start the stack
echo Building and starting services...
docker compose up --build -d

REM Wait for services to be ready
echo.
echo Waiting for services to start...
timeout /t 10 /nobreak >nul

REM Check service health
echo.
echo Checking service health...
docker compose ps

echo.
echo ================================================
echo S10.DevPortal is starting up!
echo.
echo Application: http://localhost:5090
echo MongoDB:     localhost:5091
echo PostgreSQL:  localhost:5092
echo Redis:       localhost:5093
echo.
echo To stop: docker compose down
echo To view logs: docker compose logs -f api
echo ================================================

REM Open browser
start http://localhost:5090

REM Return to original directory
cd ..

echo.
echo Press any key to view real-time logs...
pause >nul
cd docker
docker compose logs -f api