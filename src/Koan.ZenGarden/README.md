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

## Wishful Offering Requests

Applications can declare a desired dependency even when it is not ready yet.
This is the recommended startup model for dynamic infrastructure.

### Ask for an offering (no capability constraint)

```csharp
using var sub = ZenGarden.Offering.On(
    "mongodb",
    async (evt, ct) =>
    {
        if (evt.Kind == ZenGardenAvailabilityEventKind.Online)
        {
            await appFeatures.EnableMongoAsync(evt.Current, ct);
        }

        if (evt.Kind == ZenGardenAvailabilityEventKind.Offline)
        {
            await appFeatures.DisableMongoAsync(ct);
        }
    });
```

### Ask for an offering with required capabilities

```csharp
using var sub = ZenGarden.Offering.On(
    "ollama",
    ["llama3.2", "nomic-embed-text"],
    async (evt, ct) =>
    {
        if (evt.Kind == ZenGardenAvailabilityEventKind.CapabilitiesSatisfied)
        {
            await appFeatures.EnableAiRouteAsync(evt.Current, ct);
        }

        if (evt.Kind == ZenGardenAvailabilityEventKind.CapabilitiesUnsatisfied ||
            evt.Kind == ZenGardenAvailabilityEventKind.Offline)
        {
            await appFeatures.DisableAiRouteAsync(ct);
        }
    });
```

### Wishful query at startup (point-in-time)

```csharp
var mongo = await ZenGarden.Offering.Catalog("mongodb", ct);
if (mongo?.Ready == true)
{
    await appFeatures.EnableMongoAsync(mongo, ct);
}

var ollamaWithModels = await ZenGarden.Offering.Catalog(
    "ollama",
    ["llama3.2", "nomic-embed-text"],
    ct);
```

Capability notes:

- Use bare capability names by default (`llama3.2`, `nomic-embed-text`).
- Prefixes like `model:` are optional and only needed for disambiguation.

## Event Kinds

- `Online`
- `Offline`
- `Changed`
- `CapabilitiesSatisfied`
- `CapabilitiesUnsatisfied`

## Listening And Adapting To Announcements

`Koan.ZenGarden` consumes Zen Garden tool announcements from the stream and emits
derived app events. The handler is the application adaptation point.

```csharp
using var sub = ZenGarden.Offering.On(
    "redis",
    async (evt, ct) =>
    {
        switch (evt.Kind)
        {
            case ZenGardenAvailabilityEventKind.Online:
                await runtime.BindRedisAsync(evt.Current, ct);
                break;
            case ZenGardenAvailabilityEventKind.Changed:
                await runtime.RefreshRedisBindingAsync(evt.Current, ct);
                break;
            case ZenGardenAvailabilityEventKind.Offline:
                await runtime.UnbindRedisAsync(ct);
                break;
        }
    },
    new ZenGardenWatchOptions
    {
        // true by default: emit current state immediately after subscribe.
        EmitInitialState = true
    });
```

Operational guidance:

- Keep handlers idempotent. Duplicate or replayed stream events can happen.
- Use `evt.Current` as the source of truth for connection/capability state.
- Treat `Changed` as a rebind signal when online/offline did not change.
- Prefer enabling/disabling features instead of throwing during startup.

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

## Connection Intent URIs

`Koan.ZenGarden` is an initialization provider for adapter connection intent resolution.

Minimum valid URI:

```text
zen-garden://<offering>
```

Example:

```text
zen-garden://mongodb
```

Optional extended forms:

- `zen-garden://<offering>:<instance>`
- `zen-garden://<offering>?cap=<item>[,<item>...]`

Capability notes:

- Bare capability items are preferred (`llama3.2`, `nomic-embed-text`).
- Typed selectors remain optional for disambiguation only.

Resolution order:

1. Explicit native connection string -> pass-through.
2. `zen-garden://...` -> resolve via Zen Garden first, then connector autonomous discovery fallback.
3. `auto` or empty with `Koan.ZenGarden` referenced -> Zen Garden first, then connector autonomous discovery fallback.

Selector note:

- `zen-garden://<offering>` resolves the base offering first (`offering:<offering>`).
- If only instance variants exist, resolution falls back to the first ready `offering:<offering>:<instance>`.

### MongoDB adapter behavior

- Default auto path (`ConnectionString` empty or `auto`):
  - resolves `mongodb` through Zen Garden first
  - falls back to existing Mongo autonomous discovery if unresolved
- Explicit Zen Garden URI path:
  - `Koan:Data:Mongo:ConnectionString = "zen-garden://mongodb"`
  - `Koan:Data:Mongo:ConnectionString = "zen-garden://mongodb:dev"`
  - unresolved intent falls back to Mongo autonomous discovery

Optional Mongo Zen Garden overrides:

- `Koan:Data:Mongo:ZenGarden:Offering`
- `Koan:Data:Mongo:ZenGarden:Instance`
- `Koan:Data:Mongo:ZenGarden:Capabilities` (array or csv)

### Ollama adapter behavior

- Explicit single connection intent:
  - `Koan:Ai:Ollama:ConnectionString = "zen-garden://ollama?cap=llama3.2,nomic-embed-text"`
- Explicit URL list still works, and each URL can also be a Zen Garden URI:
  - `Koan:Ai:Ollama:Urls:0 = "zen-garden://ollama"`
- Auto path (no explicit members, or unresolved explicit intent):
  - resolves `ollama` through Zen Garden first
  - falls back to existing host/container/local Ollama discovery
  - `AdditionalUrls` are then merged as fallback members

Ollama capability requirements passed to Zen Garden are sourced from:

- `Koan:Ai:Ollama:RequiredCapabilities`
- `Koan:Ai:Ollama:RequiredModels`
- `Koan:Ai:Ollama:ZenGarden:Capabilities`

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
