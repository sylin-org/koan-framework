@echo off
setlocal enableextensions

REM Ensure we run from the script's directory
set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%"

REM S13.DocMind Document Intelligence Platform
set COMPOSE_FILE=docker-compose.yml
set PROJECT_NAME=s13-docmind
set API_URL=http://localhost:5125/health
set OPEN_URL=http://localhost:5125/swagger
set DOTNET_URLS=http://localhost:5125

echo Starting S13.DocMind Document Intelligence Platform...

where docker >nul 2>nul
if errorlevel 1 (
  echo Docker is required but not found in PATH.
  exit /b 1
)

REM Use modern "docker compose" if available, else fallback to legacy "docker-compose"
for /f "tokens=*" %%i in ('docker compose version 2^>nul') do set HAS_DOCKER_COMPOSE_CLI=1
if defined HAS_DOCKER_COMPOSE_CLI (
  echo Using "docker compose" CLI
  echo Building and starting infrastructure services...
  docker compose -p %PROJECT_NAME% -f %COMPOSE_FILE% build || goto :error
  docker compose -p %PROJECT_NAME% -f %COMPOSE_FILE% up -d || goto :error
) else (
  where docker-compose >nul 2>nul || goto :nolegacy
  echo Using legacy "docker-compose" CLI
  echo Building and starting infrastructure services...
  docker-compose -p %PROJECT_NAME% -f %COMPOSE_FILE% build || goto :error
  docker-compose -p %PROJECT_NAME% -f %COMPOSE_FILE% up -d || goto :error
)

REM Wait for infrastructure services
echo Waiting for infrastructure services to be ready...
timeout /t 10 >nul

REM Check if dotnet is available for local development
where dotnet >nul 2>nul
if not errorlevel 1 (
  echo .NET 8 SDK found. Starting S13.DocMind API locally...

  set ASPNETCORE_ENVIRONMENT=Development
  set ASPNETCORE_URLS=%DOTNET_URLS%

  echo Restoring dependencies...
  dotnet restore S13.DocMind.csproj || goto :error

  echo Running application...
  start /b dotnet run --project S13.DocMind.csproj --urls="%DOTNET_URLS%"

  REM Wait a bit for the app to start
  timeout /t 5 >nul
) else (
  echo .NET 8 SDK not found. Using containerized deployment...

  echo Building Docker image...
  docker build -t s13-docmind:latest -f Dockerfile ../../.. || goto :error

  REM Stop any existing API container
  for /f "tokens=*" %%i in ('docker ps -q --filter name=s13-docmind-api 2^>nul') do (
    docker stop s13-docmind-api >nul 2>&1
    docker rm s13-docmind-api >nul 2>&1
  )

  echo Starting API container...
  docker run -d --name s13-docmind-api --network s13_docmind_network -p 5000:5000 -e ASPNETCORE_ENVIRONMENT=Development -e "Koan__Data__Providers__mongodb__connectionString=mongodb://mongodb:27017" -e "Koan__Data__Providers__postgresql__connectionString=Host=postgresql;Database=s13docmind_audit;Username=docmind;Password=docmind123" -e "Koan__Data__Providers__weaviate__endpoint=http://weaviate:8080" -e "Koan__Data__Providers__redis__connectionString=redis:6379" -e "Koan__AI__Providers__ollama__baseUrl=http://ollama:11434" s13-docmind:latest || goto :error

  set API_URL=http://localhost:5000/health
  set OPEN_URL=http://localhost:5000/swagger
)

echo Waiting for API to be ready at %API_URL% ...
where curl >nul 2>nul && set HAS_CURL=1
if defined HAS_CURL goto :wait_with_curl
goto :wait_with_powershell

:wait_with_curl
for /l %%i in (1,1,60) do (
  curl -f -s -o NUL "%API_URL%" && goto :success
  timeout /t 2 >nul
)
echo Timed out waiting for %API_URL%.
goto :success

:wait_with_powershell
for /l %%i in (1,1,60) do (
  powershell -NoProfile -Command "try { Invoke-WebRequest -Uri '%API_URL%' -UseBasicParsing -TimeoutSec 2 ^| Out-Null; exit 0 } catch { exit 1 }" >nul 2>&1
  if not errorlevel 1 goto :success
  timeout /t 2 >nul
)
echo Timed out waiting for %API_URL%.
goto :success

:success
start "" "%OPEN_URL%"
echo.
echo SUCCESS: S13.DocMind is starting up!
echo.
echo API Documentation: %OPEN_URL%
echo Health Check: %API_URL%
echo.
echo Infrastructure Services:
echo    MongoDB:    localhost:5120
echo    Weaviate:   localhost:5122
echo    Ollama:     localhost:5124
echo.
echo To stop all services: docker compose -p %PROJECT_NAME% down
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