---
type: SPEC
domain: data
title: "DAC-21 Build the MongoDB Gold Adapter from an Empty Implementation"
audience: [architects, maintainers, developers, ai-agents]
status: in-progress
last_updated: 2026-07-29
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: behavior-pass-strict-defer
  scope: MongoDB clean-room replacement, greenfield integrity, and live 35-case verification
---

# DAC-21 — Build the MongoDB gold adapter from an empty implementation

| Field | Value |
|---|---|
| Phase / kind | gold / ground-up replacement |
| Depends on | DAC-15 |
| Unlocks | DAC-24 |
| Primer scope | complete ratified MongoDB manifest |
| Production writes | MongoDB connector, newly designated MongoDB tests/docs, and `evidence/mongodb/**` |
| Owner | Adapter(MongoDB) |

## Meaningful outcome

MongoDB becomes the lean document reference: clear setup, honest topology-sensitive behavior, disciplined resource
ownership, one native execution path, and minimal warm-path work.

## Approved vertical-slice exploration

**Task:** Replace the MongoDB connector from an empty implementation root, using current code only for public facts,
provider constraints, and black-box failures.

**Application intent:** Reference MongoDB, call `AddKoan()`, and use ordinary `Entity<T>` persistence; optionally inspect
an external source, declare a compact aggregate map, or give a MongoDB pipeline a business name.

**Public expression:** The managed path is package + `AddKoan()` + `Entity<T>`. External decisions remain
`Source(...).Map<T>(...)`, `Data.Source(...).Inspect()`, and `Query(..., q => q.Pipeline(...))`; configuration selects
source, connection, database, lifecycle, and access. No repository, driver registration, or adapter service appears in
application code.

**Guarantee/correction:** MongoDB performs native BSON CRUD/query/count/page/bulk work with exact receipts and bounded
cursors. Read-only and External policies reject forbidden work before mutation or DDL. Atomic batches remain unclaimed;
an atomic requirement rejects before mutation instead of inferring transaction support from an unproved topology.

**Complete intent surface:** There are no user actions beyond the package reference, ordinary Koan bootstrap/API,
optional compact declarations, source policy/configuration, and a reachable MongoDB deployment.

**Public concepts:** `Pipeline` is the precise Mongo-native registered-read leaf. All other concepts—Source, Container,
RecordSet, Map, Name, Object, lifecycle, access, and read bounds—are already provider-neutral Data concepts.

**Docs read:** The development primer fixes the experience, four mapping shapes, registered-operation grammar, policy,
and hot-path laws. The responsibility map assigns policy/mapping/materialization to Data and BSON/topology/resources to
MongoDB. Architecture principles require Entity-first use, one compiled decision, and honest capabilities. The current
Mongo README/TECHNICAL contribute package/configuration/provider facts only; their implementation claims are not
authority.

**Code read:** `IDataRepository`/`IQueryRepository` and exact receipts define the execution seam; `MappingPlan` and
`RecordSet` own provider-neutral mapping/materialization; Source Integration owns inspection and named results. The
existing 2,700-line Mongo connector is retirement evidence, not a pattern. Official MongoDB documentation confirms
driver class maps must exist before use, BSON pipelines use physical names, clients should be reused, and transactions
require a replica set or sharded cluster—standalone servers do not support them.

**Reusing:** public provider/package/configuration identities; the pinned MongoDB driver; Data source routing, policy,
mapping, lifecycle, receipts, materialization, and naming; stable discovery/provenance contracts. No current Mongo
repository, client-provider, translator, serializer/convention, cache, control flow, or test structure is reused.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| adapter activation and immutable route | `MongoAdapterFactory.cs` | one election/routing authority |
| host-owned clients/databases | `Runtime/MongoClientManager.cs` | one bounded resource owner per physical route |
| managed/mapped BSON codecs | `Runtime/MongoEntityPlan.cs` | compile identity, polymorphism, mapping, and values once |
| native query lowering | `Runtime/MongoQueryCompiler.cs` | one complete Filter/sort/page/count translator with exact receipts |
| collection/index realization | `Runtime/MongoSchema.cs` | only Mongo DDL/introspection owner |
| repository and batch dispatch | `MongoRepository.cs`, `Runtime/MongoBatch.cs` | one CRUD path and one explicit commit path |
| inspection and neutral BSON reader | `Runtime/MongoInspector.cs`, `Runtime/MongoNeutralReader.cs` | bounded provider-neutral collection/record projection |
| named pipeline binding/execution | `MongoPipeline.cs`, `Runtime/MongoSourceIntegration.cs` | precise native leaf plus one source execution path |

**Coalescence:** SQLite is the closest completed gold pattern for ownership and receipts, not code reuse. MongoDB is
document-specific at the adapter layer; a new family abstraction would add ceremony without repeated proved mechanics.
The existing `DocumentStore`, Mongo repository, client provider, filter translator, driver conventions/serializers,
telemetry wrapper, and duplicate connection logic are deleted from the replacement path.

**Ergonomics:** Managed persistence remains invisible behind Entity. External mapping uses the same compact language as
SQLite. Inspection returns provider-neutral Containers and a bounded neutral union of observed BSON fields, preserving
missing separately from null. Named reads say `Pipeline`, without repeating query/read context.

**Constraints satisfied:** Entity remains the application surface; no HTTP surface is involved; stable identifiers stay
in connector constants and tunables in `MongoOptions`; large reads are explicitly paged; no placeholders, parallel
runtime path, unbounded cache, or process-host state is introduced; README and TECHNICAL change with behavior.

**Risks:** Real transaction proof requires replica-set/sharded topology while the default test fixture is standalone;
driver retryable writes can make commit outcome ambiguous; heterogeneous inspection must preserve missing/null and
bounds; native BSON serialization must reject reserved Koan-field collisions without global mutable conventions.

## Approved lifecycle correction

**Task:** Remove provider I/O from MongoDB option materialization and resolve connection intent once, asynchronously,
at first provider use.

**Application intent:** Referencing MongoDB plus `AddKoan()` remains sufficient; application code uses `Entity<T>` and
never manages discovery.

**Public expression:** No change: package reference, `AddKoan()`, optional source configuration, then ordinary Entity or
source operations.

**Guarantee/correction:** Reading configuration is pure and immediate. The first MongoDB operation asynchronously
resolves `auto` or explicit Zen Garden intent; explicit unresolved intent fails with the existing corrective message
before database work.

**Complete intent surface:** No new user action, decoration, service registration, or configuration is required.

**Public concepts:** None added.

**Docs read:** Architecture principles assign configuration and resource ownership clearly; the Data reference preserves
the zero-ceremony Entity surface; the document-store catalogue makes MongoDB the resource-disciplined gold reference;
this card requires discovery off warm operations.

**Code read:** `MongoOptionsConfigurator` blocks twice on asynchronous discovery; `MongoAdapterFactory` creates immutable
routes; `MongoClientManager` owns bounded physical resources; repository, schema, inspection, health, and registered
operations are already asynchronous at the provider boundary; the current configuration specs incorrectly certify
discovery during options access.

**Reusing:** The existing discovery coordinator, route, constants, corrective error, client manager, and asynchronous
operation boundaries.

**Creating new:** No public or top-level type is created. Async route resolution and exact bounded single-flight
admission live in `Runtime/MongoClientManager.cs`; pure intent normalization remains in `MongoOptionsConfigurator.cs`;
lifecycle regression coverage lives in the existing Mongo configuration specs.

**Coalescence:** `MongoClientManager` remains the sole adapter-level route/resource owner; discovery is absorbed there,
configurator discovery methods are deleted, and no resolver service or family abstraction is introduced.

**Ergonomics:** No application-visible branch, registration, or type is added. IntelliSense and the coding model remain
unchanged.

**Constraints satisfied:** Entity remains the application surface; no HTTP or streaming behavior is involved; existing
constants and typed options are reused; no unbounded state, placeholder, global mutable state, or shadow execution path
is introduced; connector technical documentation changes with the lifecycle guarantee.

**Risks:** Shared first-use resolution must not be poisoned by one caller's cancellation; exact route capacity and
disposal must remain race-safe; a cached no-restore build is the strongest permitted compilation check in this session.

## Required work

1. Verify DAC-15's common base, empty MongoDB implementation root, ratified contract, target manifest, and complete
   retirement inventory. Use the repository explore workflow, but treat the former adapter only as provider/public
   evidence—not a pattern to preserve.
2. Design from Framework/Document contracts outward. List every runtime type, compiled plan, cache, resource owner,
   background task, dispatch boundary, and abstraction in `rewrite/replacement.json`; give each a `contract`,
   `shared-mechanics`, or measured `hot-path` reason.
3. Implement activation, client/database/collection ownership, routing and readiness, document codecs, mapping,
   CRUD/query/count/page/bulk/conditional/transaction behavior selected by the manifest, registered pipeline/function
   operations, inspection, native receipts, exact driver-code failure mapping, cancellation, facts, health, and
   disposal.
4. Derive topology-sensitive capabilities from proved provider state and permissions. Keep discovery, mapping
   compilation, capability negotiation, and readiness off warm operations. Bound caches, cursors, result
   materialization, and background state.
5. Never infer failures from messages, swallow driver errors, hide client evaluation as native, replay uncertain
   writes, or claim transactions/index behavior the selected topology cannot prove.
6. Replace adapter-specific tests with contract-derived black-box, native-command, fault, topology, lifecycle,
   negative, soak, and performance cases. Do not port old helper/test structure.
7. Complete the new-source, compile/registration, one-execution-path, moving-parts, evidence, and retirement-absence
   manifests. The accepted change deletes the former implementation atomically; it never contains old and new routes.

## Provider-local Forge cells explore

**Task:** Bind provider-specific record conformance facts without a registry.

**Application intent:** Adapter authors keep focused ordinary tests; Forge recognizes exact primer-keyed facts
contributed by that adapter.

**Public expression:** No application change. Test facts retain the existing
`<Acceptance ID>/<Case>/<Owner>: <title>` display-name grammar.

**Guarantee/correction:** MongoDB's D-01 through D-05 claims gain real-provider facts. A missing, duplicate, skipped,
or failing local cell remains non-green.

**Complete intent surface:** The shared base cells plus exact facts physically declared in the concrete AODB spec.
No behavior is inferred from unrelated tests.

**Reusing:** The primer catalog, row-key parser, MongoDB source inspector, real MongoDB fixture, and current source
journey.

**Creating new:** No runtime type, registry, or manifest. Forge reads optional local cells from the concrete spec and
the existing MongoDB AODB spec adds five focused facts.

**Coalescence:** The broad inspection journey is harvested and reduced. Forge remains the sole process orchestrator.

**Constraints satisfied:** No entity hot-path change, packet stub, or second catalog. D-05 exposed one bounded
inspection defect and justified the smallest provider correction.

**Risks:** Each local fact must prove its complete named case. D-01 through D-05 stay separate; framework-owned
negative edge cases remain separate evidence instead of being falsely attributed to the live-provider fact.

## Verification

- Clean build and complete MongoDB, Document, Data Core, AdapterSurface, and strict Forge suites on every claimed real
  topology and least-privilege role.
- Native command/trace and dispatch evidence for filters, sorts, pages, counts, indexes, bulk, conditional writes,
  transactions, registered operations, and cancellation.
- ReadOnly/External policy, commit uncertainty, pool saturation, restart/durability, disposal, two-host, soak, and
  bounded-state cases.
- Provider-relative cold/warm allocation and elapsed baselines; mutations catch policy bypass, static topology
  overclaim, fallback, extra dispatch, swallowed failure, message classification, and unbounded state.
- `Test-GreenfieldReplacement.ps1` passes only with `startedEmpty: true`, complete retirement, one execution path, no
  shadow path, and justified moving parts.

## Implementation result

The clean-room implementation landed atomically in `5cf55ab3ab04847d61d6ee1e089c084a76df8f61` from common base
`86c18819cf03160c20a001d91f3bd2f257fd1a0d`. It removed 2,592 lines from the former connector and added 2,032 lines
across the replacement, including one repository/native execution path. Eleven former implementation owners are
absent; no compatibility or shadow repository remains.

Recovery verification on 2026-07-29 restored all MongoDB source byte-identically to the pushed commit and ran the
existing exact-source 34-case binary against a fresh MongoDB 8.3.4 container with zero failures and provider skips. The
follow-up lifecycle correction then removed synchronous discovery from options materialization, moved bounded
single-flight resolution to first provider use, and proved that caller cancellation does not poison the shared route.
An explicitly offline restore from the installed package cache produced a fresh zero-warning build. Five focused,
primer-keyed real-provider facts now prove bounded container listing, source-bound resolution, honest description,
bounded sampling, and lossless MongoDB records. D-05 exposed that the driver's ordinary `BsonDocument` deserializer
rejects legal duplicate field names; the bounded inspection path now reads raw BSON, preserves duplicate occurrences
and field order, copies values into the neutral algebra, and immediately disposes the native buffers. The resulting
40-case suite passes against MongoDB 8.3.4. Canonical Forge is green on all 11 expected MongoDB rows. The greenfield
gate reports `source=23 parts=9 retired=11`, all 23 source hashes match, and the initiative gate passes.

Strict packet emission, topology expansion, stable performance evidence, broad shared/Web regressions, and independent
certification remain deferred. The existing packet compiler and validator do not yet provide an adapter-evidence
emission command; no local certificate was synthesized.

## Definition of done

- [ ] Every ratified MongoDB row passes and every decline fails closed for each selected topology.
- [x] The connector contains only MongoDB-native responsibilities behind certified shared contracts.
- [x] One activation, registration, repository/native execution, claim, and adapter-test authority remains.
- [x] Every moving part has a necessary contract/shared-mechanics/hot-path reason.
- [ ] Setup, topology truth, limits, diagnostics, resource behavior, and performance are exemplary and reproducible.

## Stop conditions

Stop for a missing shared seam, ambiguous public behavior, incomplete retirement inventory, unavailable required real
topology, provider limitation hidden by emulation, need for an old/new bridge, or unjustified moving part.
