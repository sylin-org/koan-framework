@echo off
setlocal enableextensions

REM Ensure we run from the script's directory
set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%"

REM Build the API image and bring up the stack detached, then wait and launch the app.
set COMPOSE_FILE=docker\compose.yml
set PROJECT_NAME=koan-s5-recs
set API_URL=http://localhost:5084/swagger/index.html
set OPEN_URL=http://localhost:5084

where docker >nul 2>nul
if errorlevel 1 (
  echo Docker is required but not found in PATH.
  exit /b 1
)

REM Use modern "docker compose" if available, else fallback to legacy "docker-compose"
for /f "tokens=*" %%i in ('docker compose version 2^>nul') do set HAS_DOCKER_COMPOSE_CLI=1
if defined HAS_DOCKER_COMPOSE_CLI (
  echo Using "docker compose" CLI
  docker compose -p %PROJECT_NAME% -f %COMPOSE_FILE% build || goto :error
  docker compose -p %PROJECT_NAME% -f %COMPOSE_FILE% up -d || goto :error
) else (
  where docker-compose >nul 2>nul || goto :nolegacy
  echo Using legacy "docker-compose" CLI
  docker-compose -p %PROJECT_NAME% -f %COMPOSE_FILE% build || goto :error
  docker-compose -p %PROJECT_NAME% -f %COMPOSE_FILE% up -d || goto :error
)

echo Waiting for API to be ready at %API_URL% ...
where curl >nul 2>nul && set HAS_CURL=1
if defined HAS_CURL goto :wait_with_curl
goto :wait_with_powershell

:wait_with_curl
for /l %%i in (1,1,60) do (
  curl -f -s -o NUL "%API_URL%" && goto :open
  timeout /t 2 >nul
)
echo Timed out waiting for %API_URL%.
goto :open

:wait_with_powershell
for /l %%i in (1,1,60) do (
  powershell -NoProfile -Command "try { Invoke-WebRequest -Uri '%API_URL%' -UseBasicParsing -TimeoutSec 2 ^| Out-Null; exit 0 } catch { exit 1 }" >nul 2>&1
  if not errorlevel 1 goto :open
  timeout /t 2 >nul
)
echo Timed out waiting for %API_URL%.
goto :open

:open
start "" "%OPEN_URL%"
echo Stack started. Opened %OPEN_URL% in your default browser.
popd
exit /b 0

:nolegacy
echo docker-compose is not available. Please update Docker Desktop or install docker-compose.
popd
exit /b 1

:error
echo Failed to build or start services.
popd
exit /b 1
