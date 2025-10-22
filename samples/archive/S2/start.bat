@echo off
setlocal enableextensions

REM Simple: rebuild everything and spin up the stack, then open web URLs
set "ROOT=%~dp0"
pushd "%ROOT%" >nul

set "PROJECT_NAME=koan-s2"
set "COMPOSE=compose\docker-compose.yml"
set "API_URL=http://localhost:5054/swagger/index.html"
set "CLIENT_URL=http://localhost:5055"

where docker >nul 2>nul || (
  echo Docker is required but not found in PATH.
  popd & exit /b 1
)

for /f "tokens=*" %%i in ('docker compose version 2^>nul') do set HAS_DOCKER_COMPOSE=1
if defined HAS_DOCKER_COMPOSE (
  docker compose -p %PROJECT_NAME% -f "%COMPOSE%" build || (popd & exit /b 1)
  docker compose -p %PROJECT_NAME% -f "%COMPOSE%" up -d || (popd & exit /b 1)
) else (
  where docker-compose >nul 2>nul || (
    echo docker-compose is not available. Please update Docker Desktop or install docker-compose.
    popd & exit /b 1
  )
  docker-compose -p %PROJECT_NAME% -f "%COMPOSE%" build || (popd & exit /b 1)
  docker-compose -p %PROJECT_NAME% -f "%COMPOSE%" up -d || (popd & exit /b 1)
)

start "" "%API_URL%" >nul 2>&1
start "" "%CLIENT_URL%" >nul 2>&1

popd
exit /b 0
cd /d %~dp0API
call start.bat
