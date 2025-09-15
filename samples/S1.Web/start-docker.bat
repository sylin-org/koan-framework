@echo off
setlocal
REM Start S1.Web in Docker (build + run)

set "HOST_PORT=%~1"
if "%HOST_PORT%"=="" set "HOST_PORT=5044"

REM Move to Koan root (this script is in Koan\samples\S1.Web)
pushd "%~dp0\..\.."

REM Ensure data directory exists (bind-mounted)
if not exist "samples\S1.Web\data" mkdir "samples\S1.Web\data" >nul 2>nul

echo Building image 'Koan-s1:latest'...
docker build -f samples\S1.Web\Dockerfile -t Koan-s1:latest .
if errorlevel 1 (
  echo Build failed.
  popd
  exit /b 1
)

REM Stop/remove any existing container with the same name
docker rm -f Koan-s1 >nul 2>nul

echo Starting container on http://localhost:%HOST_PORT% ...
docker run --rm -d --name Koan-s1 -p %HOST_PORT%:5044 -v "%CD%\samples\S1.Web\data:/app/data" Koan-s1:latest
if errorlevel 1 (
  echo Run failed.
  popd
  exit /b 1
)

popd
echo Container started. Press any key to open the browser (or Ctrl+C to skip)...
pause >nul
start "" "http://localhost:%HOST_PORT%"
endlocal
