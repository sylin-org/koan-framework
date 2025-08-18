@echo off
setlocal enableextensions enabledelayedexpansion
cd /d "%~dp0"

set "COMPOSE_FILE=compose\docker-compose.yml"
if not exist "%COMPOSE_FILE%" (
  echo Compose file not found: %~dp0%COMPOSE_FILE%
  exit /b 1
)

REM Usage: start.bat [up^|rebuild^|up-only^|rebuild-only^|logs^|down]
set "CMD=%~1"
if "%CMD%"=="" set "CMD=rebuild" 

if /i "%CMD%"=="up"          goto :UP
if /i "%CMD%"=="rebuild"     goto :REBUILD
if /i "%CMD%"=="up-only"     goto :UP_ONLY
if /i "%CMD%"=="rebuild-only" goto :REBUILD_ONLY
if /i "%CMD%"=="logs"        goto :LOGS
if /i "%CMD%"=="down"        goto :DOWN

echo Unknown command: %CMD%
echo Usage: start.bat [up^|rebuild^|up-only^|rebuild-only^|logs^|down]
exit /b 2

:UP
docker compose -f "%COMPOSE_FILE%" up -d
if errorlevel 1 goto :ERROR
goto :LOGS

:REBUILD
docker compose -f "%COMPOSE_FILE%" up -d --build
if errorlevel 1 goto :ERROR
goto :LOGS

:UP_ONLY
docker compose -f "%COMPOSE_FILE%" up -d
exit /b %errorlevel%

:REBUILD_ONLY
docker compose -f "%COMPOSE_FILE%" up -d --build
exit /b %errorlevel%

:LOGS
echo.
echo Showing sample logs (Ctrl+C to exit)...
echo.
docker compose -f "%COMPOSE_FILE%" logs -f s3-mq-sample
REM When logs are interrupted (Ctrl+C), return success for convenience
exit /b 0

:DOWN
docker compose -f "%COMPOSE_FILE%" down
exit /b %errorlevel%

:ERROR
echo Failed to start the compose stack.
exit /b 1
