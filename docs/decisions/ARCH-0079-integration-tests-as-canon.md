# ARCH-0079: Integration Tests as Canon for Adapters

**Status**: Accepted
**Date**: 2026-05-15
**Deciders**: Enterprise Architect
**Scope**: `Koan.*.Adapter.*`, `Koan.*.Connector.*`, `Koan.Cache.Coherence.*`, pillar cores
**Related**: ARCH-0075 (cache pillar architecture), ARCH-0076 (decorator order canon), ARCH-0078 (SWR opt-in)

---

## Context

The cache pillar work landed on `feat/koan-cache-pillar` shipped with a real integration-test surface (M4 cornerstone, SQLite persistence round-trip, middleware TestServer, full-DI boot smoke). Across that work, **four latent systemic production bugs** were caught — none of them by the 162 pre-existing unit specs:

| Bug | Surfaced by | Pillars affected | Severity |
|---|---|---|---|
| `TryAddEnumerable<TService>(factory)` indistinguishable-descriptor throw | SQLite integration test | Cache (5 sites), Recipe (2 latent), Web.Extensions (1 latent) | Would crash `AddKoanCache + any adapter` at boot |
| `CacheWriteOptions.GetEffectiveL1Ttl` not clamped to `AbsoluteTtl` | Redis SWR integration test | Cache | L1 outlives L2 for any L2 TTL < 60s |
| Cross-pillar `IConnectionMultiplexer` registration race | Bootstrap smoke | Data.Connector.Redis + Cache.Adapter.Redis | Silent fallback to localhost:6379 when both adapters referenced with mismatched config keys |
| `StartupProbeService` treats one adapter's unavailability as fatal to the entire host | Attempt to write per-pillar boot smokes in a project that transitively references the Redis adapter | Any Koan app referencing any infra adapter | App fails to start ALL pillars when ONE transitively-referenced infra adapter is unavailable, even if no pillar actually uses it |

Each bug was structurally invisible to unit tests because units hand-roll their DI graphs and skip the production bootstrap path. Each bug required exactly **one** integration test to surface.

This is not anecdotal. It is a **category claim** with four data points across one branch: integration tests reveal bug classes that unit tests cannot.

### Forces

1. **Unit tests with fakes are insufficient.** They prove component behavior in isolation but cannot prove composition. Every cross-cutting concern (DI registration, topology resolution, shared transports, hosted-service lifecycles) is invisible to them.
2. **The framework markets "Reference = Intent" as its flagship promise.** That promise is testable only through the real reflective `IKoanAutoRegistrar` discovery path — i.e., through `services.AddKoan()` against real packages. Without integration tests for that path, the promise is unsubstantiated.
3. **Adapters share resources.** Multiple adapters often consume the same transport (Redis multiplexer, RabbitMQ connection, HTTP client). Without canonical ownership, they race or duplicate. Integration tests are the only way to detect this in the worst-case composition.
4. **Fixture duplication is a smell signal.** Five connector test suites (`SqlServer`, `PGVector`, `OpenSearch`, `ElasticSearch`, `Couchbase`) hand-rolled their own container fixtures outside the existing `Koan.Testing` shared library. Each rediscovered the same Docker-detection and connection-wait logic. The pattern wants centralization.
5. **Retroactive coverage is large but bounded.** 38 adapter/connector/coherence packages exist. Of those: ~20 are straightforward Testcontainers (data, messaging, storage, secrets), ~8 need mock-server work (OAuth providers, hosted-AI APIs), ~3 are awkward (orchestration adapters testing orchestration runtimes — exempted), ~1 is trivially `TestServer` (Swagger).

---

## Decision

Integration tests become **framework canon** for adapters and pillar cores, applied in two graduated tiers.

### Mandatory before next release

Every release MUST include:

1. **A boot-smoke test for every pillar core.** Uses `services.AddKoan()` to go through the real reflective discovery path. Asserts the pillar's primary public surface (its main `I*Client` or equivalent) resolves and the pillar's hosted services start without throwing. Approximate scope: 12 pillar cores (Cache, Data.Core, Storage, Web, AI.Core, Messaging.Core, Mcp, Media.Core, Jobs, Rag, Auth, Canon).
2. **The shared `KoanIntegrationHost` helper** in `Koan.Testing`. Replaces the per-suite `RedisCacheNode`-style ad-hoc host builders. One canonical entry-point for "build a real `IHost`, start hosted services, dispose cleanly, with config seeded from a dictionary."
3. **The 5 existing custom-fixture suites refactored** under `KoanIntegrationHost` (`SqlServer`, `PGVector`, `OpenSearch`, `ElasticSearch`, `Couchbase`). Removes fixture duplication.
4. **Documentation** in `tests/README.md` describing the canon pattern for new adapter authors.

### Tracked backlog (target: 2 release cycles from acceptance)

5. **Testcontainers integration test for each containerizable adapter** — every adapter not exempted (see exemptions below) ships at least one integration test exercising its primary contract (CRUD for data adapters, publish/subscribe for messaging, etc.) against a real container.
6. **Mock-server integration test for OAuth/hosted-AI adapters** — adapters that can't be containerized (Discord, Google, Microsoft, OIDC, HuggingFace, etc.) must have integration tests using `WireMock.Net` or equivalent against a recorded contract.

### Documented exemptions

7. **Orchestration adapters** (`Docker`, `Podman`, `Compose` renderers) are exempted from Testcontainers — the adapter under test IS the container runtime. Each must instead ship an alternative integration test that exercises orchestration generation against a fixture-based filesystem (e.g., asserting a generated compose file's shape).

Other exemptions require an ADR amendment to add. The bar is intentionally high — exemptions accumulate cost.

---

## The shared-resource lesson

The cross-pillar `IConnectionMultiplexer` race surfaced in Phase 2 of this branch is not an isolated bug — it is a **missing abstraction**. When multiple adapters consume the same transport (Redis multiplexer, RabbitMQ connection, HTTP client factory, etc.), the framework must designate **one canonical owner** of the shared resource, with other adapters declared as consumers.

The canon mandates that **integration tests must exercise the worst-case adapter composition** for each pillar — explicitly including any cross-pillar transport sharing. The bootstrap smoke in this branch composing `Koan.Cache.Adapter.Redis + Koan.Data.Connector.Redis` is the template: deliberately reference adapters that would conflict, and prove they cooperate.

The specific abstraction — how shared transports are declared, owned, and consumed — is deferred to **ARCH-0080: Shared transport ownership** (forthcoming). ARCH-0080 will codify the pattern (`Koan.Data.Connector.Redis` owns `IConnectionMultiplexer`; `Koan.Cache.Adapter.Redis` consumes; etc.) and apply it across the framework. This ADR captures the lesson that motivated ARCH-0080 — integration tests are how we will continue to find shared-resource issues until ARCH-0080 lands and codifies the pattern globally.

---

## Tooling: `Koan.Testing.KoanIntegrationHost`

A single helper, lifted from the `RedisCacheNode` pattern introduced on this branch. Sketch:

```csharp
public static class KoanIntegrationHost
{
    public static Builder Configure() => new();

    public sealed class Builder
    {
        public Builder WithSetting(string key, string value);
        public Builder WithConfiguration(IConfiguration config);
        public Builder ConfigureServices(Action<IServiceCollection> configure);
        public Task<IHost> StartAsync(CancellationToken ct = default);
    }
}
```

Internals: builds a real `HostBuilder`, seeds `IConfiguration` from a dictionary, invokes `services.AddKoan()`, calls `host.StartAsync()`, and returns the `IHost`. Test code disposes via `await using`.

Acceptance: all four cache integration suites + the bootstrap smoke compile and pass against the lifted helper with strictly less per-test setup code than they have today.

---

## Consequences

### Positive

- **Eliminates an entire bug class going forward.** Composition / shared-resource / boot-time bugs become catchable. The branch-as-evidence shows this is real value — three production bugs found in one feature branch, zero by unit tests.
- **Makes "Reference = Intent" provable.** The headline framework promise stops being marketing and becomes a tested invariant per pillar.
- **Centralizes fixture infrastructure.** The 5 custom fixtures move under `Koan.Testing`; subsequent adapter PRs adopt the canon near-free.
- **Surfaces shared-resource conflicts as composition mandates.** Integration tests compose competing adapters by design, forcing ARCH-0080's pattern to materialize through actual constraint pressure.

### Negative

- **Adapter PRs become slightly larger.** Every new adapter ships ≥1 integration test from day one. Estimate: 2–3 hr per adapter beyond unit work.
- **Retroactive backfill cost is ~70–130 hr.** Tracked, not release-blocking. The release-blocking minimum (boot smokes + helper + 5 refactors) is ~20 hr.
- **Test runtime grows.** Container startup adds seconds per test class. Mitigation: `Koan.Testing`'s existing fixture-collection pattern already shares containers across classes within a collection.

### Neutral

- **CI must have Docker.** Already true for the 6 existing suites that use container fixtures. The canon makes it universal for adapter test runs.

---

## Implementation order (this branch and follow-ups)

1. **This branch, after ADR sign-off**:
   - Lift `KoanIntegrationHost` into `Koan.Testing` (~2–3 hr).
   - Refactor cache integration suites (3 projects) + bootstrap smoke under the helper (~1 hr).
   - Refactor 5 custom-fixture suites (SqlServer, PGVector, OpenSearch, ElasticSearch, Couchbase) under the helper (~2.5 hr).
   - Write boot-smoke specs for the remaining pillar cores beyond Cache (~12 hr).
   - Update docs: `tests/README.md` + `koan-caching` skill + CLAUDE.md (~1 hr).

2. **Follow-up branches**:
   - `arch/0080-shared-transport-ownership` — codify the shared-transport pattern; refactor Redis multiplexer ownership; cache adapter becomes consumer; remove the workaround comment from `CachePillarBootstrapSpec`.
   - `fix/startup-probe-degrade-not-throw` — `StartupProbeService` should mark probes failed (degraded health) rather than abort host startup. Currently any infra adapter that's transitively referenced but unavailable kills the host even when no pillar uses it. Surfaced while attempting per-pillar boot smokes.
   - Per-pillar bootstrap test projects (`Koan.Tests.Integration.Bootstrap.Data`, `.Storage`, `.Messaging`, etc.) — required because the cross-project coupling otherwise forces every pillar smoke to satisfy every transitively-referenced adapter's runtime requirements.

3. **Tracked backlog (next 2 release cycles)**:
   - Testcontainers integration tests for the remaining ~20 containerizable adapters.
   - Mock-server integration tests for the remaining ~8 hosted-API adapters.
   - Orchestration-adapter integration tests via filesystem fixtures (no containers).

---

## Notes for reviewers

- The evidence section of this ADR cites three named bugs from `feat/koan-cache-pillar`. Each is a tracked commit on this branch. The argument is "look at what one branch already proved" rather than "trust that this is worth it."
- The exemptions list is intentionally narrow (only orchestration adapters). The aim is to keep the canon's force ironclad while honest about technical infeasibility in the recursive case.
- ARCH-0080 (shared transport ownership) is **not** a prerequisite for this ADR. The canon's "compose worst-case adapters in integration tests" mandate is what would have caught the Redis multiplexer race even without ARCH-0080's abstraction. ARCH-0080 will be cleaner, but the canon already addresses the bug class.
