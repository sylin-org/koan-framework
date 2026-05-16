# DATA-0091: Koi as authoritative topology handler for Koan.ZenGarden

Status: Accepted

## Context

Koan.ZenGarden's topology discovery has a fundamental limitation in container environments: UDP multicast does not cross Docker's bridge network. The current resolution chain (`ZenGardenClient.cs:1324-1449`) compensates with container host binding, persisted roster re-reads, and cached optimistic failover — but all of these rely on stale or pre-configured state. No mechanism provides **live, real-time topology awareness** from inside a container.

Meanwhile, [Koi](https://github.com/onose/koi) — a host-level mDNS-to-HTTP proxy — is already deployed as a bundled tool on Windows (`bin/tools/koi.exe`) and available standalone on Linux/macOS. Moss already registers itself with Koi as `_moss._tcp` with full TXT metadata (see `zen-garden/docs/archive/proposals/windows-mdns-via-koi.md`, status: implemented). Koi's SSE events stream provides real-time `found`, `resolved`, and `removed` events for any mDNS service type — including mDNS goodbye packets, which signal clean Stone shutdowns in milliseconds.

The current resolution chain treats topology as a **point-in-time query**: "give me a Moss endpoint right now." This forces discovery work onto the critical path (reconnect, failover), adding latency exactly when it hurts most. Koi's continuous event stream inverts this: topology is maintained as background state, always current, available for zero-cost reads.

### Forces

- Containers cannot do UDP multicast. The current chain's step 8 (UDP discovery) is unreachable from containers.
- Container host binding (step 5) is a single static endpoint. If that Moss dies, the only fallback is cached state.
- Persisted roster (step 6-7) survives restarts but doesn't detect topology changes between restarts.
- The Moss SSE tools stream provides real-time updates, but only from the **bound** Moss — it cannot discover alternatives.
- Koi sees **all** Stones on the local subnet and reports additions **and removals** in real-time.
- When Lanterns are introduced as a discoverable service type (`_lantern._tcp`), Koi becomes the bridge to cross-subnet topology.

## Decision

### 1. Introduce a self-contained KoiHandler as an async background process

The `KoiHandler` is an independent, singleton component that starts as early as possible (at DI registration time) and runs for the application lifetime. It maintains a persistent SSE connection to Koi, projects a local topology view, and fires events that `ZenGardenClient` intercepts.

The handler is a **pure state machine** with one input (Koi's SSE stream) and two outputs:
- A read-only `KoiTopologySnapshot` (immutable, for zero-cost point-in-time reads)
- Topology events (for proactive reactions by the client)

**The handler does not own persistence.** It fires events; the `ZenGardenClient` persists discovered Stones through the existing `StoneRosterStore` merge-on-write infrastructure. This avoids duplicating the shared-volume-safe persistence logic from DATA-0090.

### 2. KoiHandler state machine

```
                 ┌──────────────┐
        boot ───→│ Initializing │──→ (silent, no events)
                 └──────┬───────┘
                        │
              health probe GET /healthz (500ms timeout)
                        │
              ┌─────────┴──────────┐
              │                    │
              ▼                    ▼
   ┌───────────────┐    ┌──────────────────┐
   │ NotDetected   │    │ Connected        │──→ KoiAvailable event
   │               │    │                  │──→ TopologyReset event (initial browse)
   │ re-probe      │    │ SSE stream:      │──→ Stone*/Lantern* events as they arrive
   │ every 30s     │    │  _moss._tcp      │
   └───────┬───────┘    │  _lantern._tcp   │
           │            └────────┬─────────┘
           │                     │
           │ probe succeeds      │ stream break / health fail
           └──→ Connected        ▼
                        ┌──────────────────┐
                        │ Reconnecting     │──→ KoiLost event
                        │                  │
                        │ backoff:         │
                        │ 1s, 2s, 4s...30s │
                        └──────┬───────────┘
                               │
                     ┌─────────┴──────────┐
                     ▼                    ▼
              NotDetected          Connected ──→ KoiAvailable
                                               ──→ TopologyReset (reconcile)
```

`NotDetected` re-probes every 30 seconds to detect late-starting Koi. This is lightweight (single TCP health probe) and handles the case where Koi starts after the application.

### 3. Koi topology events

```csharp
public enum KoiTopologyEventKind
{
    StoneOnline,       // New _moss._tcp instance resolved on the network
    StoneOffline,      // _moss._tcp removed (mDNS goodbye or Koi lease expiry)
    StoneChanged,      // Same stone, updated TXT records (version, health, etc.)
    TopologyReset,     // Reconnect after gap — full reconciled snapshot
    LanternFound,      // New _lantern._tcp instance resolved
    LanternLost,       // _lantern._tcp instance removed
    KoiAvailable,      // Handler connected to Koi
    KoiLost            // Handler lost connection to Koi
}

public sealed record KoiTopologyEvent
{
    public required KoiTopologyEventKind Kind { get; init; }
    public DiscoveredStone? Stone { get; init; }
    public DiscoveredStone? Previous { get; init; }       // For StoneChanged
    public DiscoveredLantern? Lantern { get; init; }
    public required KoiTopologySnapshot Snapshot { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
```

The event model follows the existing ZenGarden convention: `Func<TEvent, CancellationToken, ValueTask>` callbacks with sequential invocation and per-handler error isolation.

### 4. Published snapshot

```csharp
public sealed record KoiTopologySnapshot
{
    public required KoiHandlerState State { get; init; }
    public required IReadOnlyList<DiscoveredStone> Stones { get; init; }
    public required IReadOnlyList<DiscoveredLantern> Lanterns { get; init; }
    public DateTimeOffset? LastUpdate { get; init; }
    public DateTimeOffset? KoiDetectedAt { get; init; }
    public string? KoiVersion { get; init; }
}

public enum KoiHandlerState
{
    Initializing,
    NotDetected,
    Connected,
    Reconnecting
}
```

The snapshot is immutable. The handler publishes a new snapshot on every topology change (swap reference, not mutation). Readers get a consistent point-in-time view without locks.

### 5. DiscoveredStone — mapped from Koi mDNS response

```csharp
public sealed record DiscoveredStone
{
    public required string StoneName { get; init; }       // TXT: stone_name, or mDNS instance name
    public string? StoneId { get; init; }                 // TXT: stone_id
    public required string Endpoint { get; init; }        // http://{ip}:{port}
    public string? LocalEndpoint { get; init; }           // http://{host} (e.g. moss-01.local:7185)
    public string? MossVersion { get; init; }             // TXT: version
    public string? Health { get; init; }                  // TXT: health
    public string? Mac { get; init; }                     // TXT: mac
    public DateTimeOffset DiscoveredAt { get; init; }
}
```

Maps directly to `CachedMossStone` for integration with the existing stone cache.

### 6. Koi is authoritative when Connected

When the handler is in `Connected` state, the `ZenGardenClient` treats its topology projection as **ground truth for the local subnet**:

**StoneOffline supersedes all cached state.** Koi receives mDNS goodbye packets — this is the fastest possible removal signal. On `StoneOffline`:
1. Evict the Stone from the in-memory cache immediately
2. Evict from the persisted roster
3. If the bound Moss is the offline Stone → initiate failover immediately, do not wait for TCP timeout or health check failure

**TopologyReset replaces cached topology.** On handler reconnect after a gap, the handler receives a fresh browse. Stones present in the client's cache but absent from the Koi snapshot are marked suspect and health-checked opportunistically. This prevents stale ghosts from Stones that died during the connection gap.

**StoneOnline feeds proactive failover.** New Stones are cached immediately as failover candidates. If the bound Moss fails later, alternatives are already known — zero discovery latency on failover.

For unclean deaths (crash, OOM, power loss), there is no mDNS goodbye. Koi's lease expiry detects these (120s default), comparable to TCP keepalive detection. The client's own SSE stream break triggers failover through the existing path. Koi adds a fast path for clean shutdowns without regressing unclean ones.

### 7. Revised resolution chain

```
 1. Active SSE connection (live stream to bound Moss — verified connected, not just "was bound")
 2. Koi snapshot: Stones (zero-cost read from handler projection)
 3. Koi snapshot: Lanterns → query Lantern for cross-subnet topology
 4. Explicit endpoint / GARDEN_STONE
 5. Preferred stone name affinity (filters Koi results and cache)
 6. In-memory cache (continuously fed by Koi events)
 7. Container host binding
 8. Persisted roster re-read
 9. Cached optimistic failover
10. UDP discovery
```

Steps 2 and 3 are **instant** — reading an immutable struct, not making network calls. The handler has already done the work in the background.

The distinction at step 1 is deliberate: "bound stone" means "we have a live SSE connection right now," not "we connected at some point." If the SSE stream is broken and reconnecting, the bound stone is stale — Koi (step 2) takes over.

Step 3 (Lantern query) is the only network call in the fast path. It fires only when local Stones (step 2) are insufficient and a Lantern is available. Steps 2 and 3 can run in parallel — browse results and Lantern query race; first healthy Stone wins.

### 8. ZenGardenClient event interception

The client subscribes to handler events during construction:

```csharp
_koiSubscription = _koiHandler.OnTopologyEvent(async (evt, ct) =>
{
    switch (evt.Kind)
    {
        case KoiTopologyEventKind.StoneOnline:
            CacheStone(evt.Stone);
            _ = PersistRosterAsync(ct);  // fire-and-forget
            break;

        case KoiTopologyEventKind.StoneOffline:
            EvictStone(evt.Stone);
            _ = PersistRosterAsync(ct);
            if (IsBoundTo(evt.Stone))
                _ = InitiateFailoverAsync(ct);
            break;

        case KoiTopologyEventKind.StoneChanged:
            UpdateCachedStone(evt.Stone);
            break;

        case KoiTopologyEventKind.TopologyReset:
            ReconcileCacheWithSnapshot(evt.Snapshot);
            _ = PersistRosterAsync(ct);
            break;

        case KoiTopologyEventKind.LanternFound:
            AddLantern(evt.Lantern);
            break;

        case KoiTopologyEventKind.LanternLost:
            RemoveLantern(evt.Lantern);
            break;

        case KoiTopologyEventKind.KoiLost:
            _logger.LogInformation(
                "Koi connection lost; topology frozen at last known state");
            break;
    }
});
```

The handler's `IDisposable` subscription follows the same pattern as the existing `SubscriptionHandle` in `ZenGardenClient`: `ConcurrentDictionary` storage, `Interlocked.Exchange` guard on dispose, `TryRemove` on unsubscription.

### 9. Handler interface

```csharp
public interface IKoiHandler
{
    KoiTopologySnapshot CurrentSnapshot { get; }
    KoiHandlerState State { get; }

    IDisposable OnTopologyEvent(
        Func<KoiTopologyEvent, CancellationToken, ValueTask> handler);
}
```

Nothing outside the handler knows how to talk to Koi. The handler could be backed by Koi's HTTP API today and replaced with a different mechanism tomorrow. The boundary is `KoiTopologySnapshot` and `KoiTopologyEvent` — the transport is an implementation detail.

### 10. Koi endpoint auto-detection

Resolution order for the Koi endpoint itself:

1. Explicit config: `ZenGardenOptions.KoiEndpoint`
2. Environment variable: `KOAN_ZENGARDEN_KOI_ENDPOINT`
3. Container auto-detect: `http://host.docker.internal:5641` (when `DOTNET_RUNNING_IN_CONTAINER=true`)
4. Host auto-detect: `http://localhost:5641`

The handler probes `GET /healthz` with a 500ms timeout. If unreachable, it enters `NotDetected` and re-probes every 30 seconds.

### 11. Configuration

```csharp
// ZenGardenOptions additions
public string? KoiEndpoint { get; set; }                  // null = auto-detect
public bool KoiDiscoveryEnabled { get; set; } = true;
public TimeSpan KoiHealthTimeout { get; set; } = TimeSpan.FromMilliseconds(500);
public TimeSpan KoiBrowseIdleTimeout { get; set; } = TimeSpan.FromSeconds(3);
public bool KoiContinuousDiscovery { get; set; } = true;  // background SSE stream
public bool KoiLanternDiscovery { get; set; } = true;     // also browse _lantern._tcp
public TimeSpan KoiRetryInterval { get; set; } = TimeSpan.FromSeconds(30);
```

Environment overrides: `KOAN_ZENGARDEN_KOI_ENDPOINT`, `KOAN_ZENGARDEN_KOI_ENABLED`, `KOAN_ZENGARDEN_KOI_CONTINUOUS`, `KOAN_ZENGARDEN_KOI_LANTERN_ENABLED`.

### 12. Koi SSE-to-event mapping

The handler consumes `GET /v1/mdns/events?type=_moss._tcp&idle_for=0` and maps Koi events:

| Koi SSE event | Handler action | Emitted event |
|---|---|---|
| `{"event":"found","service":{...}}` | Parse TXT, add to projection | — (wait for resolved) |
| `{"event":"resolved","service":{...}}` | Upsert in projection with full details | `StoneOnline` (new) or `StoneChanged` (update) |
| `{"event":"removed","service":{...}}` | Remove from projection | `StoneOffline` |
| Stream connected | Mark `Connected`, snapshot projection | `KoiAvailable` + `TopologyReset` |
| Stream break | Mark `Reconnecting`, start backoff | `KoiLost` |

On reconnect after a gap, the handler compares the new browse results against its last known projection:
- Present in new but not old → `StoneOnline`
- Present in old but not new → `StoneOffline`
- Present in both with changed TXT → `StoneChanged`
- Then `TopologyReset` with the full reconciled snapshot

For `_lantern._tcp` (when enabled), the handler opens a parallel SSE stream or multiplexes service types. Same mapping logic produces `LanternFound` / `LanternLost` events.

### 13. No .NET Koi client library — internal implementation

Koi's HTTP API is simple (REST + SSE). Rather than creating a separate NuGet package, the handler contains an internal HTTP client (~150-200 lines) using `HttpClient` for health probes and `HttpClient.GetStreamAsync()` for SSE consumption. The ZenGardenClient already has SSE parsing infrastructure for the Moss tools stream — the Koi SSE format is simpler (`data: {json}` lines, no event ID tracking or cursor).

### 14. DI registration and startup

```csharp
// In ServiceCollectionExtensions.AddKoanZenGarden()
services.AddSingleton<IKoiHandler>(sp =>
{
    var options = sp.GetRequiredService<IOptions<ZenGardenOptions>>();
    var logger = sp.GetRequiredService<ILogger<KoiHandler>>();
    var handler = new KoiHandler(options.Value, logger);
    if (options.Value.KoiDiscoveryEnabled)
        handler.Start();  // begins async health probe + SSE immediately
    return handler;
});
```

The handler starts at DI resolution time — before the first `ZenGarden.Offering.On(...)` call. By the time the application needs to resolve a Moss endpoint, the handler has had the full app startup sequence (configuration, DI, middleware) as head start. In typical scenarios, Koi topology is already populated before the first resolution attempt.

### 15. Boot report integration

The handler's state maps to the `KoanAutoRegistrar.Describe()` boot report:

```
Koi Discovery
  Status .............. Connected
  Endpoint ............ http://localhost:5641
  Stones discovered ... 3 (moss-primary, moss-secondary, moss-edge)
  Lanterns discovered . 1 (lantern-core)
  Last topology update  2s ago
```

or:

```
Koi Discovery
  Status .............. Not Detected
  Probe target ........ http://host.docker.internal:5641
  Note ................ Koi daemon unavailable; using cached topology
```

### 16. Lantern as cross-subnet bridge (future)

When Lanterns register as `_lantern._tcp` on mDNS, the handler discovers them automatically. The resolution chain (step 3) queries the first available Lantern for full-network topology:

```
Container → Koi (HTTP) → mDNS (physical LAN)
  ├→ _moss._tcp     → local Stones (direct bind)
  └→ _lantern._tcp  → Lantern endpoint → full garden topology (all subnets)
```

This eliminates hardcoded Lantern endpoints. Cross-subnet discovery becomes: Koi finds the Lantern, Lantern knows the whole garden.

## What does NOT change

- **Moss SSE tools stream** — unchanged; remains the authority for tools/offerings/capabilities after binding
- **UDP discovery** — stays as last-resort fallback for environments without Koi
- **Persisted roster** — stays as cold-start fallback; continuously enriched by Koi events via the client
- **Explicit endpoint config** — available below Koi in the chain; also available when `KoiDiscoveryEnabled=false`
- **Container host binding** — remains as fallback for non-Koi container environments
- **All subscription, capability, and wish logic** — untouched
- **`StoneRosterStore` merge-on-write** — untouched; handler events flow through existing persistence

## Consequences

### Positive

- **Solves the container UDP gap** with live, real-time topology — not stale cache.
- **Fastest possible failover** on clean Stone shutdown — mDNS goodbye arrives in milliseconds, before TCP timeout detection.
- **Zero-cost resolution reads** — the handler pre-populates topology in the background; the resolution chain reads a struct, not a network.
- **Proactive failover** — new Stones are cached as failover candidates before they're needed.
- **Cross-subnet discovery** via Lantern bridge — no hardcoded endpoints.
- **Platform-agnostic** — same discovery path on Windows, Linux, macOS, and in containers.
- **No breaking changes** — entirely additive, feature-gated, fail-safe. Koi absence = current behavior.

### Neutral

- Two long-lived SSE connections per client (Koi events + Moss tools stream). The Koi stream is lightweight (only `_moss._tcp` events). The tradeoff is justified: it's the only way a containerized client detects topology changes independently of its bound Moss.
- `RequireHostMossWhenContainerized` becomes less critical over time — Koi provides better alternatives before the container host binding step. The flag remains for backward compatibility but could default to `false` in a future release.

### Risks

- **Koi not installed** — mitigated by fail-safe design: 500ms probe timeout, `NotDetected` state, 30s re-probe. Zero regression from current behavior.
- **Stale Koi state after long disconnection gap** — mitigated by `TopologyReset` reconciliation on reconnect.
- **Unclean Stone deaths** (no mDNS goodbye) — Koi lease expiry handles these (120s default). Comparable to current TCP keepalive detection. No regression.

## Files to create

| File | Purpose |
|---|---|
| `src/Koan.ZenGarden/Koi/KoiHandler.cs` | State machine, SSE consumer, event emitter |
| `src/Koan.ZenGarden/Koi/KoiTopologyEvent.cs` | Event record and enum |
| `src/Koan.ZenGarden/Koi/KoiTopologySnapshot.cs` | Published snapshot record |
| `src/Koan.ZenGarden/Koi/KoiHandlerState.cs` | State enum |
| `src/Koan.ZenGarden/Koi/DiscoveredStone.cs` | Stone record mapped from Koi mDNS |
| `src/Koan.ZenGarden/Koi/DiscoveredLantern.cs` | Lantern record |
| `src/Koan.ZenGarden/Koi/IKoiHandler.cs` | Public interface |
| `tests/Koan.ZenGarden.Tests/KoiHandlerTests.cs` | State machine and event tests |
| `tests/Koan.ZenGarden.Tests/KoiTopologyResolutionTests.cs` | Resolution chain integration |

## Files to modify

| File | Change |
|---|---|
| `src/Koan.ZenGarden/ZenGardenClient.cs` | Subscribe to `IKoiHandler` events; insert Koi snapshot reads in resolution chain; add `CacheStone`, `EvictStone`, `ReconcileCacheWithSnapshot`, `AddLantern`, `RemoveLantern` methods |
| `src/Koan.ZenGarden/ZenGardenOptions.cs` | Add Koi configuration properties (section 11) |
| `src/Koan.ZenGarden/Extensions/ServiceCollectionExtensions.cs` | Register `IKoiHandler` as singleton |
| `src/Koan.ZenGarden/Initialization/KoanAutoRegistrar.cs` | Add Koi handler state to boot report |
| `src/Koan.ZenGarden/Constants.cs` | Add Koi-related constants (default port, service types, timeouts) |

## Testing

### Unit tests (KoiHandlerTests)

1. **State transitions**: Verify Initializing → Connected on successful probe; Initializing → NotDetected on probe failure; Connected → Reconnecting on stream break; Reconnecting → Connected on recovery.
2. **Event emission**: Verify `KoiAvailable` fires on Connected entry; `KoiLost` fires on Reconnecting entry; `TopologyReset` fires on each Connected entry (including reconnect).
3. **StoneOnline/StoneOffline**: Feed mock SSE `resolved` and `removed` events; verify corresponding topology events fire with correct `DiscoveredStone` records.
4. **StoneChanged**: Feed two `resolved` events for same instance with different TXT; verify `StoneChanged` with `Previous` populated.
5. **Reconnect reconciliation**: Populate handler with 3 Stones, simulate disconnect, reconnect with 2 Stones — verify 1x `StoneOffline` for the missing Stone.
6. **NotDetected re-probe**: Verify handler re-probes after configured interval and transitions to Connected when Koi becomes available.
7. **Snapshot immutability**: Verify reading `CurrentSnapshot` during event processing returns consistent, non-partial state.

### Integration tests (KoiTopologyResolutionTests)

1. **Resolution chain with Koi**: Mock `IKoiHandler` with pre-populated snapshot; verify resolution chain returns a Stone from Koi snapshot at step 2.
2. **Fallthrough when Koi empty**: Mock `IKoiHandler` in `NotDetected` state; verify resolution chain falls through to existing steps.
3. **Failover on StoneOffline**: Bind to a Stone; simulate `StoneOffline` event for that Stone; verify failover initiates and binds to alternative from Koi snapshot.
4. **Cache persistence**: Verify Koi-discovered Stones are persisted to `garden-stones.json` via existing `StoneRosterStore`.

## References

- [DATA-0090 — Shared topology directory](./DATA-0090-shared-topology-directory.md)
- [DATA-0088 — Adapter auto-configuration resolver pipeline](./DATA-0088-adapter-auto-configuration-resolver-pipeline.md)
- [DATA-0089 — Zen Garden connection intent minimum URI shape](./DATA-0089-zen-garden-connection-intent-minimum-uri-shape.md)
- `zen-garden/docs/archive/proposals/windows-mdns-via-koi.md` — Koi integration in Moss (implemented)
- `zen-garden/docs/specs/discovery.md` — Discovery protocol specification
- `koi/CONTAINERS.md` — Koi container integration guide
- `src/Koan.ZenGarden/ZenGardenClient.cs` — Current resolution chain and event infrastructure
- `src/Koan.ZenGarden/Persistence/` — Stone roster persistence
