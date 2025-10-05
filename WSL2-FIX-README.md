# WSL2 Fix Scripts

Two PowerShell scripts to fix WSL2 and Docker Desktop issues.

## Quick Fix (Try This First) ⚡

**File**: `fix-wsl2-quick.ps1`

**What it does**:
- Stops Docker Desktop
- Shuts down WSL
- Restarts WSL service
- Updates WSL kernel
- Sets WSL2 as default

**Usage**:
```powershell
# Open PowerShell as Administrator
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\fix-wsl2-quick.ps1
```

**When to use**:
- Docker fails to start with WSL errors
- "Docker Desktop is unable to start" errors
- WSL distribution issues
- After Windows updates

**Time**: ~30 seconds

---

## Full Reinstall (Nuclear Option) 💥

**File**: `fix-wsl2.ps1`

**What it does**:
- **Removes all WSL distributions** (Docker distros will be recreated)
- Disables WSL features
- Re-enables WSL features
- Updates WSL kernel
- Sets WSL2 as default

**Usage**:
```powershell
# Open PowerShell as Administrator
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
.\fix-wsl2.ps1
# Type YES to confirm
# Choose whether to restart
```

**When to use**:
- Quick fix didn't work
- Corrupted WSL installations
- Persistent "un-mounting WSL VHDX" errors
- Complete WSL reset needed

**Time**: ~2 minutes + restart

---

## Fixing Your Current Error

Your error:
```
Docker Desktop is unable to start
terminating main distribution: un-mounting data disk: unmounting WSL VHDX
```

### Recommended Steps:

1. **Try Quick Fix First**:
   ```powershell
   .\fix-wsl2-quick.ps1
   ```

2. **If that fails, Full Reinstall**:
   ```powershell
   .\fix-wsl2.ps1
   ```

3. **After fix, test S14**:
   ```bash
   cd samples\S14.AdapterBench
   .\start.bat
   ```

---

## Manual Alternative

If scripts don't work, manual steps:

```powershell
# Stop Docker
Stop-Process -Name "Docker Desktop" -Force

# Shutdown WSL
wsl --shutdown

# Restart WSL service
Restart-Service LxssManager -Force

# Update WSL
wsl --update

# Restart computer
Restart-Computer
```

---

## After Fix

Once WSL2 is working:

```bash
# Verify WSL2 is running
wsl --list --verbose

# Should show WSL2 distros like:
#   NAME                   STATE           VERSION
# * docker-desktop         Running         2
#   docker-desktop-data    Running         2

# Start Docker Desktop normally
# Then test S14.AdapterBench
cd samples/S14.AdapterBench
./start.bat
```

---

## Prevention

To avoid future issues:

1. **Windows Update**: Keep Windows updated
2. **WSL Update**: Run `wsl --update` monthly
3. **Clean Shutdown**: Always stop Docker before shutting down Windows
4. **Disk Space**: Ensure C: drive has 10GB+ free space for WSL VHDX

---

## Troubleshooting

### Error: "This operation requires elevation"
- Run PowerShell as Administrator (Right-click → Run as Administrator)

### Error: "Execution policy"
```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

### Error: "WSL 2 requires an update to its kernel component"
```powershell
wsl --update --web-download
```

### Docker still won't start
1. Uninstall Docker Desktop
2. Run `fix-wsl2.ps1` (full reinstall)
3. Restart computer
4. Reinstall Docker Desktop
