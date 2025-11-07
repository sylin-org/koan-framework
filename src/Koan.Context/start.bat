@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%"

echo Checking for existing Koan.Context processes...

rem Kill any existing Koan.Context.exe processes
taskkill /F /IM koan.context.exe >nul 2>&1
if %ERRORLEVEL% EQU 0 (
    echo Killed existing Koan.Context process
) else (
    echo No existing Koan.Context process found
)

echo Starting Koan.Context...

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