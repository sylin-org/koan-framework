@echo off
setlocal
REM Start S1.Web via Docker Compose

set "HOST_PORT=%~1"
if "%HOST_PORT%"=="" set "HOST_PORT=5044"

pushd "%~dp0"
if not exist "data" mkdir "data" >nul 2>nul

echo Starting with docker compose on http://localhost:%HOST_PORT% ...
REM Compose file already uses ${HOST_PORT:-5044}:5044, so just pass the env var
docker compose up -d --build

if errorlevel 1 (
  echo Compose up failed.
  popd
  exit /b 1
)

popd
echo Running. Press any key to open the browser (or Ctrl+C to skip)...
pause >nul
start "" "http://localhost:%HOST_PORT%"
endlocal
