@echo off
setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
pushd "%SCRIPT_DIR%" >nul

set "RID=win-x64"
set "DOTNET_EXTRA="
set "APP_ARGS="

:parse
if "%~1"=="" goto build
if /I "%~1"=="--rid" (
    shift
    if "%~1"=="" goto usage
    set "RID=%~1"
    shift
    goto parse
)
if "%~1"=="--" (
    shift
    goto collect_app
)
set "DOTNET_EXTRA=!DOTNET_EXTRA! %~1"
shift
goto parse

:collect_app
if "%~1"=="" goto build
set "APP_ARGS=!APP_ARGS! %~1"
shift
goto collect_app

:build
set "PUBLISH_ARGS=-c Release -r %RID% --self-contained true -p:PublishAot=true"
if defined DOTNET_EXTRA set "PUBLISH_ARGS=%PUBLISH_ARGS%!DOTNET_EXTRA!"

echo Publishing NativeAOT binary (RID=%RID%)...
dotnet publish g1c1.GardenCoop.csproj %PUBLISH_ARGS%
if errorlevel 1 goto epilogue

set "PUBLISH_DIR=bin\Release\net10.0\%RID%\native"
set "APP=%PUBLISH_DIR%\g1c1.GardenCoop.exe"
if not exist "%APP%" set "APP=%PUBLISH_DIR%\g1c1.GardenCoop"

if not exist "%APP%" (
    echo Native publish completed but executable was not found under "%PUBLISH_DIR%".
    goto epilogue
)

echo Launching %APP%...
if defined APP_ARGS (
    start "" "%APP%" !APP_ARGS!
) else (
    start "" "%APP%"
)

goto epilogue

:usage
echo Usage: start-native.bat [--rid <runtime-identifier>] [-- ^<dotnet-publish-args^>] [-- <app-args>]
goto epilogue

:epilogue
popd >nul
endlocal
exit /b 0
