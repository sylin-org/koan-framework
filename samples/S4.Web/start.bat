@echo off
setlocal enableextensions

REM Minimal: bring up the S4 compose stack (mongo + s4) in detached mode.
pushd "%~dp0"
docker compose up -d --build
set ERR=%ERRORLEVEL%
popd
exit /b %ERR%
