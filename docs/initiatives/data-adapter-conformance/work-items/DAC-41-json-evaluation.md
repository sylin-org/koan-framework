---
type: SPEC
domain: data
title: "DAC-41 Rebuild and Certify the JSON Entity Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: in-progress
last_updated: 2026-07-29
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-29
  status: behavior-green-strict-deferred
  scope: empty-root JSON Entity adapter rebuild, complete real-file conformance, and sibling regressions
---

# DAC-41 — Rebuild and certify the JSON Entity adapter

| Field | Value |
|---|---|
| Phase / kind | fleet / break-and-rebuild-certification |
| Depends on | DAC-40 |
| Primer scope | Entity Core, Source Core, KeyValue family, G-09 |
| Production writes | authorized after the exploration gate recorded below |
| Owner | Adapter(JSON); KeyValue family semantics remain in Data Core |

## Meaningful outcome

Koan has an inspectable, zero-server local persistence floor: an application saves ordinary Entities, receives detached
values, survives a new host, and gets precise correction when its directory, source posture, file shape, size, or
requested guarantee cannot be honored.

## Exploration gate

**Task:** Replace the JSON Entity adapter from an empty implementation root, retaining stable file/configuration facts
and the proven KeyValue family contract, then certify its complete real-file surface.

**Application intent:** Persist ordinary Entities in inspectable local JSON files without running a database server.

**Public expression:** Reference the connector, use ordinary Koan bootstrap, and use Entity verbs:

```powershell
dotnet add package Sylin.Koan.Data.Connector.Json
```

```csharp
builder.Services.AddKoan();

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

var saved = await new Todo { Title = "Ship" }.Save();
var same = await Todo.Get(saved.Id);
```

The managed default is the platform-neutral `data` directory. An application chooses another physical root with the
one JSON-specific setting:

```json
{
  "Koan": {
    "Data": {
      "Json": { "DirectoryPath": "state" },
      "Sources": {
        "Archive": {
          "Adapter": "json",
          "json": { "DirectoryPath": "archive" }
        }
      }
    }
  }
}
```

**Guarantee/correction:** A successful write serializes a detached record, writes one complete bounded aggregate beside
the target, replaces the target, and only then publishes the new host snapshot. Reads materialize fresh values. The
same canonical file has one in-process cache and gate even when multiple source paths spell it differently. Managed
read/write use may create its directory; read-only and External paths never provision. External requires the addressed
Entity file to exist. Corrupt, duplicate-identity, oversized, wrong-type, or unavailable files fail correctively and
never become an empty or successful in-memory state. Required atomic batches and provider-bounded streams reject before
partial work.

**Complete intent surface:** Package reference, `AddKoan()`, and an Entity are sufficient. `DirectoryPath`, named Source,
access, lifecycle, and partition context appear only when the application makes those standard placement/policy
decisions. There is no connector registration, repository, lock, cache, reset API, serializer, DTO, or recovery object.

**Public concepts:** `json` expresses deliberate file placement; `DirectoryPath` expresses the physical root. Entity,
Source, access, lifecycle, partition, query, and batch retain their provider-neutral meanings. The 1,024-file host bound
and 64 MiB aggregate-file ceiling are corrective limits, not application configuration branches; larger stores select
a database adapter.

**Docs read:**

- `docs/architecture/principles.md` — Entity is the public center, adapters own mechanics only, host composition is
  finite, and hot operations consume frozen decisions.
- `docs/architecture/data-adapter-development-primer.md` — real snapshots, four source postures, no activation I/O,
  bounded work, fail-closed declines, and an empty implementation root are acceptance law.
- `docs/decisions/DATA-0107-provider-bounded-entity-streams.md` — file-resident scans do not earn provider-bounded
  streaming.
- JSON README/TECHNICAL — the shortest local-file expression, corruption/source limits, and detached commit semantics
  are useful public facts; implementation names and passing counts are not design authority.

**Code read:**

- `KeyValueStore<TEntity,TKey>` and `KvRecord<TEntity>` own query/filter/sort/page, managed guards, batch grammar,
  instructions, and G-09; JSON supplies only file primitives and earned capability facts.
- Current factory/options/health/module establish the `json` automatic floor and `DirectoryPath` resolution, but default
  path setup is duplicated and uses a Windows-specific spelling.
- Current repository reveals the provider mechanics and failure modes: per-repository caches/gates do not coordinate
  canonical path aliases; capacity admission races and failed writes can retain gates; construction performs storage
  I/O; bounded scans call `ConcurrentDictionary.Values` before `Take`; every write reserializes every unchanged Entity;
  and file input has no byte ceiling.
- The 28-case real-file suite proves CRUD, restart, corruption, failed persistence, polymorphism, source policy, health,
  routing, isolation, instructions, and batch decline, but two persistence cases construct adapter internals directly
  and no case proves canonical-path coordination, exact host capacity, file-size rejection, or native one-replacement
  bulk claims.
- DAC-40 InMemory demonstrates the exact finite host-registry admission pattern. Its memory/storage mechanics are not a
  JSON implementation template.

**Existing constants/options/contracts:**

- Already exists: provider identity `json`, priority `0`, automatic-floor election, `DirectoryPath`, source-setting
  resolution, `JsonDataOptions`, naming, `DataSourcePlan`, `EntityJsonSerialization`, `ManagedFieldJsonInjector`,
  `KeyValueStore`, and selection-aware health.
- Needs to be created: canonical host file registry, immutable file snapshot/record form, cross-platform default path,
  exact file-count and byte bounds, and black-box proofs for the newly frozen guarantees.
- No public request/response/DTO or new provider option is required.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| compact stable facts and hard safety bounds | `Infrastructure/Constants.cs` | One owner for provider/config/file identities and finite limits. |
| one typed directory option | `JsonDataOptions.cs` | Preserve the sole JSON-specific application decision. |
| immutable source route resolver | `Runtime/JsonRoute.cs` | Give factory and health one canonical directory/policy interpretation. |
| bounded canonical file registry | `Runtime/JsonFileRegistry.cs` | Own one lock-free warm slot and one write gate per actual host file across source aliases. |
| thin file repository | `Runtime/JsonRepository.cs` | Implement KeyValue primitives, detached record encoding, bounded load, and replace-then-publish. |
| executable claims | `Runtime/JsonFeatures.cs` | Keep factory and repository truth identical, including one-replacement bulk. |
| plan/policy factory | `JsonAdapterFactory.cs` | Reject physical maps, resolve one route, and create the repository without filesystem I/O. |
| selection-aware health | `JsonHealthContributor.cs` | Probe only participating sources and preserve non-creating constrained posture. |
| compact module | `Initialization/JsonModule.cs` | Register options, one host registry, factory, and health contributor. |
| black-box acceptance cases | connector JSON suite | Prove storage only through `AddKoan()` and Entity behavior; delete direct repository construction. |
| instruction-first docs | JSON `README.md` and `TECHNICAL.md` | State the result, guarantees, finite limits, and unsupported boundaries. |

**Coalescence:** The closest semantic pattern is `KeyValueStore<TEntity,TKey>`; keep it as the capability-family owner.
JSON needs one adapter-specific `JsonFileRegistry` because physical-path identity, cached bytes, and file-write exclusion
have identical host lifetime across every source/repository that addresses that file. Repository-local registries are
too narrow and Data Core is too wide: no other KeyValue adapter has filesystem alias or replacement semantics. Rebuild
all JSON execution files; delete the root-level repository, options configurator, implementation-coupled tests, duplicate
default-setting interpretation, and every per-repository file/gate dictionary. Leave one route, one file-state owner,
and one repository path.

**Ergonomics:** Human code remains package + `AddKoan()` + Entity, with one discoverable `DirectoryPath` only when local
placement matters. IntelliSense exposes one options property and no infrastructure types. File limits fail with a
correction to select a database connector rather than adding tuning branches to a deliberately small local floor.

**Constraints satisfied:**

- Entity-first access; no public repository, cache, codec, DTO, or provider operation API.
- No HTTP surface.
- Stable identifiers and hard bounds live in constants; the only application tuning decision remains typed options.
- No filesystem I/O during package availability, factory composition, or pure diagnostics.
- Host state is finite and DI-owned; no process-static cache or unbounded gate map.
- `AllStream`/`QueryStream` remain corrective declines under DATA-0107.
- No compatibility bridge, hidden fallback, alternate file path, placeholder, or commented scaffold.
- README/TECHNICAL, generated product truth, and initiative ledgers move with executable behavior.

**Risks:** File replacement is same-directory and preserves the last complete target across ordinary serialization,
cancellation, and rename failures; it is not a power-loss/fsync, multi-process transaction, backup, or recovery promise.
The cache deliberately does not observe external concurrent edits. Lexical canonicalization coordinates relative/absolute
aliases but cannot identify every filesystem symlink alias. The fixed byte ceiling must reject before reading or writing
an oversized aggregate.

Standing user authorization permits implementation after this gate. It does not relax empty-root, real-file,
conformance, or strict-packet requirements.

## Execute

1. Replace every JSON production execution file in one atomic slice with only the owners above.
2. Convert persistence safety cases to real host/Entity behavior and add RED cases for canonical-path coordination,
   bulk claims, pure construction, exact capacity, duplicate identities, and file-size rejection.
3. Run the JSON ledger, InMemory/Data Core/Redis family regressions, restart/concurrency/handle soak, full solution build,
   package/product/docs gates, and initiative consistency checks.
4. Keep strict packet absence visible; do not create a local certification substitute.

## Replacement result

The root repository and options configurator are deleted. The replacement has one immutable source route, one bounded
host-owned canonical-file registry, one KeyValue repository bridge, one capability authority, one factory, one health
contributor, and one module. No old execution path or compatibility bridge remains.

Warm reads materialize only the selected stored record. Writes serialize changed records once, reuse unchanged record
strings, persist one complete bounded candidate, and publish only after replacement. Canonical source aliases and
concurrent writers coordinate through one file slot. The exact 1,024-file admission boundary and 64 MiB read/write
ceiling fail correctively.

The real-file suite is green at 34/34 with zero skipped cases. It proves public-host persistence safety, cold
polymorphism, duplicate and oversized input rejection, canonical alias coherence, concurrent writes, exact capacity,
bulk claims, policy, health, routing, managed isolation, and unsupported-boundary declines. Strict packet generation
remains deferred because the shared versioned packet is absent; no local substitute is counted as certification.

Shared regressions are green at Data Core 471/471, InMemory 56/56, and live pinned Redis 17/17. The changed JSON
project restores from the local cache and builds with zero warnings/errors after removing its redundant options
configuration dependency. A full solution restore could not complete because sandbox networking blocked NuGet and the
safe cache-only retry proved several unrelated projects require packages absent from the local cache. Unsandboxed
restore was not authorized because it would disclose repository dependency metadata; this environmental gate is not
reported as product success or failure.

Repository truth gates remain coherent: package quality reports 93 packages, 0 repair, 10 review, and 83 structurally
ready; product surface reports 43 claims and 93 packages with generated outputs current; public documentation truth
passes; structural docs lint has 0 errors and 1,477 baseline warnings. Initiative validation retains only the known
DAC-11/DAC-51/DAC-53/DAC-54/DAC-56 dependency mismatches, the pre-DAC-30 multiple-lane rule, and missing historical
DAC-09/DAC-10 progress rows; DAC-41 adds no mismatch.

## Definition of done

- [x] One empty-root implementation passes every advertised Entity/Source/KeyValue/file claim.
- [x] Persist-then-publish, detached values, cold restart, corruption, duplicate identity, and size bounds are proven.
- [x] Canonical aliases coordinate one host slot; file admission is exact and failed access cannot grow state unbounded.
- [x] Read-only/External/map/streaming/atomic/native-index declines fail before mutation or hidden fallback.
- [ ] Strict Forge has a complete versioned packet or remains explicitly deferred.

## Stop conditions

Eager or process-static filesystem state, retained caller references, unbounded files/gates/input, source-alias lost
updates, mutate-before-persist, corruption-as-empty, full-scan streaming, compatibility paths, or fabricated
multi-process/atomic/durability guarantees block certification.
