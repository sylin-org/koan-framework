# Koan.ZenGarden

Greenfield Zen Garden tools-domain runtime for Koan applications.

`Koan.ZenGarden` subscribes to Zen Garden's normative tools APIs and emits app-friendly events when offerings and seed banks become ready, unavailable, or change capabilities.

## Quick Start

```csharp
using Koan.ZenGarden;
using Koan.ZenGarden.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
builder.Services.AddKoanZenGarden(configure: options =>
{
    options.EnableDiscovery = true;
});

using var mongodbSub = ZenGarden.Offering.On(
    "mongodb",
    async (evt, ct) =>
    {
        // Reconfigure routes/clients when Mongo comes online/offline.
    });

using var ollamaSub = ZenGarden.Offering.On(
    "ollama",
    ["modelv1", "modelv2"],
    async (evt, ct) =>
    {
        // React when required capabilities are satisfied/unsatisfied.
    });

using var storageSub = ZenGarden.Storage.On(
    "default",
    async (evt, ct) =>
    {
        // React to seed-bank readiness changes.
    });
```

## API Surface

### Offering events

```csharp
IDisposable On(
    string offering,
    Func<ZenGardenAvailabilityEvent, CancellationToken, ValueTask> handler,
    ZenGardenWatchOptions? options = null);

IDisposable On(
    string offering,
    IReadOnlyList<string> requires,
    Func<ZenGardenAvailabilityEvent, CancellationToken, ValueTask> handler,
    ZenGardenWatchOptions? options = null);
```

### Storage events

```csharp
IDisposable On(
    string seedBank,
    Func<ZenGardenAvailabilityEvent, CancellationToken, ValueTask> handler,
    ZenGardenWatchOptions? options = null);

IDisposable OnAny(
    Func<ZenGardenAvailabilityEvent, CancellationToken, ValueTask> handler,
    ZenGardenWatchOptions? options = null);
```

### Catalog

```csharp
Task<IReadOnlyList<ZenGardenToolSnapshot>> ZenGarden.Offering.Catalog(...)
Task<ZenGardenToolSnapshot?> ZenGarden.Offering.Catalog("ollama", ...)
Task<IReadOnlyList<ZenGardenToolSnapshot>> ZenGarden.Storage.Catalog(...)
Task<ZenGardenToolSnapshot?> ZenGarden.Storage.Catalog("default", ...)
```

## Event Kinds

- `Online`
- `Offline`
- `Changed`
- `CapabilitiesSatisfied`
- `CapabilitiesUnsatisfied`

## Selector Ergonomics

The offering selector can include bracketed requirements:

```csharp
var subscription = ZenGardenSubscription.Parse("ollama[modelv1,modelv2]");
```

You can also build explicitly:

```csharp
var subscription = ZenGardenSubscription.ForOffering("ollama")
    .Require("modelv1", "modelv2");
```

## Runtime Notes

- Source APIs:
  - `GET /api/v1/garden/tools`
  - `GET /api/v1/garden/tools/stream`
- Endpoint resolution:
  - explicit `ZenGardenOptions.Endpoint`
  - `GARDEN_STONE` environment selector
  - cached Moss binding (TTL-backed)
  - UDP discovery (`GARDEN_DISCOVERY_TIMEOUT_SECS`, `DISCOVERY_PORT`, `DISCOVERY_MCAST_GROUP` + broadcast fallbacks)
  - automatic re-discovery when bound endpoint stops responding
- Stream semantics:
  - snapshot-first
  - replay-friendly with cursor / event-id
  - at-least-once delivery; dedupe by event id in client runtime
- Tool coverage:
  - offerings (`tool_type=offering`)
  - seed banks (`tool_type=seed-bank`)

## Configuration

```csharp
builder.Services.AddKoanZenGarden(configure: options =>
{
    options.Endpoint = "http://stone-01:7185"; // optional explicit override
    options.EnableDiscovery = true;
    options.DiscoveryTimeoutSeconds = 3;
    options.DiscoveryPort = 7184;
    options.DiscoveryMulticastGroup = "239.255.42.99";
    options.DiscoveryCacheTtlSeconds = 90;
    options.DiscoveryEnableBroadcastFallback = true;
    options.DiscoveryEnableLimitedBroadcast = false;
    options.HttpTimeoutSeconds = 30;
    options.StreamReconnectDelaySeconds = 3;
    options.DedupeWindowSize = 4096;
});
```

Configuration section:

```json
{
  "Koan": {
    "ZenGarden": {
      "Endpoint": "http://stone-01:7185",
      "EnableDiscovery": true,
      "DiscoveryTimeoutSeconds": 3,
      "DiscoveryPort": 7184,
      "DiscoveryMulticastGroup": "239.255.42.99",
      "DiscoveryCacheTtlSeconds": 90,
      "DiscoveryEnableBroadcastFallback": true,
      "DiscoveryEnableLimitedBroadcast": false,
      "HttpTimeoutSeconds": 30,
      "StreamReconnectDelaySeconds": 3,
      "DedupeWindowSize": 4096
    }
  }
}
```

Direct options shape:

```csharp
new ZenGardenOptions
{
    Endpoint = "http://stone-01:7185", // optional
    EnableDiscovery = true,
    DiscoveryTimeoutSeconds = 3,
    DiscoveryPort = 7184,
    DiscoveryMulticastGroup = "239.255.42.99",
    DiscoveryCacheTtlSeconds = 90,
    DiscoveryEnableBroadcastFallback = true,
    DiscoveryEnableLimitedBroadcast = false,
    HttpTimeoutSeconds = 30,
    StreamReconnectDelaySeconds = 3,
    DedupeWindowSize = 4096
};
```
