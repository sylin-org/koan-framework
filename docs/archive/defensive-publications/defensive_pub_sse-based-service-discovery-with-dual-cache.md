# Defensive Publication: SSE-Based Service Discovery with Dual-Cache, Persistent Roster, and Throttled Topology Hydration

## 1. Header

| Field | Value |
|---|---|
| **Title** | SSE-Based Service Discovery with Dual-Cache (IP + .local mDNS), Persistent Roster with Merge-on-Write, and Throttled Topology Hydration |
| **Inventor** | Leo Botinelly (Leonardo Milson Botinelly Soares) |
| **Publication Date** | 2026-03-24 |
| **Framework** | Koan Framework v0.6.3 (.NET, target net10.0) |
| **Repository** | github.com/koan-framework (private; source excerpts included below) |
| **Module** | Koan.ZenGarden -- Service Discovery and Topology Management |
| **Classification** | Software Architecture -- Distributed Systems -- Service Discovery and Resolution |
| **Status** | PUBLISHED -- This document is a defensive publication intended to constitute prior art and prevent patenting of the described techniques. |

---

## 2. Problem Statement

Service discovery in heterogeneous network environments -- where services run across bare-metal hosts, containers, and mixed LAN segments -- presents the following concrete problems that no existing system solves in combination:

1. **Real-time topology propagation without gRPC or long-polling.** Existing service discovery systems (Consul, etcd, Eureka) rely on either HTTP long-polling, gRPC streaming, or periodic heartbeat polling. Server-Sent Events (SSE) -- a standard HTTP mechanism supported natively by all HTTP clients without gRPC dependencies -- is not used by any major service discovery client for real-time topology updates. SSE provides unidirectional server-to-client streaming over plain HTTP, requiring no special transport libraries, proxying cleanly through HTTP/1.1 infrastructure, and supporting cursor-based resumption natively via the `Last-Event-ID` header.

2. **Dual addressing gap.** Services discovered via a topology API typically return IP-based endpoints (e.g., `http://192.168.1.172:7185`). On host networks, the same service may be reachable via mDNS `.local` names (e.g., `http://stone-name.local:7185`). No existing discovery client maintains both addressing variants simultaneously, forcing operators to choose between IP-based and name-based resolution. When a DHCP lease changes an IP address, the `.local` name remains stable; when mDNS is unavailable (inside containers), only the IP address works. Maintaining both variants doubles the failover surface.

3. **Persistent roster with cross-container merge safety.** Containerized services sharing a volume mount may each write discovery data concurrently. No existing system provides a persistent discovery roster that merges incoming entries with existing disk state (preserving writes from sibling containers), uses atomic rename for crash safety, and filters expired entries by TTL on every read and write.

4. **Topology hydration storms.** When multiple clients receive a heartbeat event simultaneously and all fetch the topology API, the discovery server experiences a load spike. No existing system combines SSE heartbeat events with per-client throttled hydration (time-gated to prevent storms) and fire-and-forget background hydration on bind.

5. **Multi-step fallback in container environments.** Container runtimes cannot use UDP-based discovery (mDNS, DNS-SD) because multicast does not cross Docker/Podman network boundaries. Existing discovery systems provide at most two fallback levels (cached + re-discover). No system provides an 8-step resolution chain that progressively degrades from in-memory cache through Koi topology snapshots, persisted roster re-reads, container host bindings, and optimistic cached failover, with distinct behavior for containerized vs. host runtimes.

6. **SSE event deduplication with bounded memory.** SSE reconnections may replay events. Existing SSE clients either do not deduplicate or maintain unbounded event ID sets. No service discovery client uses a rolling-window deduplication cache (HashSet + Queue) with configurable window size, ensuring bounded memory while preventing duplicate event processing.

No existing open-source service discovery system in any language provides all six capabilities in a single, composable client.

---

## 3. Prior Art Survey

### 3.1 Service Discovery Systems

| System | Transport | Addressing | Persistence | Throttling | Container Fallback |
|---|---|---|---|---|---|
| **Consul** (HashiCorp) | HTTP long-polling (`?index=N&wait=5m`) or gRPC streaming | Single endpoint per service (IP or DNS) | None client-side; server-side Raft | Blocking query semantics only | None; assumes network connectivity |
| **Eureka** (Netflix) | HTTP polling with 30s heartbeat interval | Single endpoint per instance | None client-side; in-memory server registry | Heartbeat interval only | None; designed for cloud VPCs |
| **etcd** (CNCF) | gRPC Watch API (bidirectional streaming) | Key-value based; no endpoint abstraction | Server-side persistent storage; no client roster | Watch revision-based; no client throttle | None |
| **ZooKeeper** (Apache) | Custom TCP protocol with ephemeral znodes | Znode paths; no endpoint format | Server-side; no client-side persistence | Session-based watches (one-shot) | None |
| **Kubernetes DNS** (CoreDNS/kube-dns) | DNS resolution (A/SRV records) | DNS names only; no IP caching | None; relies on DNS TTL | DNS TTL-based | Assumes in-cluster networking |
| **mDNS / Bonjour** | Multicast UDP (5353) | `.local` names only | None; ephemeral discovery | None | Does not work across container network boundaries |
| **Nacos** (Alibaba) | HTTP long-polling + gRPC streaming | Single endpoint per instance | Client-side snapshot file (full overwrite, no merge) | Push-based; no client throttle | None |

### 3.2 SSE in Service Discovery

| Aspect | Prior Art Status |
|---|---|
| SSE for service topology events | No major discovery system uses SSE. SSE is used for UI dashboards (Grafana, ArgoCD) but not as a primary service discovery transport. |
| Cursor-based SSE resumption for topology | Standard SSE `Last-Event-ID` exists but is not applied to service discovery. Cursor-based stream resumption (server-side monotonic cursor + client-side `Last-Event-ID`) for catching up on missed topology events is novel in this context. |
| SSE event deduplication with rolling window | No existing SSE client library provides bounded deduplication. Libraries like `EventSource` (browser API), `Sse.Net`, and `LavinMQ` process events as-received without dedup. |

### 3.3 Client-Side Persistence

| System | Approach | Gap |
|---|---|---|
| **Nacos** client | Snapshot file per service; full overwrite on update | No merge with concurrent writes; no atomic rename; no TTL filtering |
| **Consul Template** | Writes rendered templates to disk | Not a discovery cache; no merge semantics |
| **Eureka** client | In-memory only; no disk persistence | No offline/restart resilience |
| **DNS caching** (systemd-resolved, dnsmasq) | TTL-based in-memory cache | No disk persistence; single address per name |

### 3.4 Key Differentiators of This Invention

No surveyed system provides all of the following in combination:

- **SSE as the primary discovery transport** with cursor-based resumption and `Last-Event-ID` recovery
- **Rolling-window event deduplication** (HashSet + Queue, configurable size) for bounded-memory idempotent event processing
- **Dual-cache strategy** maintaining both IP-based endpoints and synthesized `.local` mDNS variants per discovered service, doubling the failover surface
- **Persistent roster with merge-on-write** that reads existing disk entries before writing (catching concurrent sibling writes), filters by TTL, and uses atomic temp-file + rename for crash safety
- **Throttled topology hydration** (time-gated to N-minute intervals) triggered by SSE heartbeat events, preventing hydration storms while keeping the roster warm
- **8-step container-aware resolution chain** with distinct behavior for containerized runtimes (where UDP discovery is unavailable) and host runtimes (where mDNS and UDP work)

---

## 4. Detailed Description

### 4.1 Architecture Overview

The system is implemented as a singleton client (`ZenGardenClient`, approximately 2900 lines) registered via dependency injection. It communicates with one or more Moss servers (service registry nodes called "Stones") using three channels:

```
  +-------------------+         SSE Stream          +------------------+
  |                   | <========================== |                  |
  |  ZenGardenClient  |      text/event-stream      |   Moss Server    |
  |  (singleton)      | --------------------------> |   (Stone)        |
  |                   |   GET /api/v1/garden/tools   |                  |
  |                   |                              +------------------+
  |                   |      Topology API
  |                   | --------------------------> GET /api/v1/garden/topology
  |                   |                              -> TopologyApiResponse { Data: [...] }
  |                   |
  |   +------------+  |      Persistent Roster
  |   | _stoneCache|  | <=========================> garden-stones.json
  |   | (dual-key) |  |      (merge-on-write,       (.Koan/cache/zen-garden/)
  |   +------------+  |       atomic rename)
  +-------------------+
```

### 4.2 SSE Stream Consumption

The client opens a persistent HTTP connection to the bound Moss server's SSE endpoint (`GET /api/v1/garden/tools?cursor={N}`) using `HttpCompletionOption.ResponseHeadersRead` to enable true streaming. The connection is managed by a background loop (`StreamLoopAsync`) that:

1. **Opens the stream** via `OpenStreamWithRecoveryAsync`, which attempts connection to the bound endpoint and, on failure (HTTP 4xx/5xx or connection exception), invalidates the current binding and retries with forced rediscovery.
2. **Parses SSE frames** line-by-line following the SSE specification: `event:` sets the event type, `id:` sets the event ID, `data:` accumulates payload lines, `:` lines are comments (ignored), and blank lines delimit complete events.
3. **Dispatches by event type**:
   - `tools.snapshot` -- Full state synchronization; iterates all tools and applies upserts, then processes a `replay` array of queued events.
   - `tool.upsert` -- Single tool addition or update; compares revision numbers and discards stale updates.
   - `tool.remove` -- Tool removal.
   - `tools.heartbeat` -- Triggers throttled topology hydration (see Section 4.5).
4. **Maintains cursor state** (`_cursor`, a server-side monotonic long) extracted from each event payload. On reconnection, the cursor is passed as a query parameter, enabling the server to replay only events after the cursor position.
5. **Sends `Last-Event-ID` header** on reconnection, providing a second recovery vector when the server supports event-ID-based replay.
6. **Reconnects with configurable delay** (`StreamReconnectDelaySeconds`) after any disconnection or exception.

#### SSE Frame Parsing (Source: `ConsumeStreamAsync`)

```
line starts with ':'      -> comment, skip
line starts with 'event:' -> set eventName = line[6..].Trim()
line starts with 'id:'    -> set eventId = line[3..].Trim()
line starts with 'data:'  -> append line[5..].TrimStart() to data buffer
blank line                 -> dispatch (eventName, eventId, data.ToString())
                              then reset eventName="message", eventId=null, data.Clear()
null line (EOF)            -> break; triggers reconnect loop
```

### 4.3 Rolling-Window Event Deduplication

SSE reconnections may cause the server to replay recently-sent events. The client deduplicates using a bounded rolling window:

```
RememberEventId(eventId):
    lock (_seenEventLock):
        if _seenEventIds.Contains(eventId): return false  // duplicate
        _seenEventIds.Add(eventId)
        _seenEventOrder.Enqueue(eventId)
        while _seenEventOrder.Count > max(1, DedupeWindowSize):
            old = _seenEventOrder.Dequeue()
            _seenEventIds.Remove(old)
        return true  // new event
```

- `_seenEventIds` (HashSet<string>) provides O(1) lookup.
- `_seenEventOrder` (Queue<string>) maintains insertion order for eviction.
- `DedupeWindowSize` is configurable via `ZenGardenOptions`.
- Memory is bounded: at most `DedupeWindowSize` event IDs are retained.

Events returning `false` from `RememberEventId` are silently dropped, ensuring idempotent processing regardless of server replay behavior.

### 4.4 Dual-Cache Strategy (IP + .local)

The topology API returns IP-based endpoints (e.g., `http://192.168.1.172:7185`). On host networks, the same machine is also reachable via mDNS as `http://{stone-name}.local:{port}`. The client maintains both variants:

**During topology hydration** (source: `HydrateTopologyFromMossAsync`):
```
foreach entry in topology:
    // Primary: IP-based endpoint from API
    stone = CachedMossStone { Endpoint = entry.Endpoint, ... }
    CacheStone(stone)

    // Secondary: synthesized .local mDNS variant
    localEndpoint = "http://{entry.StoneName}.local:{DefaultPort}"
    if localEndpoint != entry.Endpoint:
        localStone = stone with { Endpoint = localEndpoint }
        _stoneCache.TryAdd("{entry.StoneName}.local", localStone)
```

**During Moss topology file seeding** (source: `SeedFromMossTopologyFileAsync`):
```
foreach entry in file entries:
    stone = CachedMossStone { ... }
    _stoneCache.TryAdd(cacheKey, stone)
    // Also cache .local variant for mDNS resolution parity
    localKey = "{entry.StoneName}.local"
    localEndpoint = "http://{entry.StoneName}.local:{DefaultPort}"
    localStone = stone with { Endpoint = localEndpoint }
    _stoneCache.TryAdd(localKey, localStone)
```

**During failover resolution** (source: `ResolveBestCachedStoneForFailover`):
```
candidates = _stoneCache.Values
    .DistinctBy(s => s.Endpoint)   // IP and .local are distinct endpoints
    .OrderByDescending(s => s.LastSeenUtc)
```

The `DistinctBy(Endpoint)` operation surfaces both IP and `.local` variants as separate candidates. When an IP address becomes unreachable (DHCP lease change, network reconfiguration), the `.local` variant may still resolve via mDNS. Conversely, when mDNS is unavailable (inside containers), the IP variant works. This dual representation doubles the failover surface without requiring explicit configuration.

The `_stoneCache` (ConcurrentDictionary<string, CachedMossStone>) uses multiple index keys per stone:
- By `StoneId` (GUID, primary key when available)
- By `StoneName` (human-readable name)
- By `{StoneName}.local` (mDNS variant key)

All keys reference `CachedMossStone` records (immutable sealed records with `with`-expression copy semantics):

```csharp
internal sealed record CachedMossStone
{
    public required string Endpoint { get; init; }
    public string? StoneId { get; init; }
    public required string StoneName { get; init; }
    public string? MossVersion { get; init; }
    public string? LanternEndpoint { get; init; }
    public DateTimeOffset LastSeenUtc { get; init; }

    [JsonIgnore]
    public string CacheKey => string.IsNullOrWhiteSpace(StoneId) ? StoneName : StoneId!;
}
```

### 4.5 Throttled Topology Hydration

Topology hydration fetches the full service registry from the bound Moss server (`GET /api/v1/garden/topology`). The response is an envelope:

```json
{
  "data": [
    {
      "stone_id": "...",
      "stone_name": "kitchen",
      "endpoint": "http://192.168.1.172:7185",
      "moss_version": "0.4.2",
      "last_seen": "2026-03-24T10:00:00Z",
      "health": "healthy",
      "services": [...],
      "capabilities": [...],
      "status": "active"
    }
  ]
}
```

Hydration is triggered by two events:

1. **On `BindStone()`** -- fire-and-forget via `HydrateTopologyFireAndForget()`. When a client binds to a Moss endpoint (at any of the 8 resolution steps), it immediately kicks off a background Task to fetch the full topology, populating the cache with all known stones. This is non-blocking: the bind completes immediately, and hydration occurs asynchronously.

2. **On `tools.heartbeat` SSE event** -- throttled via `TryHydrateTopologyThrottledAsync()`:

```
TryHydrateTopologyThrottledAsync(ct):
    elapsed = DateTimeOffset.UtcNow - _lastTopologyHydration
    if elapsed < TimeSpan.FromMinutes(TopologyHydrationIntervalMinutes):
        return  // throttled: too soon since last hydration
    await HydrateTopologyFromMossAsync(ct)
```

The throttle interval (default 5 minutes via `Constants.Moss.TopologyHydrationIntervalMinutes`) ensures that even if heartbeats arrive every 30 seconds, the topology API is called at most once per interval. Since the throttle check uses `_lastTopologyHydration` (updated after successful hydration), concurrent heartbeat events within the same interval are no-ops.

After successful hydration, the roster is persisted to disk via `PersistRosterFireAndForget()`, creating a durable snapshot for cold-start scenarios.

### 4.6 Persistent Roster with Merge-on-Write and Atomic Rename

The `StoneRosterStore` persists the in-memory stone cache to a JSON file (`garden-stones.json`) located in:

- **Container**: `/app/cache/zen-garden/garden-stones.json` (when `DOTNET_RUNNING_IN_CONTAINER=true` and `/app/cache` exists)
- **Host (configured)**: `{DiscoveryCachePath}/garden-stones.json` or `{KOAN_ZENGARDEN_CACHE_PATH}/garden-stones.json`
- **Host (convention)**: `.Koan/zen-garden/garden-stones.json` relative to working directory

**Path resolution chain** (`StoneRosterPathResolver.Resolve`):

```
1. Explicit option: ZenGardenOptions.DiscoveryCachePath
2. Environment variable: KOAN_ZENGARDEN_CACHE_PATH
3. Container convention: /app/cache/zen-garden/ (when DOTNET_RUNNING_IN_CONTAINER=true
   and /app/cache volume mount exists)
4. Host convention: .Koan/zen-garden/ relative to current directory
```

**Moss topology file resolution** (`StoneRosterPathResolver.ResolveMossTopologyPath`):

```
1. Co-located with roster file (same directory)
2. GARDEN_DATA_DIR env var + /topology/garden-topology.json
3. System-wide paths:
   - Linux/macOS: /var/lib/zen-garden/topology/garden-topology.json
   - Windows: %ProgramData%\zen-garden\topology\garden-topology.json
4. Default: roster-adjacent path (file may appear later via mount injection)
```

**Load operation** (`StoneRosterStore.Load`):

```
1. Attempt legacy filename migration (one-time, race-safe across containers)
2. Read JSON file
3. Deserialize to List<CachedMossStone>
4. Filter expired entries (LastSeenUtc older than TTL, default 7 days)
5. Return valid entries
```

**Persist operation with merge-on-write** (`StoneRosterStore.Persist`):

```
await _writeLock.WaitAsync(ct)   // SemaphoreSlim(1,1) for single-writer
try:
    incoming = stones.ToList()

    // Read current disk state (sibling containers may have written since last read)
    existing = ReadFileQuietly(ct)

    // Merge: existing entries form the base; incoming entries win on conflict
    // (by CacheKey), but only if their LastSeenUtc >= existing entry's timestamp
    merged = Dictionary<string, CachedMossStone>()
    foreach stone in existing: merged[stone.CacheKey] = stone
    foreach stone in incoming:
        if not merged.ContainsKey(stone.CacheKey)
           or stone.LastSeenUtc >= merged[stone.CacheKey].LastSeenUtc:
            merged[stone.CacheKey] = stone

    toWrite = FilterExpired(merged.Values)

    // Atomic write: temp file + rename prevents partial reads
    json = JsonSerializer.Serialize(toWrite)
    File.WriteAllText(filePath + ".tmp", json)
    File.Move(filePath + ".tmp", filePath, overwrite: true)
finally:
    _writeLock.Release()
```

Key properties:
- **Merge-on-write**: Before writing, the store reads the current file. This captures entries written by sibling containers on the same shared volume since the last read. The merge uses `CacheKey` (StoneId or StoneName) as the identity key, and the entry with the later `LastSeenUtc` wins.
- **Atomic rename**: Writing to a `.tmp` file and renaming to the final path prevents readers from seeing partial JSON. `File.Move` with `overwrite: true` is an atomic operation on Linux (rename syscall) and near-atomic on Windows (NTFS MoveFileEx).
- **TTL expiry**: Expired entries are filtered on every load and persist, preventing unbounded file growth.
- **SemaphoreSlim(1,1)**: Prevents concurrent writes from the same process; cross-process safety is provided by the merge semantics (last-writer-wins by timestamp).
- **Legacy migration**: A one-time rename from an older filename is attempted on load, race-safe across containers (first mover wins; others see FileNotFoundException and continue).

### 4.7 Eight-Step Container-Aware Resolution Chain

The `EnsureBoundEndpointAsync` method implements a progressive resolution chain with distinct behavior depending on whether the runtime is containerized:

```
EnsureBoundEndpointAsync(ct, forceRediscovery):

    SeedFromPersistedRosterAsync(ct)   // one-time: load persisted entries into _stoneCache
    containerized = IsContainerizedRuntime()

    Step 1: Currently bound Stone (cached)
        if not forceRediscovery and _boundStone is not null:
            return _boundStone.Endpoint
        // Skip when force-rediscovering after a connection failure

    Step 2: Explicit endpoint / GARDEN_STONE selector
        selector = ResolvePreferredSelector()
        // Checks ZenGardenOptions.Endpoint and GARDEN_STONE environment variable
        if selector is not empty:
            selected = ResolveStoneFromSelectorAsync(selector, ct)
            if selected: return BindStone(selected).Endpoint

    Step 3: Koi topology snapshot (authoritative when connected)
        koiStone = ResolveFromKoiSnapshot()
        // Koi is a separate topology module that provides authoritative snapshots
        // via its own subscription mechanism (UDP multicast or direct connection)
        if koiStone: return BindStone(koiStone).Endpoint

    Step 4: Preferred Stone name (soft affinity)
        preferred = ResolvePreferredStoneNameAsync(ct)
        // Iterates _stoneCache.Values.DistinctBy(Endpoint) for matching name
        // Health-checks each match before returning
        if preferred: return BindStone(preferred).Endpoint

    Step 5: In-memory cache (includes seeded persisted + topology entries)
        cached = ResolveFromCacheAsync(ct)
        // Iterates cache, health-checks candidates
        if cached: return BindStone(cached).Endpoint

    Step 6: Container host binding (containerized only)
        if containerized:
            hostStone = ResolveContainerHostStoneAsync(ct)
            // Uses KOAN_ZENGARDEN_CONTAINER_HOST + optional ContainerHostPort
            if hostStone: return BindStone(hostStone).Endpoint

    Step 7: Persisted roster re-read (containerized only)
        if containerized:
            fallbackPersisted = ResolveFromPersistedRosterAsync(ct)
            // Re-reads disk file, catching sibling container writes since seeding
            if fallbackPersisted: return BindStone(fallbackPersisted).Endpoint

    Step 8: Cached failover (containerized only, optimistic without health check)
        if containerized and _stoneCache is not empty:
            failoverStone = ResolveBestCachedStoneForFailover(configuredContainerEndpoint)
            // Prefers alternatives over the known-unreachable container host
            // Ordered by LastSeenUtc descending (most recently seen first)
            // DistinctBy(Endpoint) surfaces both IP and .local variants
            if failoverStone: return BindStone(failoverStone).Endpoint

    Cold start guard (containerized only):
        if containerized and RequireHostMossWhenContainerized():
            throw InvalidOperationException
            // Signals misconfiguration: no host Moss reachable, no cached topology

    Host-only fallback: UDP discovery
        if EnableDiscovery:
            discovered = DiscoverStonesAsync(timeout, waitForAll: true, ct)
            reachable = FindFirstReachableAsync(discovered, ct)
            if reachable: return BindStone(reachable).Endpoint
            if discovered.Count > 0: return BindStone(discovered[0]).Endpoint

    Throw: no Moss endpoint resolvable
```

Key design decisions:
- **Steps 6-8 are container-specific** because UDP-based discovery cannot cross container network boundaries. The host-only path (after step 5) falls through to UDP discovery, which works on host networks.
- **Step 7 re-reads disk** even though step 5 already checked in-memory cache, because sibling containers may have persisted new entries between the initial seeding (which happens once) and this point.
- **Step 8 is optimistic**: it returns a cached endpoint without health-checking, because all health-checked options have already been exhausted. The caller's subsequent HTTP request will fail naturally, and the reconnect loop will retry.
- **`BindStone` side effects**: Every successful bind triggers `PersistRosterFireAndForget()` and `HydrateTopologyFireAndForget()`, ensuring the roster stays warm and the topology is refreshed from the newly bound Moss.
- **`forceRediscovery`**: Set to `true` on retry attempts. Skips step 1 (cached bound stone), forcing a full walk through all resolution steps.

### 4.8 Topology Enrichment from SSE Events

In addition to explicit topology hydration, the client opportunistically learns about new stones from SSE tool events. Each tool snapshot contains `StoneId`, `StoneName`, and a Moss endpoint. When a tool event references a stone not yet in the cache, the client creates a `CachedMossStone` and adds it:

```
EnrichTopologyFromSnapshot(snapshot, ct):
    if snapshot.StoneId is null or already cached: return
    mossEndpoint = extract Moss endpoint from tool's source
    if mossEndpoint is null: return
    learned = CachedMossStone { Endpoint, StoneId, StoneName, LastSeenUtc = now }
    CacheStone(learned)
```

This passive enrichment means the client learns about new Moss nodes even between hydration cycles, purely from the SSE event stream.

---

## 5. Claims (Defensive -- Not Asserted for Exclusive Rights)

The following claims describe the technical contributions of this invention. They are published defensively to establish prior art and prevent others from obtaining exclusive patent rights over these techniques.

**Claim 1 (SSE-Based Service Discovery).** A method for real-time service topology propagation comprising: (a) maintaining a persistent HTTP connection to a service registry server using the Server-Sent Events protocol; (b) processing typed events including snapshot, upsert, remove, and heartbeat; (c) maintaining a monotonic cursor for server-side stream position tracking; (d) sending the `Last-Event-ID` header on reconnection for event-ID-based replay recovery; and (e) using a configurable reconnect delay with automatic endpoint rediscovery on connection failure.

**Claim 2 (Rolling-Window Event Deduplication).** A method for bounded-memory idempotent SSE event processing comprising: (a) a HashSet for O(1) event ID lookup; (b) a Queue maintaining insertion order; (c) eviction of the oldest event ID when the set exceeds a configurable window size; and (d) returning a boolean indicating whether the event is new, enabling the caller to skip duplicate events.

**Claim 3 (Dual-Cache with IP and mDNS Variants).** A method for increasing service discovery failover surface comprising: (a) caching an IP-based endpoint received from a topology API as a primary entry; (b) synthesizing a `.local` mDNS endpoint variant from the service name and a default port; (c) storing the mDNS variant as a separate cache entry keyed by `{name}.local`; (d) presenting both variants as distinct candidates during failover resolution via `DistinctBy(Endpoint)`; whereby IP-based and name-based resolution paths are available simultaneously without operator configuration.

**Claim 4 (Merge-on-Write Persistent Roster).** A method for crash-safe, concurrent-write-tolerant service roster persistence comprising: (a) reading the existing file before writing, capturing entries from sibling processes on shared volumes; (b) merging incoming entries with existing entries using a service identity key, where the entry with the later timestamp wins; (c) filtering expired entries by TTL on every write; and (d) writing to a temporary file and atomically renaming to the final path, preventing partial reads.

**Claim 5 (Throttled Topology Hydration).** A method for preventing topology API storms comprising: (a) triggering hydration on SSE heartbeat events; (b) comparing the time elapsed since the last successful hydration against a configurable interval; (c) skipping hydration when the interval has not elapsed; (d) triggering fire-and-forget background hydration on every bind operation; and (e) persisting the refreshed roster to disk after successful hydration.

**Claim 6 (Eight-Step Container-Aware Resolution).** A method for progressive service endpoint resolution in heterogeneous environments comprising eight ordered steps: (1) currently bound endpoint from cache; (2) explicitly configured endpoint or environment variable selector; (3) authoritative topology snapshot from a separate topology module; (4) preferred service name with health-check validation; (5) in-memory cache with health-check validation; (6) container host binding via environment variable; (7) persisted roster re-read to catch concurrent sibling writes; (8) optimistic cached failover without health check, selecting candidates ordered by recency and preferring alternatives over the known-unreachable endpoint; where steps 6 through 8 are exclusive to containerized runtimes, and host runtimes fall through to UDP-based multicast discovery.

**Claim 7 (Passive Topology Enrichment from SSE Events).** A method for learning service topology from tool-level events comprising: (a) extracting service node identity (StoneId, StoneName) and source endpoint from each SSE tool event; (b) checking whether the node is already cached; and (c) creating a new cache entry for previously unknown nodes, enabling topology expansion without explicit hydration calls.

**Claim 8 (Combination).** The combination of Claims 1 through 7 operating as a unified service discovery client wherein: SSE provides the real-time event transport; the dual-cache and persistent roster provide resilient endpoint storage; throttled hydration prevents server overload; the 8-step resolution chain provides progressive fallback; and passive enrichment supplements active hydration.

---

## 6. Advantages Over Prior Art

| Dimension | Prior Art (Consul, Eureka, etcd, K8s DNS, mDNS, Nacos) | This Invention |
|---|---|---|
| **Transport** | gRPC streaming, HTTP long-polling, DNS, or multicast UDP | SSE over plain HTTP/1.1; no gRPC dependency; proxies through any HTTP infrastructure; cursor + Last-Event-ID dual recovery |
| **Addressing** | Single endpoint format per service instance (IP or DNS, never both) | Dual-cache: IP-based primary + synthesized `.local` mDNS secondary, doubling failover surface |
| **Event deduplication** | Not addressed client-side (rely on server idempotency) | Bounded rolling-window HashSet+Queue with configurable size |
| **Client persistence** | None (Consul, Eureka, etcd) or full-overwrite snapshot (Nacos) | Merge-on-write with timestamp-wins conflict resolution, TTL expiry, and atomic rename |
| **Concurrent writers** | Not addressed (single-process assumption) | Merge-on-write reads disk before write, preserving sibling container entries on shared volumes |
| **Topology refresh** | Continuous polling (Eureka 30s), blocking query (Consul), or watch (etcd) | Throttled hydration triggered by SSE heartbeat; at most once per N minutes regardless of heartbeat frequency |
| **Container support** | Assumes in-cluster networking or requires sidecar proxy | 8-step resolution chain with container-specific steps (host binding, roster re-read, optimistic failover) and cold-start guard |
| **Fallback depth** | Typically 1-2 levels (cached + retry) | 8 levels: bound -> explicit -> Koi -> preferred -> cache -> container host -> persisted re-read -> optimistic failover |
| **Passive learning** | Topology known only through dedicated discovery queries | SSE tool events passively enrich topology as tools are announced |
| **Memory bound** | Unbounded watch sets (etcd) or fixed heartbeat cycles (Eureka) | Configurable dedup window; TTL-based cache eviction; distinct-by deduplication on failover |

---

## 7. Antagonist Analysis

This section applies adversarial scrutiny to identify weaknesses, limitations, and potential challenges to the novelty of the described techniques.

### 7.1 Novelty Challenges

**Challenge**: "SSE is a standard protocol. Using it for service discovery is an obvious application."

**Response**: While SSE is a standard transport, no major service discovery system has adopted it despite two decades of availability (SSE was first implemented in browsers circa 2006). The specific combination of SSE with cursor-based topology resumption, heartbeat-triggered throttled hydration, and rolling-window deduplication has not been documented or implemented in any surveyed system. The non-obvious aspect is not SSE itself but the integration of SSE with the other five mechanisms described herein.

**Challenge**: "Dual-caching IP and .local is just caching two DNS records for the same host."

**Response**: The `.local` endpoint is not looked up via DNS -- it is synthesized from the service name and a default port. No DNS query is performed during cache population. The insight is that maintaining both addressing schemes simultaneously in the same failover pool -- where they are treated as distinct candidates ordered by recency -- provides a failover path that survives both DHCP changes (IP invalidation) and mDNS unavailability (container environments), without requiring the operator to configure either. No surveyed system performs this synthesis.

**Challenge**: "Merge-on-write is just read-modify-write, a standard database pattern."

**Response**: The novelty is applying read-modify-write semantics to a flat JSON file shared across multiple containers via a Docker volume mount, with the specific combination of: (a) using the service identity as the merge key, (b) timestamp-based conflict resolution (not last-writer-wins-all), (c) TTL filtering on every write to prevent unbounded growth, and (d) atomic rename for crash safety. Standard service discovery clients do not persist to shared files at all.

### 7.2 Limitations

1. **Single-Moss SSE connection**: The client connects to one Moss server at a time. If that server becomes unavailable, there is a window (reconnect delay) during which no new events are received. Multi-server fan-out is not implemented.

2. **Clock skew sensitivity**: The merge-on-write strategy uses `LastSeenUtc` for conflict resolution. Significant clock skew between sibling containers could cause a stale entry to win over a fresh one. NTP synchronization is assumed but not enforced.

3. **Atomic rename on NFS/CIFS**: The atomic rename guarantee (`File.Move` with `overwrite: true`) is strong on local filesystems (ext4, NTFS, APFS) and on container overlay filesystems (overlayfs). On network filesystems (NFS, CIFS/SMB), rename atomicity may not be guaranteed, potentially allowing partial reads.

4. **No encryption or authentication on SSE stream**: The SSE connection is plain HTTP (or HTTPS if configured). There is no mutual TLS or token-based authentication specific to the SSE stream. This is a deployment concern, not an architectural limitation.

5. **Throttle granularity**: The hydration throttle is per-client, not coordinated across clients. In a fleet of N clients, up to N topology API requests can occur within the throttle interval. For large fleets, a jitter mechanism or server-side rate limiting would be advisable.

6. **Dedup window size trade-off**: A small dedup window may allow duplicate processing after a long disconnection followed by aggressive replay. A large window increases memory usage linearly. The configurable window size defers this trade-off to the operator.

### 7.3 Potential Extensions (Not Claimed)

- Coordinated hydration throttling across clients (e.g., via a distributed lock or server-side "next hydration" hint in heartbeat payloads)
- Multi-Moss SSE fan-out with conflict resolution across streams
- Encrypted roster files for sensitive deployments
- Weighted failover scoring incorporating latency measurements alongside recency

---

## Legal Notice

This document is a **defensive publication** intended to establish prior art under 35 U.S.C. section 102(a)(1) and equivalent international provisions. It is published to prevent any party -- including the inventor -- from obtaining exclusive patent rights over the techniques described herein.

The described techniques are implemented in the Koan Framework, an open-architecture .NET framework. Publication of this document does not constitute a grant of license to any specific software, nor does it waive any copyright protections on the source code itself.

All source code excerpts are provided solely for the purpose of establishing technical disclosure sufficient to constitute prior art. The excerpts are representative of the implementation as of the publication date and may evolve in subsequent framework versions.
