@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%" >nul

set "DOTNET_ENVIRONMENT=Development"
set "ASPNETCORE_ENVIRONMENT=Development"
if not defined ASPNETCORE_URLS set "ASPNETCORE_URLS=http://localhost:4998"

set "DOTNET_CMD=dotnet run --project ""S1.Web.csproj"" --no-launch-profile"

if not "%~1"=="" (
    set "DOTNET_CMD=%DOTNET_CMD% -- %*"
)

start "" cmd /c "%DOTNET_CMD%"

popd
endlocal
exit /b 0
