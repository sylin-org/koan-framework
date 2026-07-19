@echo off
setlocal
pushd "%~dp0" >nul
dotnet run --project "FirstUse.csproj" -- %*
set "koan_exit=%errorlevel%"
popd
exit /b %koan_exit%
