@echo off
setlocal enableextensions

set "ROOT=%~dp0"
pushd "%ROOT%" >nul

set "PROJECT_NAME=koan-s7-meridian"
set "COMPOSE_FILE=docker\compose.yml"
set "SWAGGER_URL=http://localhost:5080/swagger/index.html"

if not exist "storage" (
  mkdir "storage"
)

if not exist "storage\weaviate" (
  mkdir "storage\weaviate"
)

where docker >nul 2>nul || (
  echo Docker is required but not found in PATH.
  popd & exit /b 1
)

where dotnet >nul 2>nul || (
  echo .NET SDK is required but not found in PATH.
  popd & exit /b 1
)

for /f "tokens=*" %%i in ('docker compose version 2^>nul') do set HAS_DOCKER_COMPOSE=1
if defined HAS_DOCKER_COMPOSE (
  docker compose -p %PROJECT_NAME% -f "%COMPOSE_FILE%" build || (popd & exit /b 1)
  docker compose -p %PROJECT_NAME% -f "%COMPOSE_FILE%" up -d || (popd & exit /b 1)
) else (
  where docker-compose >nul 2>nul || (
    echo docker-compose is not available. Please update Docker Desktop or install docker-compose.
    popd & exit /b 1
  )
  docker-compose -p %PROJECT_NAME% -f "%COMPOSE_FILE%" build || (popd & exit /b 1)
  docker-compose -p %PROJECT_NAME% -f "%COMPOSE_FILE%" up -d || (popd & exit /b 1)
)

echo.
echo Meridian stack is running inside Docker (API: http://localhost:5080).
start "" "%SWAGGER_URL%" >nul 2>&1

echo Swagger UI: %SWAGGER_URL%
echo To stop services: run "docker compose -p %PROJECT_NAME% -f ""%COMPOSE_FILE%"" down".
echo.

popd
exit /b 0
