@echo off
setlocal enableextensions

set "ROOT=%~dp0"
pushd "%ROOT%" >nul

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
if %ELAPSED% geq %TIMEOUT_SECONDS% (
  echo Timeout waiting for Docker Desktop to start.
  echo Please start Docker Desktop manually and try again.
  popd & exit /b 1
)

echo Still waiting... [%ELAPSED%s/%TIMEOUT_SECONDS%s]
goto wait_loop

:docker_started
echo Docker Desktop is ready!

:docker_ready
echo Docker Desktop is running.

echo Starting Prism stack (MongoDB + Weaviate + Ollama + Prism)...
docker compose up --build -d
if errorlevel 1 (popd & exit /b 1)

echo.
echo Prism is starting up!
echo Opening browser to http://localhost:5087
echo.
echo   Logs:    docker compose logs -f prism
echo   Stop:    docker compose down
echo   Restart: docker compose restart prism
echo.
start "" "http://localhost:5087" >nul 2>&1
popd
exit /b 0
