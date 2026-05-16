@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%" >nul

set "DOTNET_ENVIRONMENT=Development"
set "ASPNETCORE_ENVIRONMENT=Development"

set "CMD=dotnet run --project ""g1c1.GardenCoop.csproj"""
if "%~1"=="" goto run

:collect
if "%~1"=="" goto run
set "CMD=%CMD% %~1"
shift
goto collect

:run
start "" cmd /c "%CMD%"

popd >nul
endlocal
exit /b 0