@echo off
setlocal enabledelayedexpansion
set VERSION=0.1.0-preview
if not "%1"=="" set VERSION=%1
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0pack-templates.ps1" -Version %VERSION% -Install
