# WSL2 Quick Fix Script
# Lighter approach - just restarts WSL2 without full reinstall
# Run as Administrator

#Requires -RunAsAdministrator

Write-Host "=== WSL2 Quick Fix ===" -ForegroundColor Cyan
Write-Host ""

Write-Host "Step 1: Stopping Docker Desktop..." -ForegroundColor Green
Stop-Process -Name "Docker Desktop" -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3

Write-Host "Step 2: Shutting down WSL..." -ForegroundColor Green
wsl --shutdown
Start-Sleep -Seconds 2

Write-Host "Step 3: Restarting WSL service..." -ForegroundColor Green
Restart-Service -Name "LxssManager" -Force

Write-Host "Step 4: Updating WSL kernel..." -ForegroundColor Green
wsl --update --web-download

Write-Host "Step 5: Setting WSL2 as default..." -ForegroundColor Green
wsl --set-default-version 2

Write-Host ""
Write-Host "=== Quick Fix Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Now try starting Docker Desktop" -ForegroundColor Yellow
Write-Host ""
Write-Host "If issues persist, run: .\fix-wsl2.ps1 (full reinstall)" -ForegroundColor Gray
