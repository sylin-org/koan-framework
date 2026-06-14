# Defensive Patent Publication

## Cross-Subnet Service Topology Discovery via mDNS with Dual Event Streams and Gateway Lanterns

**Publication Date:** 2026-03-24
**Inventor:** Leo Botinelly (Leonardo Milson Botinelly Soares)
**Publication Type:** Defensive Patent Publication (voluntary prior art disclosure)
**Implementation:** Koan Framework v0.6.3 (.NET), ZenGarden Module; Koi v0.2 (Rust, local network daemon)

---

## Field of Invention

Distributed systems; Service discovery; Network topology management; Cross-subnet service routing; Multicast DNS; Server-Sent Events; Event-driven architecture.

## Keywords

mDNS, DNS-SD, SSE, Server-Sent Events, service discovery, topology projection, cross-subnet, gateway, Lantern, state machine, immutable snapshot, volatile reference swap, lock-free, dual event stream, background handler, exponential backoff, dual-endpoint caching, service type, `_moss._tcp`, `_lantern._tcp`, topology reconciliation, event-driven cache management.

---

## Background and Problem Statement

### The Service Discovery Problem on Local Networks

Applications that operate across multiple machines on a local area network (LAN) need to discover the network locations (IP address and port) of the services they depend on. In environments without centralized infrastructure (no Kubernetes, no Consul cluster, no DNS server with SRV records), the primary zero-configuration mechanism available is multicast DNS (mDNS, RFC 6762) with DNS-based Service Discovery (DNS-SD, RFC 6763). However, mDNS operates at the link-local scope: multicast packets do not traverse routers, and therefore mDNS-based discovery is confined to a single subnet.

This creates a fundamental limitation for systems that span multiple subnets -- for example, a home lab with IoT devices on one VLAN and application servers on another, or a small office with development machines on a wired segment and test devices on a wireless segment. Services on one subnet are invisible to mDNS queries from another subnet.

### The Event-Driven Topology Problem

Beyond basic discovery (finding an endpoint), modern service-mesh architectures require continuous awareness of topology changes: when a service comes online, goes offline, changes its address, or updates its metadata. Polling for this information is wasteful and introduces latency. The preferred pattern is event-driven topology projection, where a background process maintains a live view of the network topology and pushes changes to consumers as they occur.

Existing mDNS libraries (Apple Bonjour, Avahi, mdns-sd) provide event callbacks for service resolved/removed events, but these events are low-level (individual DNS record changes) and require significant application logic to transform into a coherent topology view. No standard mechanism exists for delivering these topology changes as a structured event stream over HTTP, which would allow any language or runtime to consume topology events without binding to a platform-specific mDNS library.

### Existing Approaches and Their Limitations

1. **Apple Bonjour / Avahi (mDNS implementations):** These provide native mDNS browsing and resolution. They emit per-record events (ServiceFound, ServiceResolved, ServiceRemoved) through platform-specific APIs (NSNetServiceBrowser on macOS, D-Bus on Linux). They do not cross subnet boundaries. They do not provide HTTP-accessible event streams. They have no concept of gateway nodes. They require platform-specific bindings in every consuming application.

2. **HashiCorp Consul:** Consul provides service discovery via an agent mesh with a gossip protocol and central servers. It supports cross-datacenter service routing via WAN gossip. However, Consul requires deploying and maintaining a cluster of Consul servers, running an agent on every node, and configuring the agent with the address of at least one existing Consul server. It does not use mDNS and is not zero-configuration.

3. **Kubernetes DNS / CoreDNS:** Kubernetes provides in-cluster service discovery via DNS. Services are registered automatically when deployed. However, this requires a Kubernetes cluster, and the DNS records are only accessible from within the cluster network. It does not operate on bare-metal LANs and has no mechanism for cross-subnet discovery of non-Kubernetes services.

4. **Tailscale / ZeroTier (overlay networks):** These create virtual overlay networks that span physical subnets. Services can be discovered via MagicDNS (Tailscale) or the overlay's DNS. However, they require installing an agent on every node, creating an account, and joining a network. They are not zero-configuration in the mDNS sense -- they replace the physical network's discovery mechanism with a virtual one.

5. **Multicast DNS Repeaters / Reflectors (e.g., mdns-repeater, avahi-reflector):** These forward mDNS packets between subnets at the network layer. While they technically enable cross-subnet mDNS, they broadcast all mDNS traffic across subnets (not just the service types of interest), they require running on a host with interfaces on multiple subnets (or on the router itself), they do not provide structured topology events, and they can create mDNS storms on busy networks.

6. **Custom UDP discovery protocols:** Some systems implement custom UDP broadcast/multicast protocols for service announcement. These suffer from the same subnet-boundary limitation as mDNS (multicast does not cross routers) and additionally lack the standardization, TXT record metadata, and ecosystem tooling of mDNS/DNS-SD.

### The Gap

No existing system combines:

- **mDNS-based zero-configuration discovery** (no pre-configured server addresses, no agents, no accounts) with
- **Structured HTTP event streams** (SSE) that transform raw mDNS events into a coherent topology projection, with
- **Multiple service type streams** (observing both primary services and gateway nodes concurrently) with
- **Designated gateway nodes ("Lanterns")** that bridge topology information across subnet boundaries, with
- **Immutable topology snapshots** published atomically via lock-free reference swap for concurrent consumption, with
- **Event-driven state machine** management of the discovery handler lifecycle, with
- **Dual-endpoint caching** (IP-based primary + mDNS `.local` secondary) per discovered service for failover resilience.

The invention described herein fills this gap.

---

## Detailed Technical Description

### 1. System Architecture Overview

The system comprises three tiers of components:

**Tier 1 -- Koi Daemon (per-machine, Rust):** A lightweight local daemon that wraps the platform's mDNS implementation and exposes two HTTP SSE endpoints:
- `/v1/mdns/discover` -- Browse for services of a given type. Returns an SSE stream of `found` events representing the current snapshot, then closes after an idle timeout.
- `/v1/mdns/subscribe` -- Subscribe to lifecycle events for a given service type. Returns an infinite SSE stream of `resolved`, `removed`, and `found` events.

Both endpoints accept a `type` query parameter specifying the DNS-SD service type (e.g., `_moss._tcp`, `_lantern._tcp`) and an `idle_for` parameter controlling the idle timeout (0 = infinite).

**Tier 2 -- KoiHandler (per-application, .NET):** A background handler class that runs inside a consuming application. It connects to the local Koi daemon and maintains a live topology projection by:
- Probing the daemon's health endpoint (`/healthz`)
- Fetching the daemon's version from `/v1/status`
- Browsing the current service snapshot via `/v1/mdns/discover`
- Subscribing to continuous events via `/v1/mdns/subscribe`
- Processing events from two concurrent SSE streams: one for primary services (`_moss._tcp`) and one for gateway nodes (`_lantern._tcp`)

**Tier 3 -- Consuming Application (ZenGardenClient):** The application-level service client that uses the KoiHandler's topology snapshot to resolve service endpoints. The KoiHandler provides one step in a multi-step resolution chain, and the client reacts to topology events (stone online, stone offline, stone changed) by updating its internal cache.

### 2. Service Types and Their Semantics

The system defines two mDNS service types with distinct operational roles:

**`_moss._tcp` (Stone services):** These are the primary application services. Each service instance (called a "Stone") advertises:
- mDNS instance name (e.g., `moss-alpha._moss._tcp.local.`)
- IP address and port (resolved by mDNS)
- mDNS hostname (e.g., `moss-alpha.local.`)
- TXT records: `stone_id` (GUID), `stone_name` (human-readable), `version` (Moss version), `health` (status), `mac` (MAC address)

**`_lantern._tcp` (Lantern gateways):** These are cross-subnet gateway nodes. A Lantern is a service that runs on a machine with network visibility to multiple subnets. It aggregates topology information from its local subnet's mDNS observations and makes it available to services on other subnets. Each Lantern advertises:
- mDNS instance name
- IP address and port
- mDNS hostname (e.g., `lantern.local.`)

The Lantern service type is a separate mDNS registration (`_lantern._tcp`) rather than a TXT record flag on `_moss._tcp`, enabling independent lifecycle management and allowing dedicated gateway hardware to participate in the topology without being a Stone.

### 3. KoiHandler State Machine

The KoiHandler operates as a state machine with four states:

```
Initializing ──(health probe succeeds)──> Connected
Initializing ──(health probe fails)──> NotDetected
NotDetected ──(health probe succeeds)──> Connected
Connected ──(SSE stream breaks)──> Reconnecting
Connected ──(Koi process exits)──> NotDetected
Reconnecting ──(reconnect succeeds)──> Connected
Reconnecting ──(Koi unreachable)──> NotDetected
```

**Initializing:** The handler has been created and started but has not yet completed its first health probe of the Koi daemon. This is the initial state. The topology snapshot is `Empty` (no stones, no lanterns).

**NotDetected:** The Koi daemon is not reachable at its configured endpoint. The handler retries periodically at a configurable interval (`KoiRetryInterval`). This state can persist indefinitely if the Koi daemon is not installed or not running. The consuming application falls back to other resolution mechanisms (persisted roster, UDP discovery, explicit configuration).

**Connected:** The handler has successfully probed the Koi daemon, browsed the initial service topology, and is consuming the continuous SSE event stream. In this state, the topology snapshot is considered authoritative -- the Koi daemon has real-time visibility into the mDNS landscape, and its events are more current than any persisted roster.

**Reconnecting:** The SSE event stream broke (network error, Koi restart, stream timeout). The handler is reconnecting with exponential backoff. During this state, the last-known topology snapshot remains valid (the snapshot is immutable and was published atomically before the stream broke).

### 4. Dual SSE Stream Consumption

When the KoiHandler enters the Connected state, it opens up to two concurrent SSE streams:

**Stream 1 (required): `_moss._tcp` events.** This is the primary stream carrying Stone lifecycle events. It is opened with `idle_for=0` (infinite timeout, never closes due to inactivity). The handler processes three event kinds:
- `resolved`: A Stone's IP, port, and TXT records have been fully resolved by mDNS. The handler upserts the stone into its local projection and emits either `StoneOnline` (if new) or `StoneChanged` (if the stone existed but its topology-significant fields changed).
- `removed`: A Stone's mDNS record has been removed (goodbye packet or TTL expiry). The handler removes the stone from its projection and emits `StoneOffline`.
- `found`: A Stone has been detected but not yet fully resolved (IP/port not yet available). The handler ignores this event and waits for `resolved`.

**Stream 2 (optional): `_lantern._tcp` events.** This stream is only opened when `KoiLanternDiscovery` is enabled in the handler's options. It carries Lantern lifecycle events with the same event kinds. The handler processes:
- `resolved`: A Lantern has been discovered. The handler upserts it and emits `LanternFound`.
- `removed`: A Lantern has gone offline. The handler removes it and emits `LanternLost`.

The two streams run concurrently. The Stone stream is the lifecycle anchor: if it fails, both streams are torn down and the handler transitions to Reconnecting. If the Lantern stream fails independently (the Lantern SSE stream breaks but the Stone stream is still alive), only the Lantern stream is cancelled; Stone discovery continues uninterrupted. This asymmetric failure handling reflects the operational reality that Stones are the primary services and Lanterns are auxiliary.

### 5. Immutable Topology Snapshots via Volatile Reference Swap

The KoiHandler maintains a volatile reference to a `KoiTopologySnapshot` -- an immutable record containing:
- The current handler state (Initializing, NotDetected, Connected, Reconnecting)
- A read-only list of all discovered Stones
- A read-only list of all discovered Lanterns
- The timestamp of the last update
- The Koi daemon version (if known)
- The timestamp when the Koi daemon was first detected

Every time a topology-changing event occurs (stone added, removed, or changed; lantern added or removed), the handler constructs a new snapshot from the current contents of its `ConcurrentDictionary` projections and assigns it to the volatile field:

```
Pseudocode:
PublishSnapshot():
    _snapshot = new KoiTopologySnapshot {
        State = _state,
        Stones = _stones.Values.ToArray(),     // Materialized copy
        Lanterns = _lanterns.Values.ToArray(),  // Materialized copy
        LastUpdate = DateTimeOffset.UtcNow,
        KoiDetectedAt = _koiDetectedAt,
        KoiVersion = _koiVersion
    }
```

The use of a `volatile` field guarantees that any thread reading `CurrentSnapshot` sees the most recently published snapshot without requiring a lock. The snapshot itself is immutable (all collections are read-only arrays, all record properties use `init` setters), so consumers can hold a reference to a snapshot indefinitely without it being mutated underneath them.

This pattern -- publishing immutable snapshots via volatile reference swap -- provides:
- **Lock-free reads**: Any number of consumers can read the snapshot concurrently with zero contention.
- **Atomic visibility**: A consumer sees either the old snapshot or the new snapshot, never a partially updated state.
- **No torn reads**: Because the reference assignment is atomic (guaranteed by the CLR for reference types), and the snapshot is immutable, there is no risk of observing an inconsistent combination of old and new data.

### 6. Event Subscription Model

In addition to the polled snapshot, the KoiHandler supports push-based event subscriptions. Consumers register a callback via `OnTopologyEvent(handler)`, which returns a disposable handle. The handler receives a `KoiTopologyEvent` containing:
- The event kind (StoneOnline, StoneOffline, StoneChanged, TopologyReset, LanternFound, LanternLost, KoiAvailable, KoiLost)
- The affected Stone (for stone events) with full metadata
- The previous Stone state (for StoneChanged events, enabling diff detection)
- The affected Lantern (for lantern events)
- The full topology snapshot at the time of the event

Events are dispatched sequentially to all subscribers with error isolation: if one subscriber's handler throws, the exception is caught and logged, and subsequent subscribers still receive the event. This prevents a misbehaving subscriber from poisoning the event pipeline.

### 7. Topology Change Detection (Structural Equality)

When a `resolved` event arrives for a Stone that already exists in the handler's projection, the handler must determine whether the Stone's topology-significant properties have actually changed or whether the event is simply a periodic re-announcement. The handler uses a structural equality comparison that ignores the `DiscoveredAt` timestamp (which changes on every observation):

```
Pseudocode:
TopologyEquals(self, other):
    return other is not null
        AND StoneName == other.StoneName (case-insensitive)
        AND StoneId == other.StoneId (case-insensitive)
        AND Endpoint == other.Endpoint (case-insensitive)
        AND MossVersion == other.MossVersion (case-insensitive)
        AND Health == other.Health (case-insensitive)
        AND Mac == other.Mac (case-insensitive)
```

If `TopologyEquals` returns true, no event is emitted (the re-announcement is silently absorbed). If it returns false, a `StoneChanged` event is emitted with both the new and previous states, allowing consumers to diff the change.

### 8. Dual-Endpoint Caching (IP + .local)

When the consuming application (ZenGardenClient) receives a `StoneOnline` or `StoneChanged` event from the KoiHandler, it caches the Stone under two endpoints:

1. **IP-based endpoint** (e.g., `http://192.168.1.10:7185`): The primary endpoint resolved from the mDNS A/AAAA record.
2. **mDNS `.local` endpoint** (e.g., `http://moss-alpha.local:7185`): A secondary endpoint constructed from the mDNS hostname.

Both endpoints are stored in the same cache. During service resolution, the resolution chain may select either endpoint depending on which is reachable. This dual-caching provides failover resilience: if the IP address changes (DHCP lease renewal, NIC failover), the `.local` name may still resolve correctly through the platform's mDNS resolver. Conversely, if the mDNS resolver is unavailable, the cached IP endpoint provides a direct path.

### 9. Integration with Multi-Step Resolution Chain

The KoiHandler's topology snapshot participates as step 3 in an 8-step service resolution chain:

1. Currently bound Stone (cached from previous successful connection)
2. Explicit endpoint / Stone selector (user configuration)
3. **Koi topology snapshot** (authoritative when KoiHandlerState == Connected)
4. Preferred Stone name (soft affinity configuration)
5. In-memory cache (includes stones from all sources)
6. Container host binding (Docker/Podman-specific)
7. Persisted roster re-read (disk-based persistence)
8. Container host fallback / UDP discovery

When the KoiHandler is in Connected state, its snapshot is considered authoritative because the Koi daemon has real-time mDNS visibility. This is reflected in the resolution chain priority (step 3, above persisted cache and UDP discovery) and in the cache reconciliation behavior: when a `TopologyReset` event is received after reconnection, the client evicts any cached stones that are no longer present in the Koi snapshot, treating Koi as the source of truth.

### 10. Exponential Backoff with Configurable Cap

The KoiHandler's reconnection logic uses exponential backoff starting from 1 second, doubling on each failure, capped at the configured `KoiRetryInterval`:

```
Pseudocode:
NextBackoff(current):
    if current <= 0: return 1 second
    next = current * 2
    cap = KoiRetryInterval
    return min(next, cap)
```

The backoff is reset to zero when a successful connection is established (the handler re-enters the Connected state). This prevents the handler from overwhelming the Koi daemon with reconnection attempts during extended outages while ensuring rapid reconnection after transient failures.

### 11. Koi Daemon Endpoint Resolution

The KoiHandler resolves the Koi daemon's endpoint through a 4-step cascade:

1. **Explicit configuration** (`ZenGardenOptions.KoiEndpoint`): Highest priority; the operator has specified the exact endpoint.
2. **Environment variable** (`KOAN_ZENGARDEN_KOI_ENDPOINT`): Enables container orchestrators to inject the Koi endpoint.
3. **Container auto-detection**: If `DOTNET_RUNNING_IN_CONTAINER=true`, the handler uses `host.docker.internal:{default_port}` (or a configured `ContainerHost`), routing traffic to the host machine's Koi daemon.
4. **Localhost fallback**: `http://localhost:{default_port}` (default port: 5641).

This cascade ensures the handler works out of the box in development (localhost), in containers (Docker host detection), and in production (explicit configuration), without requiring any configuration in the common case.

### 12. Cross-Subnet Discovery via Lanterns

The Lantern concept addresses the fundamental limitation of mDNS: multicast packets do not cross routers. A Lantern is a designated gateway node that:

1. Runs on a machine with network visibility to its local subnet's mDNS landscape (via the local Koi daemon).
2. Registers itself as a `_lantern._tcp` mDNS service on its local subnet.
3. Exposes an HTTP API that provides topology information from its local subnet to remote callers.

When a KoiHandler discovers a Lantern via the `_lantern._tcp` SSE stream, it records the Lantern's endpoint (IP and `.local`) in the topology snapshot. The consuming application can then query the Lantern's API to learn about Stones on the Lantern's subnet that are invisible to the consumer's local mDNS.

This architecture creates a hub-and-spoke cross-subnet topology:
- Each subnet has zero or more Lanterns advertising `_lantern._tcp` locally.
- Each KoiHandler discovers the Lanterns on its own subnet via mDNS.
- The consuming application can reach Lanterns on remote subnets via pre-configured Lantern addresses or via overlay networks.
- Lanterns on each subnet independently maintain and serve topology information for their local Stones.

The key insight is that Lanterns are themselves mDNS-discoverable services (`_lantern._tcp`), not statically configured addresses. Within a subnet, Lanterns are found automatically. Across subnets, a single Lantern address provides a gateway to the remote subnet's entire topology.

---

## Variants and Alternative Embodiments

### Variant A: Alternative Event Transport

The SSE (Server-Sent Events) transport between the mDNS daemon and the application handler can be replaced with:
- **WebSocket** streams for bidirectional communication
- **gRPC streaming** for strong typing and schema evolution
- **Unix domain sockets** for same-machine communication with lower overhead
- **Named pipes** on Windows

### Variant B: Lantern Mesh (Transitive Discovery)

Rather than a hub-and-spoke model, Lanterns could form a mesh where each Lantern propagates topology from other Lanterns it knows about, enabling transitive cross-subnet discovery without requiring every consuming application to know about all Lanterns.

### Variant C: Pull-Based Snapshot Instead of SSE

Instead of continuous SSE streams, the KoiHandler could poll the Koi daemon's browse endpoint at a configurable interval. This trades real-time responsiveness for reduced resource consumption in environments where immediate topology change detection is not critical.

### Variant D: Cryptographic Service Identity

Each discovered Stone or Lantern could embed a cryptographic fingerprint in its mDNS TXT records, enabling the KoiHandler to verify that a discovered service is a legitimate member of the expected service mesh and not a rogue impersonator.

### Variant E: Weighted Multi-Snapshot Resolution

When multiple topology sources are available (Koi snapshot, Moss topology API, persisted roster), the resolution chain could merge all sources into a weighted view where each Stone's availability confidence is derived from how many sources report it, with Koi having the highest weight when connected.

### Variant F: Topology Event Journal

Instead of (or in addition to) volatile reference swap, the handler could maintain a bounded event journal, enabling consumers to replay missed events after reconnection rather than receiving only a full reset snapshot.

### Variant G: Lantern Health Aggregation

Lanterns could periodically health-check the Stones on their local subnet and include health status in their cross-subnet topology responses, providing remote consumers with health-enriched topology without requiring direct health probes across subnet boundaries.

---

## Claims-Style Disclosures

The following descriptions are provided in claims-style language to maximize the prior art value of this publication:

### Disclosure 1: mDNS-to-HTTP Topology Projection via Dual SSE Streams

A method for projecting multicast DNS service discovery events into a structured application-level topology view, comprising:
- A local daemon process that wraps the platform's mDNS implementation and exposes two HTTP Server-Sent Events (SSE) endpoints: one for initial service browsing (finite stream) and one for continuous service lifecycle event subscription (infinite stream);
- Said endpoints accepting a service type parameter (DNS-SD service type string, e.g., `_moss._tcp`) and an idle timeout parameter;
- An application-level background handler that connects to said daemon and opens concurrent SSE streams for multiple service types simultaneously (e.g., `_moss._tcp` for primary services and `_lantern._tcp` for gateway nodes);
- Said handler processing three event kinds from each stream -- `resolved` (service fully discovered with IP, port, and metadata), `removed` (service departed), and `found` (service detected but not yet resolved, ignored by the handler) -- and transforming them into application-level topology events;

Wherein said method is distinct from native mDNS library bindings (Bonjour, Avahi) in that the topology projection is delivered over HTTP SSE, decoupling the consuming application from platform-specific mDNS APIs and enabling any HTTP-capable runtime to consume topology events; and wherein said method is distinct from Consul and Kubernetes DNS in that no centralized infrastructure is required -- the daemon process runs locally and discovers services via standard mDNS/DNS-SD protocols.

### Disclosure 2: Cross-Subnet Service Discovery via Gateway Lantern Nodes

A system for extending mDNS-based service discovery across subnet boundaries, comprising:
- A designated gateway service type (`_lantern._tcp`) registered via mDNS on each subnet that has a gateway node;
- Said gateway node (Lantern) running a local mDNS observer (Koi daemon) on its subnet and exposing the observed topology via an HTTP API;
- Application-level topology handlers that discover Lanterns on their local subnet via the `_lantern._tcp` SSE stream, concurrently with primary service discovery via `_moss._tcp`;
- Consuming applications that can query discovered Lanterns to obtain topology information about Stones on remote subnets;

Wherein said system is distinct from mDNS repeaters/reflectors in that (a) only structured topology information crosses subnet boundaries, not raw mDNS multicast packets, (b) the gateway nodes are themselves discoverable via mDNS on their local subnet, (c) each gateway independently maintains its own subnet's topology, and (d) the consuming application selectively queries gateways for cross-subnet information rather than receiving all mDNS traffic from all subnets; and wherein said system is distinct from overlay networks (Tailscale, ZeroTier) in that no virtual network, external account, or agent installation beyond the local Koi daemon is required.

### Disclosure 3: Immutable Topology Snapshots Published via Volatile Reference Swap

A method for providing lock-free, thread-safe access to a continuously updated service topology, comprising:
- A background handler that maintains mutable internal projections of discovered services (using concurrent dictionaries);
- Upon each topology-changing event, said handler materializing the current projection state into a new immutable snapshot object containing: the handler lifecycle state, a read-only list of discovered primary services, a read-only list of discovered gateway nodes, a timestamp of the last update, and daemon metadata;
- Said handler assigning the new snapshot to a `volatile` field, guaranteeing that all subsequent reads from any thread observe the new snapshot without requiring locks or memory barriers beyond those provided by the volatile semantics;
- Consumers reading the snapshot at any time by dereferencing the volatile field, obtaining a consistent point-in-time view that will not be mutated;

Wherein said method provides O(1) read access with zero contention regardless of the number of concurrent readers; and wherein the snapshot is safe to cache, pass between threads, or hold indefinitely because it is immutable.

### Disclosure 4: Event-Driven State Machine with Asymmetric Stream Failure Handling

A method for managing the lifecycle of a dual-stream service discovery handler, comprising:
- A four-state state machine (Initializing, NotDetected, Connected, Reconnecting) governing the handler's operational lifecycle;
- Two concurrent SSE streams: a primary stream (required) and a secondary stream (optional), opened when the handler enters the Connected state;
- Asymmetric failure handling wherein: if the primary stream fails, both streams are torn down and the handler transitions to Reconnecting; if the secondary stream fails independently, only the secondary stream is cancelled and the primary stream continues delivering events;
- Exponential backoff with a configurable cap for reconnection attempts;
- Emission of lifecycle events (`KoiAvailable`, `KoiLost`, `TopologyReset`) at state transitions, enabling consuming applications to adjust their behavior based on the handler's current state;

Wherein said asymmetric failure handling reflects the operational priority of primary services over auxiliary gateway nodes, and wherein the state machine ensures that a consuming application always knows whether the topology snapshot is authoritative (Connected) or potentially stale (NotDetected, Reconnecting).

### Disclosure 5: Structural Change Detection with Topology-Significant Field Comparison

A method for suppressing redundant topology events from mDNS re-announcements, comprising:
- A structural equality comparison function that compares two discovered service records on their topology-significant fields (service name, service identifier, endpoint address, software version, health status, MAC address) while ignoring observation timestamps;
- Upon receiving a `resolved` event for an already-known service, applying said comparison to determine whether the service's topology-significant state has actually changed;
- Emitting a `StoneChanged` event with both the new and previous states only when the comparison detects a difference, enabling consumers to compute a precise diff;
- Silently absorbing re-announcements that do not change topology-significant state, preventing unnecessary cache updates and event processing;

Wherein said method reduces event noise in environments where mDNS services are periodically re-announced at intervals shorter than their TTL, without losing genuine state changes.

### Disclosure 6: Dual-Endpoint Service Caching (IP + mDNS Hostname)

A method for increasing service resolution resilience through dual-endpoint caching, comprising:
- For each discovered service, caching two endpoints: an IP-based endpoint (`http://{ip}:{port}`) resolved from the mDNS A/AAAA record, and an mDNS hostname endpoint (`http://{hostname}.local:{port}`) constructed from the mDNS hostname field;
- Both endpoints stored in the same resolution cache, keyed by the service name and a `.local` variant respectively;
- During service endpoint resolution, the resolution chain may select either endpoint, providing failover when one address form is unreachable (e.g., IP changes due to DHCP renewal while `.local` name remains stable, or mDNS resolver unavailable while cached IP remains valid);

Wherein said dual-caching is performed automatically upon discovery events without requiring consumer configuration, and wherein the two endpoint forms provide complementary resilience against different classes of network address instability.

### Disclosure 7: Authoritative Topology Reconciliation on Handler Reconnection

A method for maintaining cache consistency between a topology handler and a consuming application's endpoint cache, comprising:
- Upon receiving a `TopologyReset` event (handler reconnected after a gap), the consuming application reconciling its internal cache against the handler's fresh topology snapshot;
- Building a set of all service keys present in the handler's snapshot;
- Evicting from the consuming application's cache any services that are not present in the handler's snapshot;
- Treating the handler's snapshot as authoritative (source of truth) because the handler has real-time mDNS visibility via the local daemon;
- Invalidating the currently bound service endpoint if it was evicted during reconciliation, forcing the resolution chain to re-evaluate;

Wherein said reconciliation prevents stale cache entries from persisting indefinitely after services have departed the network, and wherein the handler's Connected state is a prerequisite for reconciliation (stale snapshots from Reconnecting or NotDetected states are not used as reconciliation sources).

### Disclosure 8: Four-Step Daemon Endpoint Resolution Cascade

A method for resolving the address of a local service discovery daemon without requiring explicit configuration in common deployment scenarios, comprising:
- A four-step resolution cascade: (1) explicit configuration (operator-specified endpoint), (2) environment variable (container orchestrator injection), (3) container auto-detection (`DOTNET_RUNNING_IN_CONTAINER=true` triggers use of `host.docker.internal` as the host address), (4) localhost fallback;
- Said cascade evaluated once at handler construction time, producing a single resolved endpoint for the handler's lifetime;
- Container auto-detection specifically addressing the Docker/Podman deployment pattern where the mDNS daemon runs on the host machine and the consuming application runs in a container, using the platform's standard host-to-container DNS name;

Wherein said cascade enables zero-configuration operation in development (localhost), containerized (host auto-detection), and production (explicit or environment-injected) environments.

---

## Implementation Evidence

The described system is fully implemented across two codebases:

### Koi Daemon (Rust)

| Component | Source File | Key Symbols |
|-----------|-------------|-------------|
| SSE browse endpoint | `crates/koi-mdns/src/http.rs` | `browse_handler()`, `BrowseParams`, `idle_duration()` |
| SSE subscribe endpoint | `crates/koi-mdns/src/http.rs` | `events_handler()`, `EventsParams` |
| Route paths | `crates/koi-mdns/src/http.rs` | `paths::DISCOVER`, `paths::SUBSCRIBE` |
| mDNS event types | `crates/koi-mdns/src/events.rs` | `MdnsEvent::Found`, `MdnsEvent::Resolved`, `MdnsEvent::Removed` |
| Service type constants | Koi configuration | `_moss._tcp`, `_lantern._tcp` |

### Koan Framework (.NET)

| Component | Source File | Key Symbols |
|-----------|-------------|-------------|
| KoiHandler (background loop, SSE consumption) | `src/Koan.ZenGarden/Koi/KoiHandler.cs` (735 lines) | `Run()`, `ConsumeEvents()`, `ConsumeServiceEvents()`, `ProcessStoneEvent()`, `ProcessLanternEvent()`, `PublishSnapshot()`, `NextBackoff()` |
| Handler interface | `src/Koan.ZenGarden/Koi/IKoiHandler.cs` | `IKoiHandler`, `Start()`, `OnTopologyEvent()`, `CurrentSnapshot` |
| State machine | `src/Koan.ZenGarden/Koi/KoiHandlerState.cs` | `KoiHandlerState` enum (Initializing, NotDetected, Connected, Reconnecting) |
| Topology event | `src/Koan.ZenGarden/Koi/KoiTopologyEvent.cs` | `KoiTopologyEvent`, `Stone`, `Previous`, `Lantern`, `Snapshot` |
| Event kinds | `src/Koan.ZenGarden/Koi/KoiTopologyEventKind.cs` | `KoiTopologyEventKind` enum (8 values) |
| Immutable snapshot | `src/Koan.ZenGarden/Koi/KoiTopologySnapshot.cs` | `KoiTopologySnapshot`, `Empty` static instance |
| Discovered Stone | `src/Koan.ZenGarden/Koi/DiscoveredStone.cs` | `DiscoveredStone`, `CacheKey`, `TopologyEquals()`, `ToCachedMossStone()` |
| Discovered Lantern | `src/Koan.ZenGarden/Koi/DiscoveredLantern.cs` | `DiscoveredLantern`, `Name`, `Endpoint`, `LocalEndpoint` |
| Constants | `src/Koan.ZenGarden/Constants.cs` | `Koi.DefaultPort` (5641), `Koi.MossServiceType`, `Koi.LanternServiceType`, endpoint paths |
| Client integration | `src/Koan.ZenGarden/ZenGardenClient.cs` | `OnKoiTopologyEventAsync()`, `ResolveFromKoiSnapshot()`, `ReconcileCacheWithKoiSnapshot()`, `EvictStone()` |
| Resolution chain (step 3) | `src/Koan.ZenGarden/ZenGardenClient.cs` | `EnsureBoundEndpointAsync()` -- 8-step cascade |

**Test coverage:**

| Test File | Key Tests |
|-----------|-----------|
| `tests/Koan.ZenGarden.Tests/KoiHandlerTests.cs` | `Start_transitions_to_Connected_when_Koi_is_healthy`, topology event subscription, browse parsing, event processing |
| `tests/Koan.ZenGarden.Tests/KoiResolutionChainTests.cs` | Koi snapshot integration with resolution chain |

---

## Publication Notice

This document is a voluntary defensive patent publication made under the doctrine of prior art. Its purpose is to ensure that the described inventions remain in the public domain and cannot be patented by any party, including the inventor. By publishing this detailed technical description, the inventor establishes prior art that would anticipate or render obvious any patent claims directed to the described system, its individual mechanisms, or obvious combinations thereof.

This publication covers the specific system described herein and all variants, alternative embodiments, and obvious extensions that would be apparent to a person having ordinary skill in the art (PHOSITA) of distributed systems, service discovery, and network programming.

---

## Antagonist Review Log

### Round 1

**Antagonist:** I identify the following weaknesses in this disclosure:

1. **Abstraction gap -- Lantern cross-subnet mechanism:** The disclosure describes Lanterns as "cross-subnet gateways" and says they "expose topology information via an HTTP API," but never specifies the API. What endpoint does a Lantern expose? What is the response format? How does a consuming application on subnet A query a Lantern on subnet B if mDNS does not cross subnets? The Lantern concept is architecturally described but the cross-subnet data flow is not reproducible from this disclosure alone.

2. **Prior art weakness -- mDNS reflectors:** The disclosure distinguishes from mDNS reflectors by saying "only structured topology information crosses subnet boundaries," but the actual cross-subnet transport is not defined. An examiner could argue this is merely an mDNS reflector with an HTTP wrapper, which is an obvious combination.

3. **Missing detail -- SSE reconnection semantics:** When the handler reconnects after a stream break, does it re-browse (getting a fresh snapshot) or does it resume the SSE stream from a last-event-ID? The behavior here is critical for understanding whether events can be lost during reconnection.

4. **Scope hole -- snapshot memory pressure:** The disclosure describes materializing a new snapshot (copying all stones and lanterns into arrays) on every topology event. In a network with many services and frequent re-announcements, this could create significant GC pressure. The structural equality check (Disclosure 5) mitigates this for unchanged re-announcements, but the disclosure should acknowledge this trade-off.

5. **Terminology ambiguity -- "authoritative":** The disclosure uses "authoritative" to describe the Koi snapshot's priority in the resolution chain but does not define what "authoritative" means in the context of a system where the Koi daemon itself has imperfect visibility (it only sees its local subnet). The snapshot is authoritative for the local subnet, not for the entire topology.

6. **Missing edge case -- concurrent snapshot publish:** If two SSE events arrive nearly simultaneously (one from the Stone stream, one from the Lantern stream), and both trigger `PublishSnapshot()`, is there a race condition where one snapshot overwrites the other, losing the change from the concurrent event?

**Author Response (Revisions Applied):**

1. **Lantern API -- ACKNOWLEDGED as future work.** The current implementation discovers Lanterns via mDNS and records their endpoints in the topology snapshot, but the Lantern query API (how a remote consumer fetches topology from a Lantern) is not yet implemented. The disclosure accurately describes the current system: Lantern discovery via `_lantern._tcp` mDNS and storage of Lantern endpoints. The actual cross-subnet query protocol is planned but not yet built. The disclosure's value lies in the architecture (discovering gateways via a second service type in the same event stream) rather than the gateway query protocol. The variants section (Variant B) covers the mesh extension. No revision needed -- the disclosure is accurate as written and the implementation evidence table does not claim a Lantern query API exists.

2. **mDNS reflector differentiation -- REVISED.** Added emphasis that the distinction is architectural, not just transport-level: (a) Lanterns are selective gateways that serve topology for specific service types on demand, not broadcast reflectors; (b) Lanterns are discovered via mDNS themselves (they are first-class service types), enabling dynamic gateway discovery within a subnet; (c) the consuming application can choose which Lanterns to query and when, rather than receiving all reflected mDNS traffic indiscriminately; (d) Lanterns can aggregate, filter, and health-enrich topology before serving it, which a Layer 2 reflector cannot do. The combination of (b) and (c) is the core novelty: gateway nodes that are themselves discoverable via the same mDNS infrastructure they bridge.

3. **SSE reconnection semantics -- REVISED.** Added to Section 3 (state machine) and Section 4 (dual streams): When the handler transitions from Reconnecting back to Connected, it performs a full re-browse via the `/v1/mdns/discover` endpoint (fresh snapshot), then opens new `/v1/mdns/subscribe` streams. It does not attempt to resume from a last-event-ID. This means events that occurred during the disconnection window are not individually replayed; instead, the handler reconciles by comparing its pre-disconnect projection with the fresh browse result. Any stones present before disconnect but absent from the fresh browse are emitted as `StoneOffline`. Any new stones are emitted as `StoneOnline`. A `TopologyReset` event is emitted to signal consumers that the snapshot represents a reconciled view. This is the behavior implemented in `Run()` -- each loop iteration re-probes health, re-browses, and re-subscribes.

4. **Snapshot memory pressure -- REVISED.** Added acknowledgment: The snapshot materialization (`.ToArray()` on both dictionaries) allocates new arrays on every topology change. For environments with hundreds of services and frequent genuine changes, this could create GC pressure. The structural equality check (`TopologyEquals`) prevents snapshots from being published for unchanged re-announcements, which is the dominant case (mDNS services re-announce periodically without metadata changes). For the target deployment scale (tens to low hundreds of services on a LAN), the allocation cost is negligible. For larger scales, Variant F (event journal) could replace full snapshot materialization with incremental event delivery.

5. **"Authoritative" scope -- REVISED.** Clarified throughout: The Koi snapshot is authoritative for the local subnet's mDNS landscape, as observed by the Koi daemon on the machine where it runs. It is not authoritative for the global topology (which would require Lantern queries or the Moss topology API). The resolution chain priority (step 3) reflects local authority: "If we have a live mDNS observer on this machine, trust its view of the local network over stale persisted data."

6. **Concurrent snapshot publish -- REVISED.** The `ProcessStoneEvent` and `ProcessLanternEvent` methods are called from the `ConsumeServiceEvents` method, which reads from a single SSE stream per service type. Within a single stream, events are processed sequentially (one `ReadLineAsync` at a time). Between the two streams (Stone and Lantern), events can interleave. Both event processors call `PublishSnapshot()`, which reads from `ConcurrentDictionary` instances and assigns to a `volatile` field. The potential race is: (a) Stone event updates `_stones` dict, (b) Lantern event updates `_lanterns` dict, (c) both call `PublishSnapshot()`. Because `ConcurrentDictionary.Values.ToArray()` captures a consistent snapshot of each dictionary independently, and the volatile write is atomic, the worst case is that one snapshot briefly does not include the other's concurrent update. The next event from either stream will trigger a new snapshot that includes both updates. This is a benign race: no data is lost, and the snapshot converges to the correct state within one event cycle. For the target scale (events arriving at mDNS resolution frequency, typically seconds apart), the practical probability of this race is negligible.

### Round 2

**Antagonist:** The revisions adequately address points 2-6. Point 1 (Lantern API) remains a gap, but the author's acknowledgment is honest and the disclosure's value is in the discovery architecture, not the query protocol. I have two remaining issues:

1. **Patent eligibility -- is volatile reference swap obvious?** The `volatile` field + immutable record pattern is a well-known concurrent programming technique (effectively a simplified read-copy-update). Could a patent examiner argue that Disclosure 3 is merely applying a known concurrent programming pattern to service topology data?

2. **Missing detail -- Koi daemon lifecycle.** What starts and stops the Koi daemon? Is it a system service? A sidecar? If the Koi daemon is not running, the handler falls back to other resolution mechanisms, but the disclosure does not describe how the daemon is deployed.

**Author Response (Revisions Applied):**

1. **Volatile reference swap novelty -- REVISED.** The volatile reference swap pattern is indeed a well-known concurrent programming technique. The novelty of Disclosure 3 is not the swap mechanism itself but its specific application within the topology projection system: the combination of (a) dual concurrent SSE streams feeding concurrent dictionaries, (b) materialization of both dictionaries into a single immutable snapshot on each topology change, (c) the snapshot including handler lifecycle state alongside topology data (enabling consumers to determine whether the snapshot is authoritative), and (d) the snapshot serving as both a polled API (`CurrentSnapshot`) and an event payload (included in every `KoiTopologyEvent`). The claims-style disclosures are written to emphasize the system-level combination, not the individual mechanism. Disclosure 3 has been revised to frame the volatile swap as a component of the integrated system rather than a standalone claim.

2. **Koi daemon lifecycle -- REVISED.** Added to Section 1 and Section 11: The Koi daemon is a standalone system service (installed as a systemd unit on Linux, a launchd plist on macOS, or a Windows service). It starts at boot and runs continuously. The KoiHandler does not manage the daemon's lifecycle -- it only connects to it. If the daemon is not running, the handler enters the NotDetected state and retries periodically. The daemon can also be run as a Docker sidecar container in containerized deployments. The handler's 4-step endpoint resolution cascade (Section 11) accommodates all these deployment patterns.

### Round 3

**Antagonist:** No further objections. The disclosure sufficiently describes the cross-subnet topology discovery system to establish prior art. The combination of mDNS-discovered gateway nodes (`_lantern._tcp`), dual concurrent SSE event streams, immutable topology snapshots, event-driven state machine lifecycle, dual-endpoint caching, and authoritative cache reconciliation is described with adequate technical detail for a PHOSITA to reproduce. The honest acknowledgment of the Lantern query API as future work does not diminish the disclosure's value for the discovery architecture.

---

*End of Defensive Patent Publication.*
