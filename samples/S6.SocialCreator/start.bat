@echo off
setlocal

REM Change to this script's directory
cd /d "%~dp0"

REM Optional: pass port as first arg, default 5086
if "%1"=="" (
  set "PORT=5086"
) else (
  set "PORT=%~1"
)

set "ASPNETCORE_URLS=http://localhost:%PORT%"
set "ASPNETCORE_ENVIRONMENT=Development"
set "DOTNET_ENVIRONMENT=Development"

echo.
echo Starting S6.SocialCreator
echo   URL: %ASPNETCORE_URLS%
echo   ENV: %ASPNETCORE_ENVIRONMENT%
echo.

REM Run the app
 dotnet run --verbosity minimal --no-launch-profile --configuration Debug

endlocal
