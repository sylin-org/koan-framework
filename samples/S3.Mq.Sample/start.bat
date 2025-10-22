@echo off
setlocal enableextensions

set "ROOT=%~dp0"
pushd "%ROOT%" >nul

set "PROJECT_NAME=koan-s3-mq-sample"
set "COMPOSE_FILE=compose\docker-compose.yml"

where docker >nul 2>nul || (
  echo Docker is required but not found in PATH.
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

popd
exit /b 0
