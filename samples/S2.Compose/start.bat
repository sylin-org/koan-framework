@echo off
setlocal enableextensions
pushd %~dp0
  echo Building and starting S2 compose stack (mongo + api)...
  docker compose up --build
popd
endlocal
