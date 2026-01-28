# Koan.ZenGarden Technical Reference

## Protocol Specification

This document captures the discovered Zen Garden protocols based on live testing (January 2026).

---

## Stone Discovery Protocol

### UDP Multicast

Zen Garden Stones announce themselves via UDP multicast.

| Parameter | Value |
|-----------|-------|
| **Multicast Group** | `239.255.42.99` |
| **Port** | `7184` |
| **Protocol** | UDP |
| **Payload Format** | JSON |

### Discovery Request

Send JSON payload to multicast group:

```json
{
  "action": "discover"
}
```

### Discovery Response

Stones respond with their identity:

```json
{
  "name": "stone-coral-prairie",
  "version": "1.0.0",
  "endpoint": "http://192.168.1.135:7185"
}
```

### Implementation Notes

1. **Early Return** - All Stones share the same topology map, so one response is sufficient. Return immediately on first valid response rather than waiting for timeout.
2. **Binding** - On multi-homed Windows (WSL, Hyper-V), bind to a LAN interface explicitly (192.168.x.x, 10.x.x.x, 172.16-31.x.x)
3. **Port Binding** - Bind to port 7184 to receive multicast responses
4. **Timeout** - Use per-receive timeout (500ms) rather than global timeout
5. **Echo Filtering** - Your own request may echo back; filter by checking for `endpoint` field

---

## Moss HTTP API

Each Stone runs a Moss daemon on port 7185 providing service information.

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/health` | GET | Health check |
| `/api/v1/services` | GET | List all services |
| `/api/v1/services/{name}` | GET | Get specific service |

### Health Check

```http
GET /health HTTP/1.1
Host: 192.168.1.135:7185
```

**Response:**

```json
{
  "status": "healthy",
  "timestamp": "2026-01-28T10:00:00Z"
}
```

### List Services

```http
GET /api/v1/services HTTP/1.1
Host: 192.168.1.135:7185
```

**Response:**

```json
{
  "services": [
    {
      "name": "mongodb",
      "status": "running",
      "healthy": true,
      "connection": {
        "uris": [
          "mongodb://192.168.1.135:27017"
        ]
      }
    },
    {
      "name": "redis",
      "status": "running",
      "healthy": true,
      "connection": {
        "uris": [
          "redis://192.168.1.135:6379"
        ]
      }
    }
  ]
}
```

### Get Specific Service

```http
GET /api/v1/services/mongodb HTTP/1.1
Host: 192.168.1.135:7185
```

**Response (Ports format):**

```json
{
  "offering": "mongodb",
  "status": "running",
  "healthy": true,
  "ports": {
    "native": 27017,
    "agnostic": null
  }
}
```

> ⚠️ **Note:** The single-service endpoint returns `ports` object, while the list endpoint returns `connection.uris[]`. The client handles both formats.

---

## Network Configuration

### Ports

| Service | Port | Protocol | Purpose |
|---------|------|----------|---------|
| Discovery | 7184 | UDP | Stone multicast discovery |
| Moss API | 7185 | HTTP | Service management API |
| MongoDB | 27017 | TCP | Database (when running) |
| Redis | 6379 | TCP | Cache (when running) |
| RabbitMQ | 5672 | TCP | Messaging (when running) |
| NATS | 4222 | TCP | Messaging (when running) |
| PostgreSQL | 5432 | TCP | Database (when running) |
| Elasticsearch | 9200 | TCP | Search (when running) |
| Meilisearch | 7700 | TCP | Search (when running) |

### Firewall Rules (Windows)

For discovery to work, allow:
- UDP 7184 inbound (multicast responses)
- TCP 7185 outbound (Moss API)
- TCP to service ports as needed

---

## Service Resolution Flow

```
┌──────────────────────────────────────────────────────────────────┐
│ FindServiceAsync("mongodb")                                       │
└──────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│ 1. Check offering cache                                           │
│    Key: "mongodb" → Value: ResolvedService { Stone, ConnString }  │
└──────────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────┴───────────────┐
              │                               │
           HIT ✓                           MISS
              │                               │
              ▼                               ▼
┌─────────────────────────┐   ┌────────────────────────────────────┐
│ Return cached result    │   │ 2. Get topology (discover if empty) │
└─────────────────────────┘   └────────────────────────────────────┘
                                              │
                                              ▼
                              ┌────────────────────────────────────┐
                              │ 3. For each Stone in topology:     │
                              │    GET /api/v1/services/{offering} │
                              │    If found & healthy → cache it   │
                              └────────────────────────────────────┘
                                              │
                                              ▼
                              ┌────────────────────────────────────┐
                              │ 4. Build connection string:        │
                              │    • From connection.uris[0] OR    │
                              │    • From scheme://host:ports.native│
                              └────────────────────────────────────┘
```

---

## Scheme Mappings

The client maps offering names to connection string schemes:

| Offering | Scheme |
|----------|--------|
| mongodb | mongodb |
| redis | redis |
| rabbitmq | amqp |
| nats | nats |
| postgres, postgresql | postgres |
| elasticsearch | http |
| meilisearch | http |
| mysql, mariadb | mysql |
| kafka | kafka |
| mssql | mssql |
| *other* | tcp |

---

## Discovered Stones (Test Environment)

From live testing on 2026-01-28:

| Stone Name | IP | Moss Endpoint | Services |
|------------|-----|--------------|----------|
| stone-coral-prairie | 192.168.1.135 | http://192.168.1.135:7185 | mongodb |
| stone-crystal-forest | 192.168.1.197 | http://192.168.1.197:7185 | (none active) |
| stone-bronze-canyon | 192.168.1.107 | http://192.168.1.107:7185 | (none active) |

---

## Implementation Gotchas

### 1. UDP Binding on Multi-homed Windows

Windows with WSL2 or Hyper-V has multiple network interfaces. Binding to `IPAddress.Any` may bind to wrong interface.

**Solution:** Enumerate interfaces and bind to one with a LAN prefix:

```csharp
private IPAddress GetLanBindAddress()
{
    foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
    {
        if (nic.OperationalStatus != OperationalStatus.Up) continue;
        foreach (var addr in nic.GetIPProperties().UnicastAddresses)
        {
            if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
            var bytes = addr.Address.GetAddressBytes();
            // 192.168.x.x, 10.x.x.x, 172.16-31.x.x
            if (bytes[0] == 192 && bytes[1] == 168) return addr.Address;
            if (bytes[0] == 10) return addr.Address;
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return addr.Address;
        }
    }
    return IPAddress.Any; // Fallback
}
```

### 2. UDP Receive Timeout

`ReceiveFromAsync` with a long `CancellationToken` blocks forever if no response.

**Solution:** Create per-receive timeout:

```csharp
using var receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
receiveCts.CancelAfter(TimeSpan.FromMilliseconds(500));
await socket.ReceiveFromAsync(buffer, endPoint, receiveCts.Token);
```

### 3. API Response Format Variance

`/api/v1/services` returns `connection.uris[]`, but `/api/v1/services/{name}` returns `ports.native`.

**Solution:** Handle both:

```csharp
if (service.Connection != null)
    return service.Connection.GetUri(scheme);
else if (service.Ports?.Native != null)
    return $"{scheme}://{stone.Host}:{service.Ports.Native}";
```

---

## Test Results

Successfully validated on 2026-01-28:

```
✅ Found 3 Stone(s):
   📦 stone-coral-prairie at http://192.168.1.135:7185
   📦 stone-crystal-forest at http://192.168.1.197:7185
   📦 stone-bronze-canyon at http://192.168.1.107:7185

✅ Found MongoDB!
   Connection String: mongodb://192.168.1.135:27017

✅ Connected! Found 3 database(s): admin, config, local
✅ Inserted document with _id: 6979bd5484292f1d99ecc8b1
📖 Read back: "Hello from Koan.ZenGarden! 🌱"
🗑️ Cleaned up test document.
```
