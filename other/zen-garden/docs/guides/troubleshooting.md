# Troubleshooting Zen Garden

**Purpose:** Common problems and solutions for operators and maintainers.  
**Audience:** Operators diagnosing production issues, developers debugging installations.

---

## Table of Contents

1. [Discovery Issues](#discovery-issues)
2. [Service Deployment Failures](#service-deployment-failures)
3. [Connection Problems](#connection-problems)
4. [Moss Daemon Issues](#moss-daemon-issues)
5. [Container Health Problems](#container-health-problems)
6. [Networking and Firewall](#networking-and-firewall)
7. [USB Installer Issues](#usb-installer-issues)
8. [Pond Security Issues](#pond-security-issues)
9. [Performance and Resource Issues](#performance-and-resource-issues)
10. [Diagnostic Commands Quick Reference](#diagnostic-commands-quick-reference)

---

## Discovery Issues

### Stone Not Discovered by Rake

**Symptom:** `garden-rake discover` returns "No Stones found"

**Diagnosis:**

```bash
# Test network reachability
ping stone-01.local

# If hostname fails, try IP
ping 192.168.1.42

# Test mDNS resolution (Linux)
avahi-resolve -n stone-01.local

# Test mDNS resolution (macOS)
dns-sd -G v4 stone-01.local

# Test Moss HTTP API directly
curl http://stone-01.local:7185/health
# Expected: {"status":"healthy","moss_version":"0.1.0"}
```

**Solutions:**

#### Same Subnet?

mDNS broadcasts limited to local network (192.168.1.0/24).

```bash
# Check your machine's subnet
ip addr show  # Linux
ifconfig      # macOS

# Stone and client must share first 3 octets
# Your machine: 192.168.1.10
# Stone:        192.168.1.42  ✓ OK
# Stone:        192.168.2.42  ✗ Different subnet
```

**Fix for cross-subnet:**

- Use Lantern registry: `garden-rake discover --via-lantern http://lantern:7186`
- Use direct addressing: `garden-rake status --at stone-01:7185`

#### mDNS Service Not Running?

**Linux:**

```bash
# Check Avahi daemon
systemctl status avahi-daemon

# If inactive, start it
sudo systemctl start avahi-daemon
sudo systemctl enable avahi-daemon

# Verify mDNS working
avahi-browse -a  # Lists all mDNS services
```

**macOS:** Built-in, no setup required (Bonjour).

**Windows:**

```bash
# Install Bonjour Print Services
# Download: https://support.apple.com/kb/DL999

# Or use UDP broadcast discovery (no mDNS needed)
garden-rake discover --via-udp
```

#### Firewall Blocking?

```bash
# Check if firewall blocking mDNS (UDP 5353)
sudo ufw status

# Allow mDNS
sudo ufw allow 5353/udp

# Allow Moss HTTP API (TCP 7185)
sudo ufw allow 7185/tcp
```

### Multiple Stones Discovered But Not All

**Symptom:** `garden-rake discover` finds stone-01 but not stone-02

**Diagnosis:**

```bash
# Check if Stone is powered on
ping stone-02.local

# SSH to missing Stone (if accessible)
ssh stone@stone-02.local

# Check Moss daemon status
sudo systemctl status garden-moss
```

**Solutions:**

#### Moss Daemon Not Running

```bash
# SSH to Stone
ssh stone@stone-02.local

# Start Moss daemon
sudo systemctl start garden-moss

# Enable auto-start on boot
sudo systemctl enable garden-moss

# Check logs for errors
sudo journalctl -u garden-moss -n 50
```

#### Stone Registry Out of Sync

Rake uses localhost cache. If Stone added recently, cache may be stale.

```bash
# Query topology directly from any Stone
curl http://stone-01.local:7185/api/garden/stones

# If stone-02 listed there but not in Rake discovery:
# Wait 30-60s for background sync
# Or force rediscovery:
garden-rake discover --refresh
```

---

## Service Deployment Failures

### Service Won't Install

**Symptom:** `garden-rake offer mongodb --to stone-01` fails with error

#### Port Conflict

```
✗ Error: Port 27017 already in use by 'redis'
```

**Diagnosis:**

```bash
# Check port usage on Stone
curl http://stone-01:7185/api/services

# Output shows which service using which ports
```

**Solutions:**

1. **Remove conflicting service:**

```bash
garden-rake take-away redis --at stone-01
garden-rake offer mongodb --at stone-01
```

2. **Use different port** (custom template):

Edit `/etc/zen-garden/templates/custom/mongodb-alt.yaml` and change port to 27018.

#### Incompatible Hardware

```
✗ Compatibility check failed: weaviate requires AVX2 (x86_64), but stone-raspberry-pi is ARM64
```

**Solutions:**

```bash
# Option 1: Use compatible alternative
garden-rake offer qdrant --at stone-raspberry-pi  # ARM64-compatible

# Option 2: Deploy to different Stone
garden-rake offer weaviate --at stone-intel-nuc   # x86_64 with AVX2

# Option 3: Auto-search compatible Stone
garden-rake offer weaviate --at stone-raspberry-pi --anywhere-on-fail
# Rake will automatically suggest compatible Stone
```

#### Image Pull Failure

```
✗ Error: Failed to pull image mongo:7 (manifest not found)
```

**Diagnosis:**

```bash
# SSH to Stone
ssh stone@stone-01.local

# Test image pull manually
docker pull mongo:7

# Common issues:
# - Network connectivity (can't reach Docker Hub)
# - Rate limiting (too many pulls from IP)
# - Invalid image tag (typo in version)
```

**Solutions:**

1. **Check network:**

```bash
# Test internet connectivity
curl https://hub.docker.com/

# Test DNS resolution
nslookup hub.docker.com
```

2. **Use specific version:**

```bash
# Instead of "latest" or "7", use exact version
garden-rake offer mongodb --version 7.0.4 --at stone-01
```

3. **Docker Hub rate limiting:**

```bash
# Authenticate with Docker Hub (increases rate limit)
docker login

# Or use mirror/cache registry
# Edit /etc/docker/daemon.json:
{
  "registry-mirrors": ["https://mirror.gcr.io"]
}
sudo systemctl restart docker
```

### Service Starts Then Immediately Exits

**Symptom:** `garden-rake observe` shows service as "Exited"

**Diagnosis:**

```bash
# Stream logs in real-time
garden-rake watch offering mongodb logs

# Dump last 100 lines
garden-rake watch offering mongodb logs --tail 100

# SSH to Stone and check Docker
ssh stone@stone-01.local
docker ps -a  # Shows all containers including stopped
docker logs <container_id>
```

**Common issues:**

#### Volume Permissions

```
Error: Permission denied: /data/db
```

**Solution:**

```bash
# SSH to Stone
ssh stone@stone-01.local

# Fix permissions
docker exec <container_id> chown -R mongodb:mongodb /data/db

# Restart container
docker restart <container_id>
```

#### Resource Exhaustion

```
Error: Cannot allocate memory
```

**Diagnosis:**

```bash
# Check Stone resources
curl http://stone-01:7185/info

# Output shows:
# {
#   "resources": {
#     "memory": {
#       "used_percent": 95.0,  ← Stone out of RAM
#       "available_bytes": 209715200  ← Only 200MB free
#     }
#   }
# }
```

**Solutions:**

1. **Stop unused services:**

```bash
garden-rake rest redis --at stone-01  # Temporarily stop
garden-rake take-away postgresql --at stone-01  # Remove completely
```

2. **Deploy to different Stone:**

```bash
garden-rake offer mongodb --at stone-02  # Stone with more resources
```

---

## Connection Problems

### Connection String Not Resolving

**Symptom:** Application cannot resolve `zen-garden:mongodb`

**Diagnosis:**

```bash
# Test mDNS resolution manually
avahi-resolve -n stone-01.local  # Linux
dns-sd -G v4 stone-01.local      # macOS

# Check service announcement
garden-rake observe --at stone-01
# Output should show mongodb with port 27017
```

**Solutions:**

#### Application Not Using Discovery

Most client libraries require explicit discovery integration.

**Node.js example (incorrect):**

```javascript
// This will NOT work - connection string not resolved
const client = new MongoClient('zen-garden:mongodb/myapp');
```

**Node.js example (correct):**

```javascript
// Use zen-garden discovery library
const { resolveConnectionString } = require('zen-garden-client');

const uri = await resolveConnectionString('zen-garden:mongodb/myapp');
// Returns: mongodb://stone-01.local:27017/myapp

const client = new MongoClient(uri);
```

**Alternative: Use native hostname:**

```javascript
// Direct connection (bypasses discovery)
const uri = 'mongodb://stone-01.local:27017/myapp';
const client = new MongoClient(uri);
```

#### Service Not Announced

```bash
# Check if service running
garden-rake observe --at stone-01

# If service shows "Running" but no mDNS announcement:
# Check Moss logs
ssh stone@stone-01.local
sudo journalctl -u garden-moss -n 100 | grep mDNS

# Look for:
# - "mDNS responder started"
# - "Announced service: mongodb (port 27017)"
```

**Solution:**

```bash
# Restart Moss to re-announce
ssh stone@stone-01.local
sudo systemctl restart garden-moss

# Verify announcement
avahi-browse -a | grep mongodb
```

### Application Connects But Authentication Fails

**Symptom:** Connection succeeds but database rejects credentials

**Common issue:** Connection string missing credentials

**Solution:**

```bash
# MongoDB example - add credentials
MONGODB_URI=zen-garden:mongodb/myapp?username=admin&password=secret

# Or resolve to native URL first, then add credentials:
# zen-garden:mongodb → mongodb://stone-01:27017
# mongodb://stone-01:27017 + credentials → mongodb://admin:secret@stone-01:27017
```

---

## Moss Daemon Issues

### Moss Not Responding

**Symptom:** `curl http://stone-01:7185/health` times out

**Diagnosis:**

```bash
# SSH to Stone
ssh stone@stone-01.local

# Check if Moss process running
sudo systemctl status garden-moss

# Expected: active (running)
# If inactive or failed:
```

**Solutions:**

#### Moss Crashed

```bash
# Check logs for crash reason
sudo journalctl -u garden-moss -n 100

# Common issues:
# - Port 7185 already in use (another process)
# - Configuration error (invalid moss.toml)
# - Docker daemon unreachable
```

**Start Moss:**

```bash
sudo systemctl start garden-moss

# Enable auto-restart
sudo systemctl enable garden-moss
```

#### Port 7185 In Use

```bash
# Check what's using port 7185
ss -tlnp | grep 7185

# If another process:
sudo kill <pid>

# Start Moss
sudo systemctl start garden-moss
```

#### Docker Daemon Unreachable

```bash
# Check Docker daemon
sudo systemctl status docker

# If Docker not running:
sudo systemctl start docker

# Restart Moss
sudo systemctl restart garden-moss
```

### Moss Logs Show Errors

#### "Failed to bind to 0.0.0.0:7185"

**Cause:** Port already in use

**Solution:**

```bash
# Find process using port
sudo lsof -i :7185

# Kill process or change Moss port
# Edit /etc/garden-moss/config.toml:
port = 7186

sudo systemctl restart garden-moss
```

#### "Cannot connect to Docker daemon"

**Cause:** Docker not running or Moss lacks permissions

**Solution:**

```bash
# Start Docker
sudo systemctl start docker

# Add Moss user to docker group
sudo usermod -aG docker moss

# Restart Moss
sudo systemctl restart garden-moss
```

---

## Container Health Problems

### Service Restarting Frequently

**Symptom:** `garden-rake observe` shows "(restarted 5x)"

**Diagnosis:**

```bash
# Check restart count
garden-rake describe mongodb

# Output:
# Restart count: 5
# Last exit code: 1
# Last error: Container exited with code 1

# Stream logs to see crash reason
garden-rake watch offering mongodb logs
```

**Common issues:**

#### Out of Memory (OOM Killed)

```bash
# Check diagnostics
garden-rake describe mongodb

# Output:
# OOM Killed: Yes
# Memory: 512 MB / 512 MB (100%)
```

**Solution:**

```bash
# Increase memory limit (custom template)
# Or deploy to Stone with more RAM
garden-rake offer mongodb --at stone-large
```

#### Configuration Error

```
Error: Invalid configuration in /etc/mongodb.conf
```

**Solution:**

```bash
# SSH to Stone
ssh stone@stone-01.local

# Edit configuration
docker exec mongodb vi /etc/mongodb.conf

# Restart container
docker restart mongodb
```

### Service Health Check Failing

**Symptom:** Service shows "Unhealthy" in observe output

**Diagnosis:**

```bash
# Check health check details
docker inspect <container_id> | jq '.[0].State.Health'

# Output shows:
# {
#   "Status": "unhealthy",
#   "FailingStreak": 3,
#   "Log": [
#     {
#       "ExitCode": 1,
#       "Output": "Connection refused"
#     }
#   ]
# }
```

**Solution:**

Service may be starting slowly. Wait 1-2 minutes and check again.

If persists:

```bash
# Check if service actually listening
docker exec mongodb ss -tlnp | grep 27017

# If not listening, check logs
garden-rake watch offering mongodb logs --tail 100
```

---

## Networking and Firewall

### Stone Reachable But Services Not

**Symptom:** Can `ping stone-01.local` but cannot connect to MongoDB (port 27017)

**Diagnosis:**

```bash
# Test port connectivity
nc -zv stone-01.local 27017  # Linux/macOS
# Or
telnet stone-01.local 27017

# If "Connection refused": firewall blocking
# If "No route to host": network routing issue
```

**Solutions:**

#### Firewall Blocking Ports

```bash
# SSH to Stone
ssh stone@stone-01.local

# Allow MongoDB port
sudo ufw allow 27017/tcp

# Allow all Docker container ports (if using UFW)
sudo ufw allow from 172.16.0.0/12 to any
```

#### Docker Network Misconfiguration

```bash
# Check Docker networks
docker network ls

# Verify container attached to correct network
docker inspect <container_id> | jq '.[0].NetworkSettings.Networks'

# If wrong network, recreate service
garden-rake take-away mongodb --at stone-01
garden-rake offer mongodb --at stone-01
```

---

## USB Installer Issues

### USB Boot Fails

**Symptom:** Machine won't boot from USB

**Solutions:**

#### BIOS/UEFI Settings

1. **Enter BIOS/UEFI** (press F12, DEL, or F2 during boot)
2. **Disable Secure Boot** (Debian not signed)
3. **Boot Mode:** UEFI preferred (Legacy/BIOS also works)
4. **Boot Order:** USB first

#### USB Not Bootable

```powershell
# Re-create USB with Force flag (Windows)
.\NewStone.ps1 -UsbDrive E: -StoneName "stone-01" -Force

# Verify USB contents:
# - EFI partition (FAT32)
# - Debian root partition (ext4)
# - preseed.cfg (automated install config)
```

### Installation Hangs at "Configuring Network"

**Cause:** No DHCP server or network cable unplugged

**Solutions:**

1. **Check network cable:** Ensure Ethernet connected
2. **DHCP server available:** Router providing DHCP
3. **Skip network config:** Edit preseed.cfg before USB creation (advanced)

### Pre-Install Services Not Deploying

**Symptom:** Stone boots but pre-configured services not running

**Diagnosis:**

```bash
# Check pre-install job status
curl http://stone-01:7185/api/jobs

# Output:
# {
#   "id": "018d3c6f-8e4c...",
#   "status": "Failed",
#   "completed": ["redis"],
#   "failed": {"mongodb": "Image pull timeout"}
# }
```

**Solutions:**

#### Network Timeout During Install

```bash
# Manually install failed services
garden-rake offer mongodb --at stone-01
```

#### Invalid Offering Name

```bash
# Check available offerings
garden-rake offer

# Fix typo in pre-install manifest (if re-creating USB)
# mongodb ✓ correct
# mongo   ✗ wrong
```

---

## Pond Security Issues

### Cannot Join Stone to Pond

**Symptom:** `garden-rake join pond` returns "Invalid code"

**Diagnosis:**

```bash
# Check code expiry
# TOTP codes valid for 5 minutes only

# Check clock synchronization
timedatectl status

# If clock skew > 10 minutes, codes invalid
```

**Solutions:**

#### Code Expired

```bash
# Generate new code from Cornerstone
ssh stone@cornerstone.local
sudo garden-rake invite stone-02

# Use code within 5 minutes
```

#### Clock Skew

```bash
# Sync clock via NTP
sudo timedatectl set-ntp true

# Verify sync
timedatectl status

# Retry join
sudo garden-rake join pond
```

### Certificate Auto-Renewal Failing

**Symptom:** Warning "Certificate expires in 10 minutes"

**Cause:** Cornerstone unreachable

**Diagnosis:**

```bash
# Check Cornerstone reachable
ping cornerstone.local

# Check Cornerstone Moss running
curl http://cornerstone.local:7185/health
```

**Solutions:**

```bash
# If Cornerstone offline, bring it back online
# Stone will auto-renew once Cornerstone available

# Manual renewal (if urgent)
ssh stone@stone-02.local
sudo garden-rake renew-certificate
```

---

## Performance and Resource Issues

### Stone Running Slowly

**Diagnosis:**

```bash
# Check resource usage
garden-rake observe --at stone-01

# Output:
# Stone Resources:
#   CPU: 85.2% (4 cores)  ← High CPU
#   Memory: 3.8 GB / 4 GB (95%)  ← Out of RAM
#   Disk: 450 GB / 500 GB (90%)
```

**Solutions:**

#### High CPU Usage

```bash
# Identify resource-heavy service
garden-rake observe --at stone-01

# Output shows per-service CPU:
# mongodb   Run   45.0%  ← High CPU
# redis     Run    2.3%

# Options:
# 1. Optimize service (MongoDB indexes, query tuning)
# 2. Move service to more powerful Stone
# 3. Add more Stones to distribute load
```

#### Memory Exhaustion

```bash
# Stop unused services
garden-rake rest redis --at stone-01

# Or remove completely
garden-rake take-away postgresql --at stone-01 --volumes

# Verify freed memory
garden-rake observe --at stone-01
```

#### Disk Space Full

```bash
# SSH to Stone
ssh stone@stone-01.local

# Check disk usage
df -h

# Clean up Docker images/containers
docker system prune -a  # WARNING: Removes unused images

# Remove old service volumes (if data not needed)
docker volume ls
docker volume rm <volume_name>
```

---

## Diagnostic Commands Quick Reference

### Network and Discovery

```bash
# Test Stone reachability
ping stone-01.local

# Test mDNS resolution
avahi-resolve -n stone-01.local          # Linux
dns-sd -G v4 stone-01.local              # macOS

# List all mDNS services
avahi-browse -a                          # Linux
dns-sd -B _koan-stone._tcp               # macOS

# Test Moss HTTP API
curl http://stone-01:7185/health
curl http://stone-01:7185/api/garden/stones
```

### Service Status

```bash
# List all services on Stone
garden-rake observe --at stone-01

# Detailed service info
garden-rake describe mongodb --at stone-01

# Stream logs
garden-rake watch offering mongodb logs
garden-rake watch offering mongodb logs --tail 100
```

### Moss Daemon

```bash
# Check Moss status (on Stone)
sudo systemctl status garden-moss

# View Moss logs
sudo journalctl -u garden-moss -n 100 --no-pager

# Restart Moss
sudo systemctl restart garden-moss

# Test Moss API
curl http://localhost:7185/health
curl http://localhost:7185/info
```

### Docker

```bash
# List running containers (on Stone)
docker ps

# List all containers (including stopped)
docker ps -a

# Check container logs
docker logs <container_id>
docker logs --tail 100 <container_id>

# Inspect container
docker inspect <container_id>

# Check Docker daemon
sudo systemctl status docker
```

### Network Debugging

```bash
# Test port connectivity
nc -zv stone-01.local 27017              # Linux/macOS
telnet stone-01.local 27017              # Windows

# Check listening ports (on Stone)
ss -tlnp                                 # Linux
netstat -tlnp                            # Older Linux

# Check firewall rules (on Stone)
sudo ufw status
sudo iptables -L -n
```

### Resource Monitoring

```bash
# Check Stone resources
curl http://stone-01:7185/info

# Check disk space (on Stone)
df -h

# Check memory usage (on Stone)
free -h

# Check Docker disk usage (on Stone)
docker system df
```

---

## Getting Help

### Before Opening Issue

1. **Collect diagnostics:**

```bash
# Save discovery output
garden-rake discover > discovery.txt

# Save service status
garden-rake observe --all > status.txt

# Save Moss logs (from each Stone)
sudo journalctl -u garden-moss -n 500 > moss-logs.txt
```

2. **Include version info:**

```bash
garden-rake --version
garden-moss --version
docker --version
```

3. **Describe environment:**

- Stone hardware (laptop, Raspberry Pi, thin client)
- Network topology (single subnet, multiple subnets, firewalls)
- Operating system (Debian version, kernel version)

### Community Support

- **GitHub Discussions:** https://github.com/sylin/zen-garden/discussions
- **GitHub Issues:** https://github.com/sylin/zen-garden/issues
- **Documentation:** https://zen-garden.sylin.org/docs

---

## Next Steps

- **Detailed architecture:** [Technical Specification](../specs/technical.md)
- **Security hardening:** [Pond Setup](../security/pond-setup.md)
- **Operations guide:** [Maintainers Documentation](../ops/maintainers.md)
- **Service management:** [Offering Services Guide](offering-services.md)
