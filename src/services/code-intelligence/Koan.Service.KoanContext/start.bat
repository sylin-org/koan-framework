@echo off
echo ============================================
echo  Koan.Service.KoanContext
echo  Single-Server Architecture
echo ============================================
echo.

REM Change to service root directory
cd /d "%~dp0"

echo [1/3] Installing dependencies (if needed)...
cd ui
if not exist "node_modules\" (
    echo Installing npm packages...
    call npm ci
    if %errorlevel% neq 0 (
        echo.
        echo [ERROR] npm install failed!
        pause
        exit /b 1
    )
) else (
    echo Dependencies already installed (skipping npm ci)
)

echo.
echo [2/3] Starting UI build watcher (background)...
start /B "" npm run build:watch

echo.
echo [3/3] Starting ASP.NET service...
timeout /t 3 >nul
cd ..\Service
dotnet watch run

REM Cleanup: Kill background npm process when dotnet exits
echo.
echo Cleaning up background processes...
taskkill /F /IM node.exe /T >nul 2>&1

pause