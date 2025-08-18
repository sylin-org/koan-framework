@echo off
setlocal
REM Simple start for S1.Web: optional URL, run in foreground (shows logs), open browser after short delay.

set "URL=%~1"
if "%URL%"=="" set "URL=http://localhost:5044"

pushd "%~dp0"

REM Best-effort: stop any previous published EXE (safe to ignore failures)
taskkill /F /IM "S1.Web.exe" /T >nul 2>nul

REM Open the browser after a short delay; avoid complex health checks for simplicity
start "" cmd /c "timeout /t 2 >nul & start "" "%URL%""

echo Running S1.Web at %URL% (press Ctrl+C to stop). Logs will appear below.
dotnet run -p:UseAppHost=false --urls "%URL%"

popd
endlocal
