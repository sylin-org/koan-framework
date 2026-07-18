# Koan.ZenGarden

Greenfield Zen Garden tools-domain runtime for Koan applications.

`Koan.ZenGarden` subscribes to Zen Garden's normative tools APIs and emits app-friendly events when offerings and seed banks become ready, unavailable, or change capabilities.

## Quick Start

```csharp
using Koan.ZenGarden;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

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

Referencing `Koan.ZenGarden` is the activation decision. `AddKoan()` registers the runtime and compiles
its optional discovery source; no module-specific activation call is required. Configure non-default
behavior under `Koan:ZenGarden`.

## API Surface

### Typed core subscription (`ZenGarden.On<TEvent>`)

```csharp
using var availability = ZenGarden.On<ZenGardenAvailabilityEvent>(
    ZenGardenSubscription.ForOffering("mongodb"),
    async (evt, ct) => { /* online/offline/changed */ });
```

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

### Capability wishes and progress

```csharp
var wish = await ZenGarden.Capability.Wish(
    "ollama",
    ["llama3.2", "nomic-embed-text"]);

using var progress = ZenGarden.On<ZenGardenCapabilityProgressEvent>(
    wish,
    async (evt, ct) =>
    {
        // Requested -> InProgress -> PartiallyFulfilled -> Fulfilled
        Console.WriteLine($"{evt.Kind} missing={string.Join(",", evt.Wish.Missing)}");
    });
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

Capability progress kinds (`ZenGardenCapabilityProgressEventKind`):

- `Requested`
- `InProgress`
- `PartiallyFulfilled`
- `Fulfilled`
- `Failed`

## Centralized Non-Blocking Capability Orchestration

`Koan.ZenGarden` is the orchestration owner for offering capability fulfillment.
Adapters pass intent; they do not schedule ensures directly.

Flow:

1. Application declares intent:
   - subscription (`ZenGarden.Offering.On(...)`)
   - explicit wish (`ZenGarden.Capability.Wish(...)`)
   - initialization URI (`zen-garden://<offering>?cap=...`)
2. `IZenGardenInitializationProvider.Resolve(intent)` resolves the best ready offering candidate.
3. If requested capabilities are missing, the provider schedules wishful ensures internally (throttled/deduped) and returns immediately.
4. Startup continues without waiting for fulfillment.
5. SSE stream updates drive:
   - capability progress events (`Requested`, `InProgress`, `PartiallyFulfilled`, `Fulfilled`, `Failed`)
   - availability capability transitions (`CapabilitiesSatisfied`, `CapabilitiesUnsatisfied`)

Contract:

- Non-blocking by default.
- App adaptation happens in event handlers.
- Adapter modules consume resolved state/endpoints only.

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
  - container host endpoint (when containerized)
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

### Containerized Apps

When running in containers (`DOTNET_RUNNING_IN_CONTAINER=true`), `Koan.ZenGarden` can require Moss on the host side.

Defaults:

- `RequireHostMossWhenContainerized = true`
- `ContainerHost = "host.docker.internal"`
- `ContainerHostPort = 7185`

If host Moss is unreachable in container mode, startup resolution fails fast with a clear error.

Override host alias/name:

```json
{
  "Koan": {
    "ZenGarden": {
      "ContainerHost": "moss-host",
      "ContainerHostPort": 7185
    }
  }
}
```

Environment-variable overrides:

- `KOAN_ZENGARDEN_CONTAINER_HOST`
- `KOAN_ZENGARDEN_CONTAINER_HOST_PORT`
- `KOAN_ZENGARDEN_REQUIRE_HOST_MOSS`

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
2. Explicit `zen-garden://...` -> resolve through the active Zen Garden source or fail correctively; explicit intent never becomes localhost or another provider.
3. `auto` or empty without the `Koan.ZenGarden` engine -> the connector's normal discovery path.
4. `auto` or empty with the engine activated -> Zen Garden contributes an automatic, health-checked
   candidate to the concern-owned coordinator; the connector still elects and may fall through.

Selector note:

- `zen-garden://<offering>` resolves the base offering first (`offering:<offering>`).
- If only instance variants exist, resolution falls back to the first ready `offering:<offering>:<instance>`.

### MongoDB adapter behavior

- Default auto path (`ConnectionString` empty or `auto`):
  - runs the health-checked discovery probe; the compiled Zen Garden source offers its resolved `mongodb`
    endpoint as one candidate, tried ahead of the
    compose-name / `host.docker.internal` / `localhost` guesses but **health-checked like all of
    them** — so an unreachable ZG answer (e.g. a same-host offering advertised on an interface the
    app can't reach) falls through instead of stranding the app. Zen Garden informs discovery here;
    it no longer short-circuits it.
- Explicit Zen Garden URI path (honors the requested offering/instance through the required discovery path):
  - `Koan:Data:Mongo:ConnectionString = "zen-garden://mongodb"`
  - `Koan:Data:Mongo:ConnectionString = "zen-garden://mongodb:dev"`
  - unresolved, unavailable, or unhealthy intent fails with a correction; it never falls back autonomously

To pin a non-default offering or instance for the auto path, use the explicit
`zen-garden://<offering>:<instance>` connection string above. (The former per-adapter
`Koan:Data:Mongo:ZenGarden:Offering` / `Instance` / `Capabilities` auto-path override keys were
removed when Zen Garden became a discovery contributor.)

### Ollama adapter behavior

- Explicit Zen Garden intent uses standard .NET connection-string configuration:
  - `ConnectionStrings:Ollama = "zen-garden://ollama?cap=llama3.2,nomic-embed-text"`
- An explicit Zen Garden intent that cannot resolve or pass Ollama's health check fails correctively; it is never
  weakened into automatic fallback.
- `Koan:Ai:Ollama:Endpoints` is the native HTTP mesh surface. It deliberately accepts only absolute HTTP/HTTPS
  endpoints; use `ConnectionStrings:Ollama` for a source intent.
- With no explicit placement, Core runs one composed discovery election. The active Zen Garden source contributes a
  candidate ahead of conventional container, Docker-host, and local guesses; Ollama health-checks every candidate.
- `DefaultModel` and `RequiredCapabilities` travel through the neutral discovery context. Zen Garden can schedule a
  wish for missing capabilities, but startup does not wait for fulfillment.

Ollama capability requirements passed to Zen Garden are sourced from:

- `Koan:Ai:Ollama:DefaultModel`
- `Koan:Ai:Ollama:RequiredCapabilities`

Non-blocking behavior:

- Startup does not wait for capability fulfillment.
- Capability progress is observable through `ZenGarden.Capability.On(...)` / `ZenGarden.On<ZenGardenCapabilityProgressEvent>(...)`.
- Applications should gate feature activation on progress or on offering capability availability events.

## Configuration

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
      "DedupeWindowSize": 4096,
      "RequireHostMossWhenContainerized": true,
      "ContainerHost": "host.docker.internal",
      "ContainerHostPort": 7185
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
    DedupeWindowSize = 4096,
    RequireHostMossWhenContainerized = true,
    ContainerHost = "host.docker.internal",
    ContainerHostPort = 7185
};
```
