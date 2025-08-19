@echo off
setlocal enableextensions

REM Tears down the S4 compose stack and removes volumes.

set COMPOSE_FILE=docker-compose.yml
set PROJECT_NAME=s4web

pushd "%~dp0"
if not exist "%COMPOSE_FILE%" (
  echo [S4] Compose file not found: %COMPOSE_FILE%
  popd
  exit /b 1
)

set "DC=docker compose"
%DC% version >NUL 2>&1
if errorlevel 1 (
  set "DC=docker-compose"
)

echo [S4] Bringing down stack with %DC% -f %COMPOSE_FILE% -p %PROJECT_NAME% down -v
%DC% -f "%COMPOSE_FILE%" -p "%PROJECT_NAME%" down -v
set ERR=%ERRORLEVEL%
popd
exit /b %ERR%
