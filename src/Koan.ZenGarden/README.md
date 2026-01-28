# Koan.ZenGarden

Zero-config infrastructure discovery for Koan Framework via [Zen Garden](https://github.com/your-org/zen-garden).

## Overview

`Koan.ZenGarden` enables Koan applications to automatically discover infrastructure services (MongoDB, Redis, RabbitMQ, etc.) running on Zen Garden Stones without any configuration. Just add the package and your app finds its dependencies.

## Quick Start

```csharp
// Create the client
using var httpClient = new HttpClient();
using var zenClient = new ZenGardenClient(httpClient, logger);

// Discover all Stones on the network
var stones = await zenClient.DiscoverStonesAsync();

// Find a specific service (searches all Stones)
var mongodb = await zenClient.FindServiceAsync("mongodb");
if (mongodb != null)
{
    var client = new MongoClient(mongodb.ConnectionString);
    // Use MongoDB...
}
```

## Features

- **UDP Multicast Discovery** - Finds Stones via 239.255.42.99:7184
- **Multi-Stone Search** - Searches all discovered Stones for your service
- **Connection String Building** - Constructs proper connection strings (mongodb://, redis://, etc.)
- **Caching** - Caches discovered services for app lifetime
- **Multi-homed Support** - Works correctly on Windows with WSL/Hyper-V

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  Koan Application                                           │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  ZenGardenClient                                     │   │
│  │  • DiscoverStonesAsync() - UDP multicast discovery   │   │
│  │  • FindServiceAsync() - Search for offering          │   │
│  │  • GetServicesAsync() - List all services on Stone   │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
                          │
                          │ UDP 7184 / HTTP 7185
                          ▼
┌─────────────────────────────────────────────────────────────┐
│  Zen Garden Stone (stone-coral-prairie)                     │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  Moss Daemon (HTTP :7185)                            │   │
│  │  • /health - Health check                            │   │
│  │  • /api/v1/services - List running services          │   │
│  │  • /api/v1/services/{name} - Get service details     │   │
│  └─────────────────────────────────────────────────────┘   │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐                    │
│  │ MongoDB  │ │  Redis   │ │ RabbitMQ │  ...               │
│  │  :27017  │ │  :6379   │ │  :5672   │                    │
│  └──────────┘ └──────────┘ └──────────┘                    │
└─────────────────────────────────────────────────────────────┘
```

## API Reference

### ZenGardenClient

| Method | Description |
|--------|-------------|
| `DiscoverStonesAsync()` | Discover all Stones via UDP multicast |
| `FindServiceAsync(offering)` | Find a service across all Stones, returns connection string |
| `GetServiceAsync(stone, name)` | Get specific service from a Stone |
| `GetServicesAsync(stone)` | List all services on a Stone |
| `IsStoneHealthyAsync(stone)` | Check if a Stone is reachable |
| `InvalidateOffering(name)` | Clear cached offering (triggers re-search) |
| `InvalidateStone()` | Clear Stone binding (triggers re-discovery) |

### Configuration

```csharp
var options = new ZenGardenOptions
{
    DiscoveryTimeoutSeconds = 5,    // UDP discovery timeout
    HttpTimeoutSeconds = 10,         // HTTP request timeout
    MulticastGroup = "239.255.42.99", // Multicast address
    DiscoveryPort = 7184,            // UDP port
    SchemeMappings = new Dictionary<string, string>
    {
        ["custom-service"] = "custom" // Custom scheme mapping
    }
};
```

## Caching Strategy

The client uses **two-level caching**:

1. **Topology Cache** - Discovered Stones (in-memory, app lifetime)
2. **Offering Cache** - Resolved service URLs (in-memory, app lifetime)

```
Request: FindServiceAsync("mongodb")
    │
    ├─► Check offering cache → HIT → Return cached connection
    │
    └─► MISS → Search all Stones
              │
              ├─► Found → Cache & return
              │
              └─► Not found → Return null
```

### Invalidation

- `InvalidateOffering("mongodb")` - Re-search on next request
- `InvalidateStone()` - Re-discover all Stones, clear all caches

## Error Handling

```csharp
try
{
    await mongoClient.PingAsync();
}
catch (MongoConnectionException)
{
    // Service may have moved - invalidate and re-discover
    zenClient.InvalidateOffering("mongodb");
    var newService = await zenClient.FindServiceAsync("mongodb");
    // Reconnect...
}
```

## Requirements

- .NET 10.0+
- Network access to Zen Garden Stones (UDP 7184, HTTP 7185)
- At least one Stone running on the network

## License

Apache 2.0 - See [LICENSE](../../LICENSE)
