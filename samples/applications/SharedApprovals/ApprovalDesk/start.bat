@echo off
dotnet run --project "%~dp0ApprovalDesk.csproj" -- --urls http://127.0.0.1:5101 %*
exit /b %errorlevel%
