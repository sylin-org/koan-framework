# Network Service Singleton Pattern

**Status**: Active  
**Component**: Moss (Daemon)  
**Related**: Discovery, mDNS, Future Network Services

## Problem Statement

Network services binding to specific ports can fail with "address already in use" errors on restart, particularly on Windows. Multiple factors contribute:

1. **Port Not Released**: Operating systems (especially Windows) don't release ports immediately after process termination or crash
2. **No Port Reuse**: Default socket behavior prevents binding to recently-used ports
3. **Multiple Listeners**: Spawning multiple listeners for the same service causes conflicts
4. **Async/Blocking Mismatch**: Mixing blocking `std::net` sockets in async contexts creates resource issues

### Observed Symptoms

```
Error: Only one usage of each socket address (protocol/network address/port) 
is normally permitted. (os error 10048)
```

## Solution: Centralized Singleton Pattern

### Architecture

```
┌─────────────────────────────────────────┐
│ network_singletons.rs                   │
├─────────────────────────────────────────┤
│ • create_reusable_udp_socket()          │
│   - SO_REUSEADDR (Windows)              │
│   - SO_REUSEPORT (Unix)                 │
│   - Async tokio::net::UdpSocket         │
│                                         │
│ • create_reusable_tcp_listener()        │
│   - SO_REUSEADDR (Windows)              │
│   - SO_REUSEPORT (Unix)                 │
│   - Async tokio::net::TcpListener       │
│                                         │
│ • ShutdownCoordinator                   │
│   - Graceful shutdown coordination      │
│   - Service registry tracking           │
└─────────────────────────────────────────┘
         ▲                     ▲
         │                     │
    ┌────┴──────┐         ┌────┴──────┐
    │ Discovery │         │  Future   │
    │  Service  │         │ Services  │
    │           │         │           │
    │ • UDP     │         │ • TCP     │
    │ • mDNS    │         │ • HTTP    │
    └───────────┘         └───────────┘
```

### Key Components

#### 1. Reusable Socket Creation

```rust
use crate::network_singletons;

// UDP socket with port reuse
let socket = network_singletons::create_reusable_udp_socket("0.0.0.0:7186").await?;

// TCP listener with port reuse
let listener = network_singletons::create_reusable_tcp_listener("0.0.0.0:7185").await?;
```

**Benefits**:
- Immediate port reuse after crash/restart (Windows)
- Load balancing capability (Unix SO_REUSEPORT)
- Platform-appropriate socket options

#### 2. Singleton Enforcement

```rust
use tokio::sync::OnceCell;

static UDP_LISTENER_CELL: OnceCell<Arc<Notify>> = OnceCell::const_new();

pub async fn ensure_udp_listener(...) -> Result<()> {
    // Only spawns once - subsequent calls are no-ops
    UDP_LISTENER_CELL.get_or_init(|| async {
        // Spawn listener task
        tokio::spawn(async move { /* ... */ });
        shutdown_handle
    }).await;
    
    Ok(())
}
```

**Benefits**:
- Single listener instance per process
- Thread-safe initialization
- No race conditions on startup

#### 3. Graceful Shutdown Coordination

```rust
tokio::select! {
    result = socket.recv_from(&mut buf) => {
        // Process network data
    }
    _ = shutdown.notified() => {
        tracing::info!("Received shutdown signal");
        break;
    }
}
```

**Benefits**:
- Clean resource release
- Port available immediately after shutdown
- No orphaned listeners

### Implementation Example: UDP Discovery

**Before** (Problematic):
```rust
// main.rs - spawns unconditionally
tokio::spawn(async move {
    if let Err(e) = discovery::udp_listener(name, endpoint).await {
        tracing::error!(error = ?e, "UDP listener failed");
    }
});

// discovery.rs - blocking socket, no shutdown
pub async fn udp_listener(...) -> Result<()> {
    let socket = UdpSocket::bind("0.0.0.0:7186")?; // ❌ Blocking, no reuse
    socket.set_read_timeout(Some(Duration::from_secs(1)))?;
    
    loop {
        match socket.recv_from(&mut buf) { // ❌ Blocking recv
            Ok((len, addr)) => { /* ... */ }
            Err(ref e) if e.kind() == WouldBlock => {
                tokio::task::yield_now().await; // ❌ Inefficient
            }
        }
    }
}
```

**After** (Correct):
```rust
// main.rs - singleton with shutdown
if let Err(e) = discovery::ensure_udp_listener(
    name,
    endpoint,
    shutdown_tx.clone(), // Pass shutdown signal
).await {
    tracing::error!(error = ?e, "Failed to start UDP discovery");
}

// discovery.rs - async singleton with graceful shutdown
static UDP_LISTENER_CELL: OnceCell<Arc<Notify>> = OnceCell::const_new();

pub async fn ensure_udp_listener(
    stone_name: String,
    api_endpoint: String,
    shutdown_signal: Arc<Notify>,
) -> Result<()> {
    UDP_LISTENER_CELL.get_or_init(|| async {
        // Spawn once with shutdown coordination
        tokio::spawn(async move {
            udp_listener_inner(stone_name, api_endpoint, shutdown).await
        });
        shutdown_handle
    }).await;
    Ok(())
}

async fn udp_listener_inner(..., shutdown: Arc<Notify>) -> Result<()> {
    // ✅ Async socket with SO_REUSEADDR
    let socket = network_singletons::create_reusable_udp_socket(
        &format!("0.0.0.0:{}", ports::DISCOVERY_UDP)
    ).await?;
    
    loop {
        tokio::select! {
            // ✅ Async recv
            result = socket.recv_from(&mut buf) => { /* ... */ }
            // ✅ Graceful shutdown
            _ = shutdown.notified() => break,
        }
    }
    Ok(())
}
```

## Platform-Specific Behavior

### Windows
- **SO_REUSEADDR**: Allows immediate port reuse after process termination
- **Critical**: Without this, Windows keeps ports reserved for 2-4 minutes (TIME_WAIT)
- **Error 10048**: "Only one usage of each socket address..." when port not released

### Unix/Linux
- **SO_REUSEADDR**: Basic port reuse after socket close
- **SO_REUSEPORT**: Additional load balancing - multiple processes can bind same port
- **More Forgiving**: Ports typically released faster than Windows

## Applying to Other Services

### Pattern Template

```rust
// 1. Define singleton cell
static MY_SERVICE_CELL: OnceCell<Arc<Notify>> = OnceCell::const_new();

// 2. Ensure function
pub async fn ensure_my_service(
    config: MyConfig,
    shutdown_signal: Arc<Notify>,
) -> Result<()> {
    MY_SERVICE_CELL.get_or_init(|| async {
        let shutdown = Arc::new(Notify::new());
        let task_shutdown = shutdown.clone();
        
        tokio::spawn(async move {
            if let Err(e) = my_service_inner(config, task_shutdown).await {
                tracing::error!(error = ?e, "My service failed");
            }
        });
        
        // Coordinate with main shutdown
        let cleanup_shutdown = shutdown.clone();
        tokio::spawn(async move {
            shutdown_signal.notified().await;
            cleanup_shutdown.notify_one();
        });
        
        shutdown
    }).await;
    
    Ok(())
}

// 3. Inner implementation with graceful shutdown
async fn my_service_inner(
    config: MyConfig,
    shutdown: Arc<Notify>,
) -> Result<()> {
    // Use centralized socket helpers
    let socket = network_singletons::create_reusable_udp_socket(&config.addr).await?;
    
    loop {
        tokio::select! {
            result = socket.recv_from(&mut buf) => {
                // Process data
            }
            _ = shutdown.notified() => {
                tracing::info!("Service shutting down");
                break;
            }
        }
    }
    
    Ok(())
}
```

## Testing Verification

### Port Release Check (Windows)
```powershell
# Before fix - port remains bound after crash
netstat -ano | findstr :7186
# Shows TIME_WAIT or orphaned process

# After fix - port immediately available
netstat -ano | findstr :7186
# Empty result after clean shutdown
```

### Restart Test
```bash
# Stop service
systemctl stop garden-moss  # Linux
Stop-Service garden-moss    # Windows

# Immediate restart (should not fail)
systemctl start garden-moss
Start-Service garden-moss
```

### Concurrent Start Test
```bash
# Try starting twice simultaneously
./garden-moss &
./garden-moss &

# Expected: Second instance no-ops (singleton), no error
```

## Related Files

- [src/moss/src/network_singletons.rs](../../../other/zen-garden/src/moss/src/network_singletons.rs) - Centralized helpers
- [src/moss/src/discovery.rs](../../../other/zen-garden/src/moss/src/discovery.rs) - UDP discovery implementation
- [src/moss/src/main.rs](../../../other/zen-garden/src/moss/src/main.rs) - Service initialization

## Future Work

- Apply pattern to mDNS announcements (if multi-instance issues arise)
- HTTP server binding (if changing from axum defaults)
- Metrics exporters (Prometheus, etc.)
- Inter-service communication channels

## References

- [socket2 crate documentation](https://docs.rs/socket2/)
- [tokio::sync::OnceCell](https://docs.rs/tokio/latest/tokio/sync/struct.OnceCell.html)
- [Windows Winsock SO_REUSEADDR](https://learn.microsoft.com/en-us/windows/win32/winsock/using-so-reuseaddr-and-so-exclusiveaddruse)
