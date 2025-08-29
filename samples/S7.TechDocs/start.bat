@echo off
setlocal enableextensions

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%"

set COMPOSE_FILE=docker\compose.yml
set API_URL=http://localhost:5087
set SWAGGER=%API_URL%/swagger/index.html

where docker >nul 2>nul
if errorlevel 1 (
  echo Docker is required but not found in PATH.
  exit /b 1
)

for /f "tokens=*" %%i in ('docker compose version 2^>nul') do set HAS_DOCKER_COMPOSE_CLI=1
if defined HAS_DOCKER_COMPOSE_CLI (
  echo Using "docker compose" CLI
  docker compose -f %COMPOSE_FILE% build --no-cache || goto :error
  docker compose -f %COMPOSE_FILE% up -d || goto :error
) else (
  where docker-compose >nul 2>nul || goto :nolegacy
  echo Using legacy "docker-compose" CLI
  docker-compose -f %COMPOSE_FILE% build --no-cache || goto :error
  docker-compose -f %COMPOSE_FILE% up -d || goto :error
)

echo Waiting for API to be ready at %SWAGGER% ...
where curl >nul 2>nul && set HAS_CURL=1
if defined HAS_CURL goto :wait_with_curl
goto :wait_with_powershell

:wait_with_curl
for /l %%i in (1,1,60) do (
  curl -f -s -o NUL "%SWAGGER%" && goto :open
  timeout /t 2 >nul
)
echo Timed out waiting for %SWAGGER%.
goto :open

:wait_with_powershell
for /l %%i in (1,1,60) do (
  powershell -NoProfile -Command "try { Invoke-WebRequest -Uri '%SWAGGER%' -UseBasicParsing -TimeoutSec 2 ^| Out-Null; exit 0 } catch { exit 1 }" >nul 2>&1
  if not errorlevel 1 goto :open
  timeout /t 2 >nul
)
echo Timed out waiting for %SWAGGER%.
goto :open

:open
start "" "%API_URL%"
echo Stack started. Opened %API_URL% in your default browser.
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
