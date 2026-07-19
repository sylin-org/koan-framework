# Sylin.Koan.ZenGarden technical reference

## Scope

This package is Koan's tools-domain runtime client for Zen Garden. The independently consumable wire and intent
boundary lives in `Sylin.Koan.ZenGarden.Contracts`.

## Decision

The runtime adopts a typed event model with a non-blocking wishful capability workflow:

- application primitives: `ZenGarden.Offering`, `ZenGarden.Storage`, and `ZenGarden.Capability`
- durable subscriptions use the standard hosted-service lifecycle after the host has started
- capability requests are scheduled wishfully and never block startup
- capability orchestration is centralized in `IZenGardenInitializationProvider.Resolve(intent)`
- capability fulfillment is reported incrementally (`PartiallyFulfilled`) and finally (`Fulfilled`) from tools SSE updates

It uses discovery-first Moss endpoint binding:

- explicit endpoint/selector overrides when provided
- `GARDEN_STONE` selector support
- container host binding in containerized runtime (`DOTNET_RUNNING_IN_CONTAINER=true`)
- UDP discovery with cache
- automatic re-discovery and rebind on connection failure

It does not use `/api/v1/services` as a primary catalog source.

## Module Boot

- Activation: generated descriptor metadata retains one `Initialization/ZenGardenModule.cs` instance when the application directly references `Koan.ZenGarden`.
- DI entrypoint: the application calls `AddKoan()`; the retained module owns runtime registration and contributes one typed discovery source.
- Structural composition: Core freezes the source plan once after the outermost `AddKoan(...)` declaration; live topology is queried per operation.
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

### Koi (mDNS-to-HTTP bridge)

`Koan.ZenGarden` can use Koi as an authoritative, continuous topology source.
Koi runs as a host daemon and exposes mDNS over HTTP so containers can see LAN
services without multicast.

Endpoints used by the Koi handler:

- `GET /healthz` (liveness probe)
- `GET /v1/status` (version + capability status)
- `GET /v1/mdns/discover?type=_moss._tcp&idle_for=3` (initial snapshot)
- `GET /v1/mdns/subscribe?type=_moss._tcp&idle_for=0` (continuous stream)

Optional lantern discovery uses the same endpoints with `type=_lantern._tcp`.
The SSE streams follow Koi's event format: `data: {"found": {...}}`,
`data: {"resolved": {...}}`, `data: {"removed": {...}}`.

Koi also exposes DNS, certmesh, health, and proxy capabilities, but the ZenGarden
handler only consumes the mDNS stream plus health/status endpoints.

### Topology API

- `GET /api/v1/garden/topology`
- Returns `{"data": [...]}` envelope containing `TopologyEntry` objects: `stone_id`, `stone_name`, `endpoint`, `moss_version`, `health`, `last_seen`
- Consumed for active topology hydration (failover roster)
- Note: the HTTP response uses an envelope; the file (`garden-topology.json`) is a bare array

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

1. Moss endpoint resolution (ordered by priority):
   1. Seed from own roster (`garden-stones.json`) and Moss topology (`garden-topology.json`) into in-memory cache (once on first resolution)
  2. Currently bound Stone (skipped on forced rediscovery after failure)
  3. Koi snapshot: Stones (authoritative when connected)
  4. Koi snapshot: Lanterns (cross-subnet topology when present)
  5. Explicit endpoint / `GARDEN_STONE` selector
  6. Preferred Stone name (soft affinity via `PreferredStoneName` option)
  7. In-memory cache (includes seeded persisted entries with refreshed timestamps)
  8. Container host binding (`host.docker.internal`)
  9. Persisted roster re-read (catches sibling container writes since seeding)
  10. UDP multicast/broadcast discovery
2. Endpoint health checks and automatic rebind on failure.
3. Snapshot reads for catalog queries.
4. Long-lived SSE stream consumption.
5. Local projection cache keyed by `tool_fqid`.
6. Event-id dedupe window.
7. Derived availability event emission to subscribers.
8. Non-blocking capability wish state tracking and progress emission.
9. Passive topology enrichment from tool snapshot events.
10. Persistent Stone roster with merge-on-write.

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
- Explicit `zen-garden://...` resolution is required: resolve and pass adapter health policy, or fail correctively without autonomous fallback.
- Offering-only intents (`zen-garden://mongodb`) resolve `offering:mongodb` first and fall back to ready instance candidates (`offering:mongodb:<instance>`) when needed.

## Initialization Provider

`Koan.ZenGarden` registers `IZenGardenInitializationProvider`. Its compiled discovery source receives the
selected adapter's canonical service name and stable aliases, eliminating connector-specific binding metadata.

Provider responsibilities:

- parse and normalize Zen Garden intent URIs (`zen-garden://...`)
- try canonical service selectors in adapter-declared order
- resolve ready offering projections through tools snapshot API
- return connection metadata (`uris`, `protocol`, `host`, `port`, capabilities)
- when `Resolve(intent)` includes capability requirements:
  - evaluate missing capabilities against current snapshot
  - schedule wishful ensures non-blocking (with scheduling throttle)
  - return resolved endpoint immediately so startup can proceed

Boundary:

- `Koan.ZenGarden` owns capability orchestration and fulfillment tracking.
- Adapter modules provide intent and consume resolved state; they do not invoke capability ensure directly.

The source is adapter-neutral. For example, Mongo declares `mongo` plus alias `mongodb`; the source tries
those selectors while Mongo retains connection normalization and health ownership.

## Adapter Integration

Mongo (`MongoOptionsConfigurator`):

- explicit native connection strings remain pass-through
- explicit `zen-garden://...` uses the required shared discovery path and fails if it cannot be honored
- `auto` / empty uses the compiled Zen Garden source as one health-checked candidate, then autonomous Mongo fallback

Ollama (`OllamaAdapterContributor`):

- `ConnectionStrings:Ollama` accepts one native HTTP endpoint or one `zen-garden://...` required intent
- `Koan:Ai:Ollama:Endpoints[*]` accepts an ordered native HTTP/HTTPS mesh
- explicit connection string and endpoint mesh are mutually exclusive
- automatic discovery receives Zen Garden candidates through Core's compiled discovery plan, then conventional
  container/host/local candidates; Ollama health-checks the elected endpoint
- unresolved explicit Zen Garden intent fails and is not replaced by an automatic candidate
- `DefaultModel` plus `RequiredCapabilities` use `DiscoveryContext.RequiredCapabilities`; the Zen Garden source turns
  them into a capability-bearing offering intent
- the initialization provider schedules a wish when capabilities are missing and returns without waiting for
  fulfillment

## Centralized Orchestration Flow

`Resolve(intent-with-capabilities)` runtime sequence:

1. Resolve offering candidate (`ready=true`) by selector/instance/alias.
2. Compare requested capabilities with current projection.
3. If missing:
   - enqueue wishful ensure through tools-domain capability endpoint (non-blocking)
   - emit progress through capability stream updates
4. Return offering resolution immediately.
5. App adapts later on `ZenGardenCapabilityProgressEvent` and `ZenGardenAvailabilityEvent` capability transitions.

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
- `RequireHostMossWhenContainerized` (default `true`)
- `ContainerHost` (default `host.docker.internal`)
- `ContainerHostPort` (default `7185`)
- `PersistDiscoveryCache` (default `true`)
- `DiscoveryCachePath` (optional explicit path; auto-resolved when null)
- `PersistedCacheTtlHours` (default `168` = 7 days)
- `PreferredStoneName` (optional soft-affinity Stone name)
- `KoiEndpoint` (optional explicit Koi endpoint, default `http://localhost:5641`)
- `KoiDiscoveryEnabled` (default `true`)
- `KoiHealthTimeout` (default 500ms)
- `KoiBrowseIdleTimeout` (default 3s)
- `KoiContinuousDiscovery` (default `true`)
- `KoiLanternDiscovery` (default `true`)
- `KoiRetryInterval` (default 30s)

Koi env overrides:
- `KOAN_ZENGARDEN_KOI_ENDPOINT`
- `KOAN_ZENGARDEN_KOI_ENABLED`

Containerized resolution policy:

- If `DOTNET_RUNNING_IN_CONTAINER=true` and `RequireHostMossWhenContainerized=true`:
  - `Koan.ZenGarden` requires host Moss reachability through `ContainerHost`/`ContainerHostPort`
  - if unreachable on first startup, resolution fails fast with explicit configuration guidance
  - UDP discovery is not used as the primary path in this mode

Containerized reconnection failover:

- On reconnection (`forceRediscovery`) in a container where all health-checked paths fail:
  - UDP discovery cannot cross container network boundaries
  - The adapter relies on cached Stone topology for failover
  - Best alternative Stone (most recently seen, excluding the known-down container host) is
    returned optimistically without health check
  - If no alternative stones exist in cache, falls back to the container host optimistically
  - The caller's HTTP request fails naturally and the reconnect loop retries
  - The `RequireHostMossWhenContainerized` configuration error is only thrown on first-time
    resolution, not during reconnection

## Persistent Stone Roster

The client persists discovered Moss Stone metadata to disk for failover recovery.

### Shared Topology Directory

Moss and clients coexist in a shared directory:

```
/app/cache/zen-garden/                  (container mount point)
  garden-topology.json                  ← Moss writes (authoritative mesh snapshot)
  garden-stones.json                    ← Clients write (operational roster)
```

On the host, Moss writes to the system-wide data directory:
- Linux/macOS: `/var/lib/zen-garden/topology/garden-topology.json`
- Windows: `C:\ProgramData\zen-garden\topology\garden-topology.json`
- Override: `GARDEN_DATA_DIR` env var → `{GARDEN_DATA_DIR}/topology/garden-topology.json`

See `DATA-0090-shared-topology-directory.md` for the architectural decision.

### File Format

`garden-stones.json` — a JSON array of `CachedMossStone` records (client-owned):

```json
[
  {
    "Endpoint": "http://192.168.1.50:7185",
    "StoneId": "abc123",
    "StoneName": "moss-primary",
    "MossVersion": "0.9.1",
    "LanternEndpoint": "http://192.168.1.50:3000",
    "LastSeenUtc": "2026-02-07T12:00:00+00:00"
  }
]
```

`garden-topology.json` — a bare JSON array of Moss `TopologyEntry` records (Moss-owned, read-only):

```json
[
  {
    "stone_id": "019abc12-...",
    "stone_name": "stone-coral-prairie",
    "endpoint": "http://192.168.1.50:7185",
    "moss_version": "0.9.1",
    "last_seen": "2026-02-07T12:00:30Z",
    "health": "thriving",
    "status": "online"
  }
]
```

**Note**: The file is a bare JSON array — NOT the HTTP API envelope (`{"data": [...]}`).
The client only extracts: `stone_id`, `stone_name`, `endpoint`, `moss_version`, `last_seen`.

### Cold-Start Seeding Priority

```
1. Own roster (garden-stones.json)     ← Client's operational knowledge, wins on key conflict
2. Moss topology (garden-topology.json) ← Fills gaps from Moss's mesh view
3. Active hydration (HTTP)              ← Live fetch on bind, refreshes everything
4. SSE stream (real-time)               ← Continuous updates after connection
```

The first two are file reads (< 1ms). Steps 3-4 require Moss to be reachable.

### Location Resolution

1. `Koan:ZenGarden:DiscoveryCachePath` (explicit config)
2. `KOAN_ZENGARDEN_CACHE_PATH` environment variable
3. `/app/cache/zen-garden/` when containerized (`DOTNET_RUNNING_IN_CONTAINER=true`) and
   the standard `/app/cache` volume mount exists
4. `.Koan/zen-garden/` relative to working directory (host convention)

### Write Behavior

- Write-through on `BindStone()` only (not every cache update)
- Fire-and-forget from the bind path (no I/O blocking)
- Merge-on-write: reads existing file, merges by CacheKey (newer wins), atomically replaces
- Atomic rename: writes to `garden-stones.json.tmp` then `File.Move(overwrite: true)`
- `SemaphoreSlim` serializes writes within a single process

### TTL

- In-memory cache TTL: `DiscoveryCacheTtlSeconds` (default 90s) — hot path
- Persisted roster TTL: `PersistedCacheTtlHours` (default 168h / 7 days) — cold failover path
- Expired entries are filtered on load and pruned on write

### Migration

On first `LoadAsync()`, if `garden-stones.json` does not exist but `stones.json` does at the
same resolved path, it is renamed automatically. This one-time migration preserves existing
cached topology from before the rename. On shared volumes with concurrent container starts,
the rename is race-safe (first writer wins, others continue gracefully).

### Cross-Container Safety

Multiple containers sharing a bind-mounted directory:
- Each container reads before writing, merges its knowledge, then atomically replaces
- `File.Move(overwrite: true)` ensures the last writer wins with a valid file
- Readers always get a complete, consistent file
- Moss writes `garden-topology.json`; clients write `garden-stones.json` — no writer conflicts

## Active Topology Hydration

While connected to a Moss, the client periodically fetches the full Stone topology
via `GET /api/v1/garden/topology`. This returns all Stones known to the Moss mesh,
including peers that may not host any tools visible in the SSE stream.

- Triggered on `BindStone()` (fire-and-forget, immediate on connection)
- Refreshed on `tools.heartbeat` events (throttled to every 5 minutes)
- Response contains `stone_id`, `stone_name`, `endpoint`, `moss_version`, `last_seen`
- All learned Stones are cached and persisted to the roster
- The roster is written immediately after a hydration that learns new Stones

This is the primary mechanism for building failover capacity: when the host Moss goes
down in a container, the roster contains peer Stone endpoints learned while connected.

## Passive Topology Enrichment

The SSE tools stream carries `stone_id` and `stone_name` on each tool snapshot. As the
client processes `tool.upsert` events, it passively learns about Stones it has never
directly discovered.

- Derives Moss endpoint from tool connection `hostname` or `ip`: `http://{host}:7185`
- Only enriches when the Stone is not already cached with a fresh timestamp (< 5 min)
- Enriched entries are persisted on the next `BindStone()` call

This supplements active hydration by learning Stone details from tool connection
metadata, useful when the topology endpoint is unavailable.

## Preferred Stone Affinity

Operators can specify a preferred Moss Stone name via configuration:

- `Koan:ZenGarden:PreferredStoneName` in appsettings.json
- `KOAN_ZENGARDEN_PREFERRED_STONE` environment variable

This is a soft affinity — the adapter tries the named Stone before falling back to
the container host or general discovery. If the preferred Stone is unreachable,
resolution continues through the normal chain.

Matching uses the same `MatchesSelector` logic as `GARDEN_STONE`: supports stone
names, stone IDs, `host:port`, and `.local` suffixes.

## AI Model Advisor

`Koan.ZenGarden` registers `ZenGardenModelAdvisor` as `IAiModelAdvisor` (level 4 in the
AI resolution chain, see AI-0032).

Resolution chain for AI model selection:

```
1. Explicit ChatOptions.Model           (developer, per-request)
2. Ambient AiCategoryScope              (developer, per-code-block)
3. Active recipe binding                (ML engineer / DevOps, from Koan:Ai:Recipes)
4. IAiModelAdvisor (this advisor)       (system, from orchestrator /v1/recommendations)
5. Category configuration               (ops, Koan:Ai:{Category}:Model)
6. Source/member default model           (framework)
7. Hardcoded fallback                    (framework)
```

The advisor fetches `/v1/recommendations` from the Ollama orchestrator proxy and caches
rank-1 models per capability. Cache is refreshed non-blocking (stale-while-revalidate).

Proxy endpoint resolution priority:

1. Explicit `Koan:ZenGarden:OrchestratorProxyEndpoint`
2. Cached resolution from previous discovery
3. Zen Garden offering resolution (`ollama::orchestrator` intent)

Capability mapping (orchestrator wire → Koan category):

| Wire | Koan Category |
|------|---------------|
| `chat` | `Chat` |
| `embedding` | `Embed` |
| `ocr` | `Ocr` |
| `vision` | `Vision` |
| `quick` | `Quick` |
| `synthesis` | `Synthesis` |
| `thinking` | `Thinking` |
| `tools` | `Tools` |

### AI Recipes

Recipes are named capability-to-model bindings configured in `appsettings.json`:

```json
{
  "Koan": {
    "Ai": {
      "ActiveRecipe": "production-balanced",
      "Recipes": {
        "production-balanced": {
          "Chat": "qwen3.5:9b",
          "Embed": "nomic-embed-text",
          "Thinking": "qwq:32b"
        }
      }
    }
  }
}
```

Recipes sit above the advisor (level 3 vs level 4). When a recipe binding exists for a
capability, the advisor is not consulted for that capability. Omitted capabilities fall
through to the advisor. Environment-scoped recipes use standard ASP.NET configuration
layering (`appsettings.Production.json`).

See ADR AI-0032 for full specification.

## Non-Goals

- No backward compatibility wrappers for legacy discovery API.
- No dual-read/dual-write behavior across old and new surfaces.
