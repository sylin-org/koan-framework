@echo off
setlocal enableextensions
REM Ensure we run from the script's directory
set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%"

REM Use the compose file living under S8.Compose
set COMPOSE_FILE=S8.Compose\docker-compose.yml
set PROJECT_NAME=sora-s8-location
set API_URL=http://localhost:4915

where docker >nul 2>nul
if errorlevel 1 (
  echo Docker is required but not found in PATH.
  popd
  exit /b 1
)

echo.
echo ===================================
echo S8.Location Stack Starting
echo ===================================
echo API will be available at: %API_URL%
echo MongoDB: localhost:4910
echo RabbitMQ Management: http://localhost:4912
echo Ollama: http://localhost:4913
echo ===================================
echo.

REM Prefer modern "docker compose"; fallback to legacy docker-compose
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

echo.
echo Waiting for services to start...
echo This may take a few minutes for Ollama to download models.
echo.

REM Wait for API to be ready
echo Checking API availability at %API_URL% ...
where curl >nul 2>nul && set HAS_CURL=1
if defined HAS_CURL goto :wait_with_curl
goto :wait_with_powershell

:wait_with_curl
for /l %%i in (1,1,120) do (
  curl -f -s -o NUL "%API_URL%" && goto :success
  echo Attempt %%i/120 - waiting...
  timeout /t 5 >nul
)
echo Timed out waiting for %API_URL%.
goto :open

:wait_with_powershell
for /l %%i in (1,1,120) do (
  powershell -NoProfile -Command "try { Invoke-WebRequest -Uri '%API_URL%' -UseBasicParsing -TimeoutSec 5 ^| Out-Null; exit 0 } catch { exit 1 }" >nul 2>&1
  if not errorlevel 1 goto :success
  echo Attempt %%i/120 - waiting...
  timeout /t 5 >nul
)
echo Timed out waiting for %API_URL%.
goto :open

:success
echo.
echo ===================================
echo S8.Location Stack Ready!
echo ===================================
echo API: %API_URL%
echo Swagger: %API_URL%/swagger
echo RabbitMQ: http://localhost:4912 (guest/guest)
echo MongoDB: localhost:4910
echo ===================================
echo.

:open
start "" "%API_URL%"
echo Opened %API_URL% in your default browser.
popd
exit /b 0

:nolegacy
echo docker-compose is not available. Please update Docker Desktop or install docker-compose.
popd
exit /b 1

:error
echo Failed to build or start services.
echo Check the logs above for details.
popd
exit /b 1