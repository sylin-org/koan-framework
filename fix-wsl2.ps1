# WSL2 Reinstall Script
# Fixes Docker Desktop WSL2 errors by completely reinstalling WSL2
# Run as Administrator

#Requires -RunAsAdministrator

Write-Host "=== WSL2 Reinstall Script ===" -ForegroundColor Cyan
Write-Host ""

# Backup warning
Write-Host "WARNING: This will remove all WSL2 distributions!" -ForegroundColor Yellow
Write-Host "Your Docker containers and images will be preserved," -ForegroundColor Yellow
Write-Host "but any data in WSL2 distros (Ubuntu, etc.) will be lost." -ForegroundColor Yellow
Write-Host ""
$confirm = Read-Host "Type 'YES' to continue"
if ($confirm -ne "YES") {
    Write-Host "Cancelled." -ForegroundColor Red
    exit
}

Write-Host ""
Write-Host "Step 1: Stopping Docker Desktop..." -ForegroundColor Green
Stop-Process -Name "Docker Desktop" -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 3

Write-Host "Step 2: Listing current WSL distributions..." -ForegroundColor Green
wsl --list --verbose

Write-Host ""
Write-Host "Step 3: Unregistering all WSL distributions..." -ForegroundColor Green
$distros = wsl --list --quiet
foreach ($distro in $distros) {
    if ($distro.Trim()) {
        Write-Host "  Unregistering: $distro" -ForegroundColor Yellow
        wsl --unregister $distro
    }
}

Write-Host ""
Write-Host "Step 4: Disabling WSL features..." -ForegroundColor Green
dism.exe /online /disable-feature /featurename:Microsoft-Windows-Subsystem-Linux /norestart
dism.exe /online /disable-feature /featurename:VirtualMachinePlatform /norestart

Write-Host ""
Write-Host "Step 5: Re-enabling WSL features..." -ForegroundColor Green
dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart
dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart

Write-Host ""
Write-Host "Step 6: Setting WSL2 as default version..." -ForegroundColor Green
wsl --set-default-version 2

Write-Host ""
Write-Host "Step 7: Updating WSL kernel..." -ForegroundColor Green
wsl --update

Write-Host ""
Write-Host "=== WSL2 Reinstall Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Restart your computer (recommended)" -ForegroundColor White
Write-Host "2. Start Docker Desktop" -ForegroundColor White
Write-Host "3. Docker will recreate WSL distributions automatically" -ForegroundColor White
Write-Host ""
Write-Host "Restart now? (y/n)" -ForegroundColor Yellow
$restart = Read-Host
if ($restart -eq "y") {
    Restart-Computer -Force
} else {
    Write-Host "Remember to restart before using Docker!" -ForegroundColor Yellow
}
