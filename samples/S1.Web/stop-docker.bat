@echo off
setlocal
REM Stop and remove the S1 container

docker rm -f Koan-s1 >nul 2>nul
endlocal
