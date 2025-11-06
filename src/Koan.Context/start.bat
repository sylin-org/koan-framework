@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%"

set "DOTNET_ENVIRONMENT=Development"
set "ASPNETCORE_ENVIRONMENT=Development"

set "DOTNET_CMD=dotnet run"
if "%~1"=="" (
    rem no additional arguments
) else (
    set "DOTNET_CMD=%DOTNET_CMD% -- %*"
)

start "" cmd /c "%DOTNET_CMD%"

popd
endlocal