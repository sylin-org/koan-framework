@echo off
setlocal

pushd "%~dp0" >nul
dotnet run --project S17.ZenGardenTest.csproj
set "EXIT_CODE=%ERRORLEVEL%"
popd >nul

echo.
pause
endlocal
exit /b %EXIT_CODE%
