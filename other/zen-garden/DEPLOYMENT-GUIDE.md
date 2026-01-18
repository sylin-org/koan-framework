# Zen Garden Deployment Guide

**Quick Start:** Build binaries → Create USB → Deploy Stone → Test

---

## Prerequisites

### Development Machine (Windows)

- **PowerShell 5.1+** (Windows 10/11)
- **Rust toolchain** (rustup.rs)
- **WSL2** (for Linux builds)
- **Git** (for cloning repository)
- **8GB+ USB drive**

### WSL2 Setup (One-Time)

```powershell
# Install WSL2 with Ubuntu
wsl --install

# Inside WSL, install Rust
wsl
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
source ~/.cargo/env
exit
```

---

## Step 1: Build Distribution Binaries

### Option A: Automated Build (Recommended)

```powershell
# Navigate to installer directory
cd other\zen-garden\installer

# Build release binaries (optimized, production-ready)
.\build-dist.ps1 -Release

# Verify artifacts created
ls ..\bin\
# Expected output:
#   garden-moss (15MB)
#   garden-rake (8MB)
#   garden-rake.exe (10MB)
```

### Option B: Manual Build

```powershell
# From zen-garden root
cd other\zen-garden

# Build Linux binaries via WSL
wsl bash -c "cargo build --release --bin garden-moss"
wsl bash -c "cargo build --release --bin garden-rake"

# Build Windows binary natively
cargo build --release --bin garden-rake --target x86_64-pc-windows-msvc

# Copy to bin/
mkdir bin -Force
copy target\release\garden-moss bin\
copy target\release\garden-rake bin\
copy target\x86_64-pc-windows-msvc\release\garden-rake.exe bin\
```

---

## Step 2: Create Bootable USB

### Interactive Mode (Recommended for First Use)

```powershell
cd other\zen-garden\installer

# Auto-detect USB drive
.\NewStone.ps1

# Follow prompts:
# 1. Select USB drive (e.g., E:)
# 2. Choose stone name (or auto-generate)
# 3. Select offerings (optional)
```

### Command-Line Mode

```powershell
# Specify all parameters
.\NewStone.ps1 `
    -UsbDrive "E:" `
    -StoneName "stone-lab-01" `
    -Offering mongodb,redis

# Skip confirmation prompts
.\NewStone.ps1 -UsbDrive "E:" -Force
```

### What Gets Written to USB

```
USB:\
├── preseed.cfg                    # Debian auto-install config
├── garden-moss                    # Garden-Moss Daemon binary (15MB)
├── garden-rake                    # Rake CLI binary (8MB)
├── garden-moss.service            # Systemd unit for Garden-Moss
├── garden-moss.toml               # Garden-Moss configuration
├── docker-compose.yml             # Service definitions (if offerings selected)
├── zen-garden-services.service    # Systemd unit for compose (if offerings)
└── stone-setup.sh                 # Reference script (not executed)
```

---

## Step 3: Deploy Stone

### Physical Machine Boot

1. **Insert USB** into target machine
2. **Boot from USB** (press F12/F2/Del during startup)
3. **Auto-install begins** (no interaction needed)
4. **Wait 10-15 minutes** (Debian install + packages)
5. **Stone reboots** automatically
6. **Login via SSH** or console:
   - Username: `stone`
   - Password: `stone` (lab default - change in production)

### Post-Boot Verification

```bash
# SSH to stone (find IP via router or mDNS)
ssh stone@stone-lab-01.local

# Check Garden-Moss Service
sudo systemctl status garden-moss

# Expected output:
● garden-moss.service - Garden-Moss Daemon - Zen Garden Stone Manager
   Loaded: loaded (/etc/systemd/system/garden-moss.service; enabled)
   Active: active (running) since ...
```

### Check Binaries Installed

```bash
# Verify binaries in PATH
which garden-moss
# Expected: /usr/local/bin/garden-moss

which garden-rake
# Expected: /usr/local/bin/garden-rake

# Check versions
garden-moss --version
garden-rake --version
```

### Test Garden-Moss API

```bash
# Local API test
curl http://localhost:3001/info | jq

# Expected output:
{
  "stone_name": "stone-lab-01",
  "api_endpoint": "http://stone-lab-01:3001",
  "health": "Healthy",
  "moss_version": "0.2.0",
  "resources": {
    "cpu_percent": 15.2,
    "memory_used_mb": 512,
    ...
  }
}
```

---

## Step 4: Test from Windows Client

### Install garden-rake on Windows

```powershell
# Copy Windows binary to PATH directory
copy other\zen-garden\bin\garden-rake.exe $env:USERPROFILE\bin\

# Add to PATH (if not already)
$path = [System.Environment]::GetEnvironmentVariable("PATH", "User")
[System.Environment]::SetEnvironmentVariable("PATH", "$path;$env:USERPROFILE\bin", "User")

# Restart PowerShell, then verify
garden-rake --version
```

### Discover Stone via mDNS (Linux Stone)

```powershell
# Auto-discover (tries mDNS, falls back to UDP)
garden-rake list

# Expected output:
Discovering Stones... ✓ (found stone-lab-01)

Stone: stone-lab-01
Health: Healthy
Services: 0 installed
```

### Discover via UDP Broadcast (Windows Stone or Fallback)

```powershell
# Explicit UDP discovery
garden-rake list --broadcast

# If firewall blocks, configure:
New-NetFirewallRule -DisplayName "Zen Garden UDP" `
  -Direction Inbound -Protocol UDP -LocalPort 3004,3005 -Action Allow
```

---

## Step 5: Deploy Services

### Offer a Service

```powershell
# Install MongoDB on discovered Stone
garden-rake offer mongodb

# Expected output:
Offering mongodb on stone-lab-01...
✓ Installation started
✓ Container mongodb running
✓ Health check passed
```

### List Services

```powershell
garden-rake list

# Expected output:
Stone: stone-lab-01
Services:
  mongodb - Running (27017, agnostic: 8080)
```

### Observe All Stones (Visual Dashboard)

```powershell
garden-rake observe --all

# Expected output (live-updating):
┌─────────────────────────────────────────────────────────┐
│ Stone: stone-lab-01 | Health: ● | CPU: 15% | Mem: 2.1GB │
├─────────────────────────────────────────────────────────┤
│ mongodb      ● | 27017 | CPU: 5%  | Mem: 512MB          │
│ redis        ● | 6379  | CPU: 1%  | Mem: 128MB          │
└─────────────────────────────────────────────────────────┘
```

---

## Troubleshooting

### Stone Won't Boot from USB

**Issue:** BIOS not detecting USB drive

**Solutions:**
- Ensure USB drive formatted as MBR/GPT hybrid
- Try different USB port (prefer USB 2.0 for compatibility)
- Check BIOS boot order (USB should be first)
- Verify GRUB files written to USB

### Garden-Moss Service Fails to Start

**Check logs:**
```bash
sudo journalctl -u garden-moss -f
```

**Common causes:**
- Docker not running: `sudo systemctl start docker`
- Binary permissions: `sudo chmod +x /usr/local/bin/garden-moss`
- Config file missing: `ls -la /etc/zen-garden/garden-moss.toml`

### garden-rake Can't Discover Stone

**Windows Firewall:**
```powershell
# Allow UDP discovery
New-NetFirewallRule -DisplayName "Zen Garden Discovery" `
  -Direction Inbound,Outbound -Protocol UDP -LocalPort 3004,3005 -Action Allow
```

**Linux Avahi Check:**
```bash
# Verify mDNS running
sudo systemctl status avahi-daemon

# Browse for Garden-Moss Services
avahi-browse -a | grep garden-moss
```

**Fallback to Manual Endpoint:**
```powershell
# If discovery fails, specify endpoint
garden-rake list --at http://192.168.1.100:3001
```

### Binary Not Found in PATH

**Stone (Linux):**
```bash
# Add to PATH if needed
echo 'export PATH="$PATH:/usr/local/bin"' >> ~/.bashrc
source ~/.bashrc
```

**Windows:**
```powershell
# Verify PATH
$env:PATH -split ';' | Select-String "bin"

# Re-add if missing
[System.Environment]::SetEnvironmentVariable("PATH", 
  "$env:PATH;$env:USERPROFILE\bin", "User")
```

---

## Production Hardening

### Change Default Password

```bash
# On Stone after first login
passwd stone
# Enter new password (not 'stone')
```

### Enable Pond Security (Phase 3)

```bash
# Edit moss config
sudo nano /etc/zen-garden/garden-moss.toml

# Enable Pond
[pond]
enabled = true
require_mtls = true

# Restart Moss
sudo systemctl restart garden-moss
```

### Restrict API Access

```bash
# Firewall: only allow local network
sudo ufw allow from 192.168.1.0/24 to any port 3001
sudo ufw enable
```

---

## Verification Checklist

- [ ] **Binaries built**: `ls other\zen-garden\bin\` shows garden-moss, garden-rake, garden-rake.exe
- [ ] **USB created**: NewStone.ps1 completes successfully
- [ ] **Stone boots**: Debian auto-install runs without errors
- [ ] **Garden-Moss running**: `systemctl status garden-moss` shows active
- [ ] **Rake in PATH**: `garden-rake --version` works on Stone
- [ ] **Windows client**: `garden-rake --version` works on dev machine
- [ ] **Discovery works**: `garden-rake list` finds Stone
- [ ] **Service deployment**: `garden-rake offer mongodb` installs successfully
- [ ] **Observe works**: `garden-rake observe --all` shows live metrics

---

## Next Steps

1. **Deploy multiple Stones**: Repeat USB creation with different names
2. **Test garden-wide ops**: `garden-rake upgrade --all`
3. **Monitor resources**: `garden-rake observe` dashboard
4. **Add custom offerings**: Create manifests in `manifests/` directory
5. **Enable Pond security**: Phase 3 mTLS authentication

---

## Support & Documentation

- **Technical Spec**: [other/zen-garden/docs/TECHNICAL-SPEC.md](../docs/TECHNICAL-SPEC.md)
- **Build Distribution**: [other/zen-garden/BUILD-DISTRIBUTION.md](../BUILD-DISTRIBUTION.md)
- **Development Plan**: [other/zen-garden/DEVELOPMENT-PLAN.md](../DEVELOPMENT-PLAN.md)
- **Issues**: File on GitHub with logs from `journalctl -u garden-moss`
