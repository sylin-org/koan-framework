@echo off
dotnet run --project "%~dp0ExpenseDesk.csproj" -- --urls http://127.0.0.1:5102 %*
exit /b %errorlevel%
