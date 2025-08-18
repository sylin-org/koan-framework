@echo off
setlocal enableextensions

REM Kill any existing S2.Api or dotnet process hosting S2.Api
for /f "tokens=2" %%p in ('tasklist /fi "imagename eq dotnet.exe" /fo list ^| findstr /i "PID:"') do (
  REM noop - keeping simple for now
)

set ASPNETCORE_ENVIRONMENT=Development
set ASPNETCORE_URLS=http://localhost:5054

REM Default dev Mongo connection if not already set
if "%ConnectionStrings__Default%"=="" set ConnectionStrings__Default=mongodb://localhost:5055
if "%Sora__Data__Mongo__Database%"=="" set Sora__Data__Mongo__Database=s2

pushd %~dp0
  echo Running S2.Api at %ASPNETCORE_URLS% with DB %Sora__Data__Mongo__Database%
  dotnet run -p:UseAppHost=false --project S2.Api.csproj --no-launch-profile --urls "%ASPNETCORE_URLS%"
popd

endlocal
