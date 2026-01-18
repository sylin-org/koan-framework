# E-Waste Hardware Test Plan - garden-rake watch

## Pre-Test Preparation

### 1. Create USB Installer
```powershell
cd F:\Replica\NAS\Files\repo\github\koan-framework\other\zen-garden\installer

# Example: Create USB with mongodb and redis pre-installed
.\NewStone.ps1 -UsbDrive "E:" -Offering mongodb,redis -Hostname stone-ewaste -Force

# Verify files on USB:
# - garden-moss (binary)
# - garden-rake (binary)
# - garden-rake-quickstart.sh (quickstart guide)
# - garden-moss-preinstall.json (manifest)
# - garden-moss.service (systemd service)
```

### 2. Verify Binaries
```powershell
Get-Item ..\bin\moss, ..\bin\garden-rake | Format-Table Name, Length, LastWriteTime
```

Expected:
- moss: ~5.3 MB (release build with SSE streaming)
- garden-rake: ~5.8 MB (release build with watch command)

---

## Test Scenarios

### Test 1: USB Creation and Verification

**Steps:**
1. Run NewStone.ps1 with offerings (mongodb, redis, postgresql)
2. Check USB contains all required files
3. Verify garden-rake-quickstart.sh is present

**Expected Result:**
```
✓ garden-moss binary copied
✓ garden-rake binary copied
✓ garden-rake quickstart guide copied
✓ garden-moss-preinstall.json created with 3 services
```

---

### Test 2: Unattended Installation

**Steps:**
1. Boot e-waste machine from USB
2. Wait for Debian installation to complete (~5-10 minutes)
3. Watch for automatic reboot
4. Wait for Stone to boot into Debian

**Expected Result:**
- Unattended installation completes
- Machine reboots automatically
- Debian login prompt appears
- Hostname: stone-ewaste (or your chosen name)

---

### Test 3: First Boot - Verify Installation

**SSH or Console Login:**
```bash
# Username: stone (or your chosen username)
# Password: (set during NewStone.ps1)

# Verify binaries installed
which garden-moss
# Expected: /usr/local/bin/garden-moss

which garden-rake
# Expected: /usr/local/bin/garden-rake

# Verify garden-moss is running
systemctl status garden-moss.service
# Expected: Active (running)

# Check moss endpoint
curl http://localhost:3001/health
# Expected: "healthy"

# Check stone info
curl http://localhost:3001/info | jq
```

**Expected Output:**
```json
{
  "name": "stone-ewaste",
  "api_endpoint": "http://127.0.0.1:3001",
  "health": "Healthy",
  "capabilities": {
    "max_services": 10,
    "stone_type": "standard"
  },
  "moss_version": "0.1.0",
  "resources": { ... }
}
```

---

### Test 4: Pre-Install Manifest Processing

**Check if manifest was processed:**
```bash
# Check for pre-install manifest (should be removed after processing)
ls -la ~/garden-moss-preinstall.json
# Expected: File not found (removed after completion)

# Check job history
curl http://localhost:3001/api/jobs | jq

# Check installed services
curl http://localhost:3001/api/services | jq
# Expected: mongodb, redis listed as Running

# Verify Docker containers
docker ps
# Expected: zen-offering-mongodb, zen-offering-redis
```

---

### Test 5: garden-rake Commands

**Basic Commands:**
```bash
# Show quickstart guide
~/garden-rake-quickstart.sh

# Check status
garden-rake status

# List services
garden-rake list

# Check help
garden-rake --help
```

**Expected Output for `garden-rake list`:**
```
mongodb - Running
redis - Running
```

---

### Test 6: Real-Time Watch (Primary Test)

**Terminal 1: Start Watch**
```bash
garden-rake watch
```

**Expected Output:**
```
📡 Watching events from http://127.0.0.1:3001

(waiting for events...)
```

**Terminal 2: Trigger Installation**
```bash
garden-rake offer postgresql
```

**Expected in Terminal 1:**
```
[12:34:56] ℹ Starting installation: postgresql
[12:34:56] ⚙ Loading template for postgresql
[12:34:57] ℹ Pulling image: postgres:16-alpine
[12:35:10] ℹ Creating container for postgresql
[12:35:12] ℹ ✓ Service postgresql started successfully
```

**Verify:**
```bash
# Check service is running
garden-rake list | grep postgresql
# Expected: postgresql - Running

docker ps | grep zen-offering-postgresql
# Expected: Container running
```

---

### Test 7: Watch with --until Parameter

**Single Command:**
```bash
# Start watch, install service, auto-exit when done
garden-rake watch --until "successfully" &
WATCH_PID=$!

# Install vault
garden-rake offer vault

# Wait for watch to auto-exit
wait $WATCH_PID
echo "Watch exited automatically!"
```

**Expected Output:**
```
📡 Watching events from http://127.0.0.1:3001

⏳ Will exit when 'successfully' appears

[12:40:01] ℹ Starting installation: vault
[12:40:02] ⚙ Loading template for vault
[12:40:02] ℹ Pulling image: hashicorp/vault:latest
[12:40:05] ℹ Creating container for vault
[12:40:06] ℹ ✓ Service vault started successfully

✓ Pattern 'successfully' found, exiting
```

---

### Test 8: Batch Installation Monitoring

**Watch batch operations:**
```bash
# Terminal 1: Watch
garden-rake watch

# Terminal 2: Batch install
curl -X POST http://localhost:3001/api/operations/offer \
  -H "Content-Type: application/json" \
  -d '{"offerings": "weaviate,milvus,qdrant"}'
```

**Expected in Terminal 1:**
See events for all 3 services installing sequentially:
- weaviate: Starting → Loading → Pulling → Creating → Success
- milvus: Starting → Loading → Pulling → Creating → Success
- qdrant: Starting → Loading → Pulling → Creating → Success

---

### Test 9: Error Handling

**Test template not found:**
```bash
# Terminal 1: Watch for errors
garden-rake watch

# Terminal 2: Invalid service
garden-rake offer nonexistent-service
```

**Expected in Terminal 1:**
```
[12:45:01] ℹ Starting installation: nonexistent-service
[12:45:01] ⚙ Loading template for nonexistent-service
[12:45:01] ✗ Template not found for nonexistent-service: ...
```

---

### Test 10: Network Discovery

**From another machine on the network:**
```bash
# Auto-discovery (mDNS)
garden-rake watch stone-ewaste

# Or explicit endpoint
garden-rake watch --at http://192.168.1.xxx:3001

# With until parameter
garden-rake watch stone-ewaste --until "successfully"
```

**Expected:**
- Auto-discovers stone via mDNS (stone-ewaste.local)
- Connects to SSE endpoint
- Shows real-time events

---

## Success Criteria

### ✅ Installation
- [ ] USB boots and installs unattended
- [ ] garden-moss binary at /usr/local/bin/garden-moss
- [ ] garden-rake binary at /usr/local/bin/garden-rake
- [ ] garden-rake-quickstart.sh in user home directory
- [ ] garden-moss.service is Active (running)
- [ ] Pre-install manifest processed and removed

### ✅ Basic Functionality
- [ ] `garden-rake status` works
- [ ] `garden-rake list` shows services
- [ ] `garden-rake offer <service>` installs successfully
- [ ] Pre-installed services (mongodb, redis) are Running

### ✅ Watch Feature
- [ ] `garden-rake watch` connects to SSE endpoint
- [ ] Events display in real-time with timestamps
- [ ] Symbols visible (ℹ, ⚙, ✗)
- [ ] `--until` parameter exits on pattern match
- [ ] Multiple installations stream events correctly
- [ ] Error events display when template not found

### ✅ Performance
- [ ] Event latency <100ms from emission to display
- [ ] No memory leaks during long watch sessions
- [ ] moss remains stable under multiple watch connections
- [ ] Docker operations complete successfully

### ✅ User Experience
- [ ] Quickstart guide is helpful and accurate
- [ ] Commands are intuitive
- [ ] Error messages are clear
- [ ] Network discovery works (mDNS)

---

## Troubleshooting

### Issue: moss not running
```bash
# Check logs
sudo journalctl -u garden-moss.service -f

# Check Docker
systemctl status docker

# Restart moss
sudo systemctl restart garden-moss.service
```

### Issue: garden-rake not found
```bash
# Check if binary exists
ls -la /usr/local/bin/garden-rake

# Check permissions
chmod +x /usr/local/bin/garden-rake
```

### Issue: Watch not connecting
```bash
# Test SSE endpoint directly
curl -N -H "Accept: text/event-stream" http://localhost:3001/api/events

# Check moss logs for errors
sudo journalctl -u garden-moss.service -n 50

# Verify moss is listening
netstat -tlnp | grep 3001
```

### Issue: No events appearing
```bash
# Trigger a test event
garden-rake offer redis

# Check if moss is emitting events (check logs)
sudo journalctl -u garden-moss.service -f | grep "emit_event"
```

---

## Post-Test Validation

```bash
# Final system check
echo "=== System Status ==="
systemctl status garden-moss.service
docker ps --format "table {{.Names}}\t{{.Status}}"
garden-rake list

echo -e "\n=== Quick Watch Test ==="
garden-rake watch --until "successfully" &
WATCH_PID=$!
garden-rake offer typesense
wait $WATCH_PID
echo "✓ Watch feature working!"

echo -e "\n=== Resource Usage ==="
docker stats --no-stream
free -h
df -h /
```

---

## Reporting Results

Please test and report:
1. ✅/❌ Each test scenario
2. Any error messages encountered
3. Performance observations (speed, resource usage)
4. User experience feedback
5. Screenshots/recordings of watch in action (if possible)

**Ready to test on e-waste hardware!** 🚀
