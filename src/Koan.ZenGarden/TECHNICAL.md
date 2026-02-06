# Koan.ZenGarden Technical Reference

## Scope

This project is a greenfield tools-domain runtime client for Zen Garden.

## Decision

`Koan.ZenGarden` adopts a typed event model with a non-blocking wishful capability workflow:

- canonical primitive: `ZenGarden.On<TEvent>(...)`
- ergonomic wrappers remain: `ZenGarden.Offering.On(...)`, `ZenGarden.Storage.On(...)`, `ZenGarden.Capability.On(...)`
- capability requests are scheduled wishfully and never block startup
- capability fulfillment is reported incrementally (`PartiallyFulfilled`) and finally (`Fulfilled`) from tools SSE updates

It uses discovery-first Moss endpoint binding:

- explicit endpoint/selector overrides when provided
- `GARDEN_STONE` selector support
- UDP discovery with cache
- automatic re-discovery and rebind on connection failure

It does not use `/api/v1/services` as a primary catalog source.

## Module Boot

- Auto-registration: `Initialization/KoanAutoRegistrar.cs` implements `IKoanAutoRegistrar`.
- DI entrypoint: `AddKoanZenGarden(...)` in `Extensions/ServiceCollectionExtensions.cs`.
- Runtime resolution: static `ZenGarden` facade resolves `IZenGardenClient` from `AppHost.Current`.

## Protocols

### Discovery

- UDP announcement envelope:
  - `type: discovery_request` + `data`
  - `type: discovery_response` + `data`
- default UDP:
  - multicast `239.255.42.99:7184`
  - optional broadcast fallbacks
- env overrides:
  - `GARDEN_DISCOVERY_TIMEOUT_SECS`
  - `DISCOVERY_PORT`
  - `DISCOVERY_MCAST_GROUP`
  - `DISCOVERY_ENABLE_BCAST_FALLBACK`
  - `DISCOVERY_ENABLE_LIMITED_BCAST`

### Snapshot API

- `GET /api/v1/garden/tools`
- Optional filters:
  - `tool_type`
  - `tool_fqid`
  - `capability`
  - `since`

### Stream API (SSE)

- `GET /api/v1/garden/tools/stream`
- Event types consumed:
  - `tools.snapshot`
  - `tool.upsert`
  - `tool.remove`
  - `tools.heartbeat`

## Runtime Architecture

`ZenGardenClient` performs:

1. Moss endpoint resolution:
   - explicit option endpoint
   - `GARDEN_STONE` selector
   - hot cache
   - UDP discovery
2. Endpoint health checks and automatic rebind on failure.
3. Snapshot reads for catalog queries.
4. Long-lived SSE stream consumption.
5. Local projection cache keyed by `tool_fqid`.
6. Event-id dedupe window.
7. Derived availability event emission to subscribers.
8. Non-blocking capability wish state tracking and progress emission.

### Local Projection

Each tool is represented as `ZenGardenToolSnapshot`:

- `tool_fqid`, `tool_uid`, `tool_type`
- `state`, `ready`, `revision`
- `connection`
- `capabilities`, `capability_revision`
- `stone_id`, `stone_name`

### Subscription Matching

`ZenGardenSubscription` contains:

- `ToolType` filter (offering / seed-bank)
- optional `ToolFqid`
- optional capability requirements (`AND` semantics)

Capability tokens support:

- untyped (recommended): `modelv1`
- typed (optional disambiguation): `extension:pgvector`
- separators: `,` or `|`

## Derived Event Semantics

`ZenGardenAvailabilityEventKind`:

- `Online`: tool transitions to ready.
- `Offline`: tool transitions away from ready or is removed.
- `Changed`: revision change without online/offline transition.
- `CapabilitiesSatisfied`: requirement set becomes satisfied.
- `CapabilitiesUnsatisfied`: requirement set becomes unsatisfied.

`ZenGardenCapabilityProgressEventKind`:

- `Requested`: wish accepted and tracked.
- `InProgress`: capability ensure requests submitted.
- `PartiallyFulfilled`: some requested capabilities are now present.
- `Fulfilled`: all requested capabilities are present.
- `Failed`: wish scheduling/ensure request failed.

## Wishful Dependency Contract

Wishful dependency means applications declare desired tools before those tools are
available, then adapt when announcements indicate readiness changes.

Application surfaces:

- `ZenGarden.Offering.On("mongodb", handler)`
- `ZenGarden.Offering.On("ollama", ["llama3.2"], handler)`
- `ZenGarden.Offering.Catalog("mongodb")`
- `ZenGarden.Offering.Catalog("ollama", ["llama3.2"])`
- `ZenGarden.Capability.Wish("ollama", ["llama3.2", "nomic-embed-text"])`
- `ZenGarden.Capability.On(wish, handler)`

Behavior:

- Missing tools do not fail registration of the subscription.
- `EmitInitialState=true` (default) emits current state immediately after subscribe.
- Capability-bound subscriptions receive capability transition events in addition to online/offline.
- Capability wish requests are non-blocking: startup continues while fulfillment progresses via SSE-driven updates.

Capability requirement matching:

- `modelv1`: untyped requirement, matches any capability type containing `modelv1`.
- `type:modelv1`: typed requirement, matches only `capabilities["type"]`.
- Multiple requirements are `AND`-ed.
- For model queries, type prefixes are generally unnecessary and treated as optional hints.

## Connection Intent URI Contract

For initialization-driven connection resolution, the minimum Zen Garden URI is:

```text
zen-garden://<offering>
```

Canonical minimum example:

```text
zen-garden://mongodb
```

Extended forms remain optional:

- `zen-garden://<offering>:<instance>`
- `zen-garden://<offering>?cap=<item>[,<item>...]`

Rules:

- Minimum form must always be accepted.
- Capability items are untyped by default; typed selectors are optional.
- Zen Garden resolution is first attempt for `zen-garden://...`, then connector autonomous discovery fallback.
- Offering-only intents (`zen-garden://mongodb`) resolve `offering:mongodb` first and fall back to ready instance candidates (`offering:mongodb:<instance>`) when needed.

## Initialization Provider

`Koan.ZenGarden` registers `IZenGardenInitializationProvider` and consumes connector-provided
`IZenGardenOfferingBinding` metadata from `Koan.ZenGarden.Core`.

Provider responsibilities:

- parse and normalize Zen Garden intent URIs (`zen-garden://...`)
- map adapter default offering bindings
- resolve ready offering projections through tools snapshot API
- return connection metadata (`uris`, `protocol`, `host`, `port`, capabilities)
- schedule capability ensures wishfully without blocking startup

Implemented adapter bindings:

- Mongo connector: `mongo` / `mongodb` -> offering `mongodb`
- Ollama connector: `ollama` -> offering `ollama`

## Adapter Integration

Mongo (`MongoOptionsConfigurator`):

- explicit native connection strings remain pass-through
- explicit `zen-garden://...` is resolved first, then autonomous Mongo discovery fallback
- `auto` / empty uses Zen Garden first (`mongodb` binding), then autonomous Mongo discovery fallback
- optional overrides:
  - `Koan:Data:Mongo:ZenGarden:Offering`
  - `Koan:Data:Mongo:ZenGarden:Instance`
  - `Koan:Data:Mongo:ZenGarden:Capabilities`

Ollama (`OllamaAdapterContributor`):

- `Koan:Ai:Ollama:ConnectionString` supports `zen-garden://...` or direct URL
- `Koan:Ai:Ollama:Urls[*]` also accepts Zen Garden intents per entry
- auto path (or unresolved explicit intent) runs Zen Garden first, then legacy host/container/local probes
- required capability hints forwarded to Zen Garden:
  - `Koan:Ai:Ollama:RequiredCapabilities`
  - `Koan:Ai:Ollama:RequiredModels`
  - `Koan:Ai:Ollama:ZenGarden:Capabilities`
- when required capabilities are missing, contributor schedules wishful ensure and continues registration

## Announcement Adaptation Flow

Runtime flow:

1. Moss stream event arrives (`tools.snapshot`, `tool.upsert`, `tool.remove`).
2. Local projection cache is updated (`tool_fqid` keyed).
3. Subscription predicates are evaluated.
4. Derived availability events are emitted to application handlers.
5. Application rebinds/disables features.

Recommended handler pattern:

- Handle `Online` by binding client/routes/features.
- Handle `Offline` by unbinding and degrading gracefully.
- Handle `CapabilitiesSatisfied` and `CapabilitiesUnsatisfied` for conditional features.
- Handle `Changed` as a refresh/reconfigure signal.

Reliability notes:

- Event stream is at-least-once; client keeps event-id dedupe window.
- Reconnect uses cursor / last-event-id where available.
- Adaptation handlers should be idempotent and safe to replay.

## Public Ergonomics

Preferred app-facing API:

```csharp
using var sub = ZenGarden.Offering.On("mongodb", async (evt, ct) => { });
using var capSub = ZenGarden.Offering.On("ollama", ["modelv1", "modelv2"], async (evt, ct) => { });
using var storageSub = ZenGarden.Storage.On("default", async (evt, ct) => { });

var wish = await ZenGarden.Capability.Wish("ollama", ["modelv1", "modelv2"]);
using var wishSub = ZenGarden.Capability.On(wish, async (evt, ct) => { });

using var typed = ZenGarden.On<ZenGardenAvailabilityEvent>(
    ZenGardenSubscription.ForOffering("mongodb"),
    async (evt, ct) => { });
```

Catalog access:

```csharp
var offerings = await ZenGarden.Offering.Catalog();
var storage = await ZenGarden.Storage.Catalog();
```

## Configuration

`ZenGardenOptions`:

- section: `Koan:ZenGarden`
- `Endpoint` (optional explicit selector/endpoint)
- `EnableDiscovery`
- `DiscoveryTimeoutSeconds`
- `DiscoveryPort`
- `DiscoveryMulticastGroup`
- `DiscoveryCacheTtlSeconds`
- `DiscoveryEnableBroadcastFallback`
- `DiscoveryEnableLimitedBroadcast`
- `HttpTimeoutSeconds`
- `StreamReconnectDelaySeconds`
- `DedupeWindowSize`

## Non-Goals

- No backward compatibility wrappers for legacy discovery API.
- No dual-read/dual-write behavior across old and new surfaces.
