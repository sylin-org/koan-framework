@echo off
setlocal enableextensions enabledelayedexpansion

set "ROOT=%~dp0"
pushd "%ROOT%" >nul

set "PROJECT_NAME=koan-s6-snapvault"
set "COMPOSE_FILE=docker\compose.yml"
set "OPEN_URL=http://localhost:5086"

where docker >nul 2>nul
if errorlevel 1 (
  echo Docker is required but not found in PATH.
  popd & exit /b 1
)

echo Checking Docker Desktop status...
docker ps >nul 2>nul
if errorlevel 1 goto start_docker
goto docker_ready

:start_docker
echo Docker Desktop is not running. Starting Docker Desktop...
start "" "C:\Program Files\Docker\Docker\Docker Desktop.exe"

echo Waiting for Docker Desktop to start...
set /a TIMEOUT_SECONDS=120
set /a ELAPSED=0

:wait_loop
timeout /t 2 /nobreak >nul
docker ps >nul 2>nul
if not errorlevel 1 goto docker_started

set /a ELAPSED+=2
if !ELAPSED! geq %TIMEOUT_SECONDS% (
  echo Timeout waiting for Docker Desktop to start.
  echo Please start Docker Desktop manually and try again.
  popd & exit /b 1
)

echo Still waiting... [!ELAPSED!s/%TIMEOUT_SECONDS%s]
goto wait_loop

:docker_started
echo Docker Desktop is ready!

:docker_ready
echo Docker Desktop is running.

docker compose version >nul 2>nul
if errorlevel 1 goto use_docker_compose

:use_compose
echo Building images...
docker compose -p %PROJECT_NAME% -f "%COMPOSE_FILE%" build
if errorlevel 1 (popd & exit /b 1)

echo Starting services...
docker compose -p %PROJECT_NAME% -f "%COMPOSE_FILE%" up -d
if errorlevel 1 (popd & exit /b 1)
goto done

:use_docker_compose
where docker-compose >nul 2>nul
if errorlevel 1 (
  echo docker-compose is not available. Please update Docker Desktop.
  popd & exit /b 1
)

echo Building images...
docker-compose -p %PROJECT_NAME% -f "%COMPOSE_FILE%" build
if errorlevel 1 (popd & exit /b 1)

echo Starting services...
docker-compose -p %PROJECT_NAME% -f "%COMPOSE_FILE%" up -d
if errorlevel 1 (popd & exit /b 1)

:done
echo.
echo SnapVault is starting up!
echo Opening browser to %OPEN_URL%
echo.
start "" "%OPEN_URL%" >nul 2>&1
popd
exit /b 0
