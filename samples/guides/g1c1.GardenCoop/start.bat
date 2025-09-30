@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%"

set "DOTNET_CMD=dotnet run --project ""g1c1.GardenCoop.csproj"""
if "%~1"=="" (
    rem no additional arguments
) else (
    set "DOTNET_CMD=%DOTNET_CMD% -- %*"
)

start "" cmd /c "%DOTNET_CMD%"

popd
endlocal
