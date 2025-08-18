@echo off
setlocal
REM Danger: deletes persisted data for S1.Web sample
pushd "%~dp0"
if exist data (
  echo Deleting %CD%\data ...
  rmdir /S /Q data
) else (
  echo No data directory found.
)
popd
endlocal
