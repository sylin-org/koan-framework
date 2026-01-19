# Garden-Rake CLI Specification

**Purpose:** Technical specification for the Rake CLI tool - discovery, commands, output formatting.  
**Audience:** Developers implementing Rake, operators understanding command behavior.

---

## Table of Contents

1. [Overview](#overview)
2. [Hot Cache Discovery](#hot-cache-discovery)
3. [UDP Broadcast Discovery](#udp-broadcast-discovery)
4. [HTTP Client](#http-client)
5. [Command Parser](#command-parser)
6. [Output Formatting](#output-formatting)
7. [Error Handling](#error-handling)
8. [Target Selection](#target-selection)

---

## Overview

**Binary:** `garden-rake`  
**Installation:** User PATH or `/usr/local/bin/`  
**Language:** Rust (clap for CLI parsing)

### Responsibilities

1. Query localhost Moss for hot-cached topology (zero discovery)
2. Fall back to network discovery if no local Moss (UDP broadcast, mDNS, Lantern)
3. Send HTTP requests to Moss API
4. Parse command-line arguments and flags
5. Handle target selection (local, specific Stone, all Stones)
6. Generate operation IDs for group operations (`--all`)
7. Pretty-print responses with colors and tables
8. Provide user-friendly error messages

---

## Hot Cache Discovery

**Strategy:** Query hot cache instead of performing network discovery

### Common Case (90%)

Rake runs on a Stone (alongside Moss):

- Query: `GET http://localhost:7185/api/garden/stones`
- Latency: <1ms (localhost HTTP)
- Discovery: Zero (Moss already has topology from broadcasts)
- Result: Instant access to full garden topology

### Special Case (10%)

Rake runs on developer machine (no local Moss):

- UDP discovery to get any Moss HTTP endpoint
- Cache that endpoint and initial topology
- Background polling (30s-60s) using cursor-based updates
- Cursor-based polling minimizes bandwidth, only transfers changes

### Discovery Flow

**Priority order:**

1. **Localhost Cache Query** (instant, zero discovery)
   - Query `http://localhost:7185/api/garden/stones`
   - **Common case:** Rake runs on Stone → <1ms, full topology available
   - **Benefit:** Zero network discovery, immediate access to hot cache

2. **UDP Broadcast + Election** (Windows-compatible)
   - Single Stone responds with endpoint
   - Rake queries that Stone's cache: `GET /api/garden/stones`
   - **Benefit:** One broadcast reveals full topology via cache

3. **mDNS Browse** (Linux/macOS fallback)
   - Browse `_moss._tcp.local.`
   - Used when localhost and UDP broadcast both fail
   - **Benefit:** Zero-config discovery on Unix-like systems

4. **Lantern HTTP** (directory fallback)
   - Query `GET http://lantern:3000/api/resolve`
   - Used when no Moss accessible directly
   - **Benefit:** Works across subnets, centralized directory

5. **Manual --at** (always works)
   - Direct connection to specified Stone
   - Example: `garden-rake status --at stone-01.local`
   - **Benefit:** Explicit control, bypasses discovery

### Target Selection

- **Local (default):** localhost or hostname
- **Specific:** `--at stone-01`
- **All:** `--all` for garden-wide operations

---

## UDP Broadcast Discovery

**Purpose:** Enable auto-discovery on Windows without mDNS browse or Lantern dependency

**Port:** 3004 (Rake discovery broadcasts)

### Protocol Flow

```
1. Rake broadcasts discovery request
   UDP broadcast to 255.255.255.255:3004
   {
     "discover": "moss",
     "request_id": "550e8400-e29b-41d4-a716-446655440000",
     "requester": "rake-cli"
   }

2. All Moss instances receive broadcast
   Calculate election delay using blake3 hash:
   let hash = blake3::hash(format!("{}{}", stone_name, request_id));
   let delay_ms = (hash[0] as u64) * 10;  // Range: 0-2550ms

3. First responder (lowest hash) replies
   UDP unicast back to requester IP:3005
   {
     "stone_name": "stone-01",
     "stone_endpoint": "http://stone-01.local:7185",
     "lantern_endpoint": "http://stone-09.local:3002",
     "moss_version": "0.1.0"
   }

4. Rake queries that Stone for full topology
   GET http://stone-01.local:7185/api/garden/stones
   {
     "stones": [
       {
         "name": "stone-01",
         "api_endpoint": "http://stone-01.local:7185",
         "health": "healthy"
       }
     ]
   }
```

### Benefits

- **Zero-discovery common case:** Localhost query <1ms
- **Windows first-class support:** UDP broadcast solves mDNS browse limitation
- **No Lantern dependency:** Works with pure Moss network
- **Hot cache always available:** Every Moss has full topology
- **Single query reveals all:** One Stone responds with cached topology
- **Idempotent:** Re-run anytime for fresh topology

### Configuration

- Localhost-first discovery enabled by default
- UDP broadcast with 3s timeout
- Retry on failure with max 3 retries
- Endpoint cache TTL of 300 seconds for remote Rake
- Background poll interval of 30 seconds with cursor-based delta updates

### Error Handling

- No response (timeout): Fall back to Lantern HTTP or manual `--at`
- Invalid response: Log warning, try next discovery method
- Connection to returned endpoint fails: Re-run discovery automatically

---

## HTTP Client

**Library:** `reqwest` crate

### Client Operations

**POST requests:** Send to Moss API endpoints for operations:
- `offer`, `remove`, `upgrade`, `wake`
- JSON payloads including `operation_id` for group operations

**GET requests:** Query operations:
- List services
- Get health
- Query garden stones

**Technical requirements:**
- Proper timeouts (30s default)
- Retry logic for transient failures
- Connection pooling for efficiency

### Error Handling

- **Connection refused** → "Cannot reach Moss on stone-XX"
- **Timeout** → "Request timed out (is Moss running?)"
- **4xx** → Display API error message
- **5xx** → "Internal server error on stone-XX"

---

## Command Parser

**Library:** `clap` crate (subcommands)

### Command Structure

```bash
garden-rake <SUBCOMMAND> [ARGS] [FLAGS]
```

**Consistent flags:**
- `--at <stone>` - Target specific Stone
- `--all` - Garden-wide operations
- `--json` - JSON output
- `--version` - Show version
- `--help` - Show help

### Subcommands

#### Status

```bash
garden-rake status [--at stone-01] [--all]
```

Show service status for local Stone, specific Stone, or all Stones.

#### Offer

**List offerings:**

```bash
garden-rake offer
```

Lists validated offerings by category.

**Install offering:**

```bash
garden-rake offer <name>
```

Installs when `<name>` matches a known offering.

**Query recommendations:**

```bash
garden-rake offer <query>
```

Prints top ranked recommendations when `<query>` is not a known offering.

**Flags:**

- `--at <endpoint|stone-name|anywhere>` - Target specific stone or search all
- `--prefer <tokens>` - Bias ranking (e.g., `--prefer ssd,nvme`)
- `--anywhere-on-fail` - Auto-run cross-stone recommendations after compatibility failure

**Examples:**

```bash
# Install MongoDB
garden-rake offer mongodb --at stone-01

# Query for document databases
garden-rake offer database,document

# Cross-stone search with preferences
garden-rake offer vector --at anywhere --prefer ssd
```

#### Remove

```bash
garden-rake remove <SERVICE> [--at stone-01] [--volumes]
```

Remove service, optionally including volumes.

**Special command:**

```bash
garden-rake remove keystone
```

Removes Pond security from all Stones.

#### List

```bash
garden-rake list [--at stone-01] [--all]
```

List all available service offerings and installed services.

#### Upgrade

```bash
garden-rake upgrade [SERVICE] [--at stone-01] [--all] [--version X.X]
```

Upgrade specific service or all services to latest/specified version.

#### Rest/Wake

```bash
garden-rake rest <SERVICE>  # Stop service
garden-rake wake <SERVICE>  # Start service
```

Both support `--at` and `--all` flags.

#### Observe

```bash
garden-rake observe [--at stone-01] [--all]
```

Real-time status with resource usage.

#### Watch

```bash
garden-rake watch offering <name> logs [--tail N] [--timestamps]
garden-rake watch stone <name> logs
```

Stream logs in real-time (SSE) or dump tail.

---

## Output Formatting

**Library:** `colored` for colors, custom table formatting

### Status Output

```
Stone: stone-01
Status: Healthy
Moss Version: 0.1.0
Docker: Running

Services (2):
┌──────────┬─────────┬─────────┬────────┬──────────┬────────────┐
│ Name     │ Offering│ Version │ Health │ Uptime   │ Memory     │
├──────────┼─────────┼─────────┼────────┼──────────┼────────────┤
│ mongodb  │ mongodb │ 7.0.4   │ ✓      │ 2d 3h    │ 450 MB     │
│ redis    │ redis   │ 7.2.3   │ ✓      │ 5h 23m   │ 80 MB      │
└──────────┴─────────┴─────────┴────────┴──────────┴────────────┘

Total: 2 services, 530 MB memory
```

### Install Output

```
Discovering Garden-Moss Daemons... ✓ (found stone-01)
Installing mongodb on stone-01...
  [1/4] Validating template... ✓
  [2/4] Checking port availability... ✓ (27017, 8080)
  [3/4] Updating docker-compose.yml... ✓
  [4/4] Starting containers... ✓

✓ Successfully installed mongodb 7.0.4

Services:
  - mongodb (native): mongodb://stone-01.local:27017
  - mongodb-agnostic (HTTP): http://stone-01.local:8080

Next steps:
  1. Use connection string: zen-garden:mongodb
  2. Check status: garden-rake status
```

### JSON Output

```bash
garden-rake status --json
```

```json
{
  "stone": "stone-01",
  "status": "healthy",
  "moss_version": "0.1.0",
  "services": [
    {
      "name": "mongodb",
      "status": "running",
      "health": "healthy",
      "uptime": 7200,
      "memory_mb": 450
    }
  ]
}
```

### Color Coding

- **Green:** Healthy, success
- **Yellow:** Warnings, degraded
- **Red:** Errors, critical
- **Blue:** Informational
- **Gray:** Disabled, inactive

---

## Error Handling

### User-Friendly Errors

**Service not found:**

```
✗ Error: Offering 'mysql' not found

Available offerings:
  - mongodb
  - postgresql
  - redis
  - sqlserver

Try: garden-rake offer <service>
```

**No Stones discovered:**

```
✗ Error: No Stones found on network

Troubleshooting:
  1. Check Stone is powered on and connected
  2. Verify same subnet (mDNS limited to local network)
  3. Try: garden-rake discover --via-lantern http://lantern:7186
  4. Try: garden-rake status --at stone-01.local
```

**Connection failed:**

```
✗ Error: Cannot reach Moss on stone-01

Diagnostics:
  - Network: reachable (ping OK)
  - Port 7185: connection refused

Possible issues:
  1. Moss daemon not running (check systemctl status garden-moss)
  2. Firewall blocking port 7185
  3. Wrong Stone name/IP

Try: ssh stone@stone-01.local 'sudo systemctl status garden-moss'
```

### Error Philosophy

- **Template validation:** Warn and proceed (log issues)
- **Batch operations (--all):** Continue with other Stones on failure
- **Destructive operations:** Require explicit confirmation
- **Network errors:** Provide actionable troubleshooting steps

---

## Target Selection

### Local vs Remote

**Local (default):**

```bash
garden-rake status
# Queries localhost:7185
```

**Remote (specific):**

```bash
garden-rake status --at stone-01
# Discovers stone-01 via mDNS/UDP/cache
```

**Garden-wide:**

```bash
garden-rake status --all
# Queries all Stones in topology cache
```

### Parallel Execution

```rust
async fn execute_on_all_stones(operation: Operation) -> Result<()> {
    let endpoints = discover_moss(Target::All).await?;

    let tasks: Vec<_> = endpoints.iter().map(|endpoint| {
        let op = operation.clone();
        tokio::spawn(async move {
            let client = MossClient::new(endpoint);
            op.execute(&client).await
        })
    }).collect();

    let results = join_all(tasks).await;
    display_aggregate_results(results)?;

    Ok(())
}
```

**Behavior:**

- Operations execute in parallel across Stones
- Continue on failure (collect all results)
- Display aggregated results with per-Stone status
- Include operation_id for correlation

---

## Next Steps

- **Moss daemon specification:** [moss-daemon.md](moss-daemon.md)
- **Service offerings:** [offerings.md](offerings.md)
- **Discovery protocol:** [discovery.md](discovery.md)
- **User guides:** [../guides/offering-services.md](../guides/offering-services.md)
