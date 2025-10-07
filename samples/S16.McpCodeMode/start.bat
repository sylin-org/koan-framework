@echo off
REM S16.McpCodeMode Start Script
REM
REM This script runs the MCP Code Mode sample application.

echo.
echo ======================================
echo Starting S16.McpCodeMode Sample
echo ======================================
echo.

cd /d "%~dp0"

dotnet run --project S16.McpCodeMode.csproj

if errorlevel 1 (
    echo.
    echo ERROR: Failed to start the application
    pause
    exit /b 1
)
