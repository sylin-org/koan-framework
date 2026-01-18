# garden-rake watch - Real-time Event Streaming

The `watch` command provides real-time visibility into moss operations, streaming events like service installations, container creation, and error messages directly to your terminal.

## Usage

```bash
# Watch all events from default/discovered stone
garden-rake watch

# Watch specific stone by name
garden-rake watch stone-01

# Watch until specific string appears (then exit)
garden-rake watch stone-01 until "completed"

# Watch with explicit endpoint
garden-rake watch --at http://192.168.1.100:3001

# Combine stone name and until pattern
garden-rake watch stone-02 until "started successfully"
```

## Command Syntax

```
garden-rake watch [STONE] [OPTIONS]

Arguments:
  [STONE]  Stone name or endpoint (omit to auto-discover)

Options:
      --until <PATTERN>  Exit when this string appears in event stream
      --at <ENDPOINT>    Moss endpoint (omit to auto-discover)
  -h, --help             Print help
```

## Examples

### Watch Installation Progress

```powershell
# Terminal 1: Start installation
curl -X POST http://stone-01:3001/api/operations/offer/mongodb

# Terminal 2: Watch events
garden-rake watch stone-01
```

**Output:**
```
📡 Watching events from http://stone-01:3001

[12:34:56] ℹ Starting installation: mongodb
[12:34:57] ⚙ Loading template for mongodb
[12:34:58] ℹ Pulling image: mongo:7
[12:35:12] ℹ Creating container for mongodb
[12:35:14] ℹ ✓ Service mongodb started successfully
```

### Wait for Specific Event

```bash
# Exit when installation completes
garden-rake watch stone-01 until "started successfully"
```

**Output:**
```
📡 Watching events from http://stone-01:3001

⏳ Will exit when 'started successfully' appears

[12:34:56] ℹ Starting installation: mongodb
[12:34:58] ℹ Pulling image: mongo:7
[12:35:14] ℹ ✓ Service mongodb started successfully

✓ Pattern 'started successfully' found, exiting
```

### Monitor Batch Installation

```powershell
# Start batch install
$body = @{ offerings = "mongodb,redis,postgresql" } | ConvertTo-Json
Invoke-RestMethod -Uri "http://stone-01:3001/api/operations/offer" -Method POST -Body $body -ContentType "application/json"

# Watch progress
garden-rake watch stone-01
```

**Output:**
```
📡 Watching events from http://stone-01:3001

[12:40:10] ℹ Starting installation: mongodb
[12:40:11] ℹ Pulling image: mongo:7
[12:40:25] ℹ ✓ Service mongodb started successfully
[12:40:26] ℹ Starting installation: redis
[12:40:27] ℹ Pulling image: redis:7-alpine
[12:40:35] ℹ ✓ Service redis started successfully
[12:40:36] ℹ Starting installation: postgresql
[12:40:37] ℹ Pulling image: postgres:16-alpine
[12:40:48] ℹ ✓ Service postgresql started successfully
```

### Watch Pre-Install Manifest

When Stone boots with a `garden-moss-preinstall.json` manifest, watch the auto-installation:

```bash
# Immediately after Stone first boot
garden-rake watch stone-01
```

**Output:**
```
📡 Watching events from http://stone-01:3001

[08:15:22] ℹ Starting installation: mongodb
[08:15:23] ℹ Loading template for mongodb
[08:15:24] ℹ Pulling image: mongo:7
[08:15:38] ℹ Creating container for mongodb
[08:15:40] ℹ ✓ Service mongodb started successfully
[08:15:41] ℹ Starting installation: redis
[08:15:42] ℹ Pulling image: redis:7-alpine
... continues until all services installed ...
```

## Event Types

Events are color-coded by severity level:

- **ℹ (info)** - Normal operation (installation started, service ready)
- **⚙ (debug)** - Detailed operations (template loading, container creation)
- **⚠ (warn)** - Non-fatal issues (retries, fallbacks)
- **✗ (error)** - Failures (template not found, installation errors)

## Event Format

Each event includes:
- **Timestamp** - HH:MM:SS format from stone's timezone
- **Symbol** - Visual indicator of severity
- **Message** - Human-readable description

Raw JSON structure (not shown to user):
```json
{
  "timestamp": "2026-01-16T12:34:56.789Z",
  "level": "info",
  "message": "Starting installation: mongodb",
  "job_id": "01936b8d-a4f0-7000-8000-000000000001"
}
```

## Auto-Discovery

The `watch` command uses the same discovery mechanism as other garden-rake commands:

1. **Fast-path**: Check localhost (http://127.0.0.1:3001) - ideal for Windows same-host
2. **LAN discovery**: UDP broadcast to find stones on network
3. **Explicit**: Use `--at` flag for specific endpoint

## Exit Conditions

The watch command exits when:
- **Pattern match**: `--until` pattern appears in event message (exit code 0)
- **Connection lost**: Network error or moss shutdown (exit code 0)
- **User interrupt**: Ctrl+C pressed (exit code 130)

## Use Cases

### CI/CD Pipeline

```bash
#!/bin/bash
# Wait for mongodb to be ready before running tests
garden-rake offer mongodb --at http://stone-ci:3001
garden-rake watch stone-ci until "mongodb started successfully"

# Run integration tests
npm test
```

### Debugging Installation

```bash
# Watch for errors during installation
garden-rake watch stone-dev until "error" || echo "Installation failed"
```

### Pre-Install Validation

```bash
# Create USB with pre-install manifest
.\NewStone.ps1 -UsbDrive "G:" -Offering mongodb,redis,postgresql -Force

# Boot Stone from USB, then monitor auto-install
garden-rake watch stone-01 until "Pre-install manifest removed"
```

### Multi-Stone Monitoring

```bash
# Watch multiple stones in parallel (requires terminal multiplexer)
tmux new-session \; \
  send-keys "garden-rake watch stone-01" C-m \; \
  split-window -h \; \
  send-keys "garden-rake watch stone-02" C-m
```

## Technical Details

### Server-Sent Events (SSE)

The watch command uses SSE to stream events from moss:
- **Endpoint**: `GET /api/events`
- **Protocol**: HTTP with `Content-Type: text/event-stream`
- **Reconnect**: Automatic on transient errors (not implemented yet)
- **Buffering**: 100-event capacity in moss broadcast channel

### Performance

- **Latency**: <50ms from event emission to terminal display
- **Bandwidth**: ~100-200 bytes per event (~10KB/minute typical)
- **Resource**: Minimal CPU/memory (single HTTP connection + parsing)

### Limitations

- **Event History**: Only events *after* watch starts are shown (no backfill)
- **Job-specific filtering**: Not yet implemented (shows all moss events)
- **Reconnection**: Manual restart required on connection loss
- **Color support**: Depends on terminal (fallback to symbols only)

## Related Commands

- `garden-rake status` - One-time snapshot of stone health
- `garden-rake list` - View currently installed services
- `garden-rake observe` - Multi-stone dashboard view
- `curl http://stone-01:3001/api/jobs` - View async job history

## Future Enhancements

- Filter events by job ID: `garden-rake watch --job <job_id>`
- Event history: `garden-rake watch --since 5m`
- JSON output: `garden-rake watch --format json`
- Follow mode: Auto-reconnect on connection loss
- Multi-stone watch: `garden-rake watch --all`

---

**See Also:**
- [Async Job API](./ASYNC-JOB-API.md) - Background job system
- [Stone Installation Flow](./STONE-INSTALLATION-FLOW.md) - USB deployment process
