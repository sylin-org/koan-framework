# Stone Installation Flow with Pre-Install Manifest

## Complete Workflow

### 1. USB Creation

```powershell
# Create USB with pre-configured services
.\NewStone.ps1 -UsbDrive "G:" -Offering mongodb,redis,postgresql -Force
```

**What happens:**
- NewStone.ps1 generates `garden-moss-preinstall.json` on USB
- Contains list of services to auto-install on first boot
- No more docker-compose.yml - moss handles deployment

### 2. Hardware Installation

1. Boot target machine from USB
2. Debian installer runs automatically (preseed)
3. System installs Docker, moss, garden-rake
4. Machine reboots into fresh Stone

### 3. First Boot - Automatic Service Deployment

**Garden-Moss Startup Sequence:**

```
1. Garden-Moss starts via systemd (garden-moss.service)
2. Waits for Docker daemon (up to 60s with retries)
3. Detects /home/stone/garden-moss-preinstall.json
4. Spawns async batch job with UUIDv7 job_id
5. Installs services one by one
6. Removes manifest ONLY after job completes
```

**Resilience:**
- ✅ **Docker not ready?** Moss waits up to 60 seconds (30 retries × 2s)
- ✅ **Moss crashes?** systemd restarts it (`Restart=always`)
- ✅ **Shutdown during install?** Manifest persists, job resumes on reboot
- ✅ **Service fails?** Tracked in `job.failed` map, other services continue

### 4. Monitoring Installation Progress

```bash
# Check job status
curl http://stone-01:3001/api/jobs

# Get specific job details
curl http://stone-01:3001/api/jobs/<job_id>

# Example response:
{
  "id": "018d3c6f-8e4c-7890-a123-456789abcdef",
  "status": "Running",
  "offerings": ["mongodb", "redis", "postgresql"],
  "completed": ["mongodb", "redis"],
  "failed": {},
  "started_at": "2026-01-15T23:00:00Z"
}
```

### 5. Using garden-rake

Once installation completes, use garden-rake to manage services:

```bash
# garden-rake automatically connects to local Docker

# List available offerings
garden-rake list

# Install additional service
garden-rake offer --service weaviate

# Remove service
garden-rake remove --service mongodb

# Check status
garden-rake status
```

## Manifest Format

```json
{
  "version": "1.0",
  "offerings": ["mongodb", "redis", "postgresql"],
  "auto_install": true,
  "created_at": "2026-01-15T12:00:00Z",
  "created_by": "NewStone.ps1"
}
```

## Failure Scenarios & Recovery

### Docker Daemon Not Ready

**Problem:** Fresh install, Docker takes time to start

**Solution:** Moss waits up to 60 seconds with exponential backoff
```
[WARN] Docker not ready, waiting 2s before retry... (attempt 1/30)
[WARN] Docker not ready, waiting 2s before retry... (attempt 2/30)
...
[INFO] Docker daemon connected successfully
```

### Moss Crashes During Installation

**Problem:** System instability, OOM, etc.

**Solution:** systemd restarts garden-moss automatically
```bash
# garden-moss.service configuration:
Restart=always
RestartSec=10
```

Manifest remains at `/home/stone/garden-moss-preinstall.json` and is re-processed.

### Power Loss During Installation

**Problem:** Shutdown/reboot while services installing

**Solution:** 
1. Manifest persists (not removed until job completes)
2. On reboot, moss detects manifest again
3. Re-spawns installation job
4. Already-installed services skipped (idempotent)

### Service Installation Failure

**Problem:** One service fails to install

**Solution:**
```json
{
  "status": "Completed",
  "completed": ["mongodb", "redis"],
  "failed": {
    "postgresql": "Failed to pull image: network timeout"
  }
}
```

Other services continue installing. Failed services tracked in job history.

## File Locations

| File | Path | Purpose |
|------|------|---------|  
| **Pre-install manifest** | `/home/stone/garden-moss-preinstall.json` | Auto-install list |
| **Garden-Moss binary** | `/usr/local/bin/garden-moss` | Service daemon |
| **garden-rake** | `/usr/local/bin/garden-rake` | CLI tool |
| **Systemd service** | `/etc/systemd/system/garden-moss.service` | Daemon config |
| **Job history** | In-memory (lost on moss restart) | Runtime only |

## Debugging

### Check moss logs
```bash
# Live tail
journalctl -u garden-moss.service -f

# Recent logs
journalctl -u garden-moss.service -n 100

# Check for pre-install manifest processing
journalctl -u garden-moss.service | grep "pre-install"
```

### Check Docker status
```bash
# Is Docker running?
systemctl status docker

# Can moss access Docker?
sudo -u stone docker ps
```

### Manual manifest processing
```bash
# Check if manifest exists
ls -la /home/stone/garden-moss-preinstall.json

# View manifest content
cat /home/stone/garden-moss-preinstall.json | jq

# Manually trigger (for testing)
sudo systemctl restart garden-moss.service
```

### Verify installed services
```bash
# List all zen-offering containers
docker ps --filter "name=zen-offering-"

# Check specific service
docker logs zen-offering-mongodb
```

## Advanced Configuration

### Custom Manifest Location

Edit garden-moss.service:
```ini
Environment="MOSS_MANIFEST_PATH=/custom/path/manifest.json"
```

### Disable Auto-Install

Modify manifest before boot:
```json
{
  "auto_install": false
}
```

Moss will skip automatic installation.

### Timeout Configuration

Currently hardcoded: 30 retries × 2s = 60s max wait for Docker

To modify, edit `src/linux/moss/src/main.rs`:
```rust
let max_retries = 60; // Increase to 120s
```

## Testing the Flow

### Local Test (Windows + Docker Desktop)

1. Generate manifest:
```powershell
$manifest = @{
    version = "1.0"
    offerings = @("mongodb", "redis")
    auto_install = $true
    created_at = (Get-Date -Format "o")
} | ConvertTo-Json
```

2. Copy to test location
3. Build and run moss in Docker container
4. Monitor job progress

### Integration Test (Real Hardware)

1. Create USB with offerings
2. Install on e-waste machine
3. Boot and SSH in
4. Monitor: `watch -n 1 'curl -s http://localhost:3001/api/jobs | jq'`
5. Verify: `docker ps | grep zen-offering`

## Comparison: Old vs New

| Aspect | Old (docker-compose.yml) | New (garden-moss-preinstall.json) |
|--------|--------------------------|----------------------------|
| **Creation** | Generated at USB build time | Generated at USB build time |
| **Deployment** | systemd runs `docker-compose up -d` | Moss async job system |
| **Progress** | ❌ Opaque (check logs) | ✅ Real-time API (`/api/jobs`) |
| **Resilience** | ❌ Single-shot, no retry | ✅ Survives crashes, retries |
| **Observability** | ❌ Manual log inspection | ✅ Structured job status |
| **Docker Ready** | ❌ systemd waits, may fail | ✅ Moss waits 60s with retries |
| **Shutdown Safety** | ❌ Manifest removed immediately | ✅ Persists until completion |
| **Partial Failure** | ❌ All-or-nothing | ✅ Tracks per-service status |

## Summary

The new manifest-based system provides:

1. **Reliability**: Survives crashes, shutdowns, Docker delays
2. **Observability**: Real-time job tracking via API
3. **Resilience**: Automatic retries, graceful degradation
4. **Idempotency**: Safe to re-run, skips existing services
5. **Simplicity**: Single JSON manifest, no complex orchestration

Perfect for unattended e-waste machine deployments where network/hardware may be unreliable.
