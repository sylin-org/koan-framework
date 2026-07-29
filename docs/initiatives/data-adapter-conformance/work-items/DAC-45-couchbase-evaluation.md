---
type: SPEC
domain: data
title: "DAC-45 Evaluate and Certify the Couchbase Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: connector-acceptance-green
  scope: Couchbase greenfield implementation, real-provider acceptance, and shared SQL++ regression
---

# DAC-45 — Evaluate and certify the Couchbase adapter

| Field | Value |
|---|---|
| Phase / kind | fleet / whole-adapter greenfield rebuild |
| Depends on | DAC-30 |
| Primer scope | dynamically selected Couchbase manifest |
| Production writes | Couchbase connector and necessary shared dead-path retirement authorized |
| Owner | Adapter(Couchbase); Document Family rows split |

## Meaningful outcome

Couchbase proves that the document contract is provider-neutral rather than a MongoDB-shaped abstraction, including
scope/collection routing, CAS, indexing, bootstrap, and resource behavior.

## Approved greenfield exploration

**Task:** Replace the Couchbase connector from an empty implementation root and certify its document behavior against
the current Koan Data contract and a pinned real Couchbase provider.

**Application intent:** Reference Couchbase, call `AddKoan()`, and use ordinary `Entity<T>` persistence; optionally map
one aggregate onto an existing bucket/scope/collection, inspect that source, or invoke a bounded registered SQL++ read.

**Public expression:** Managed persistence remains package + `AddKoan()` + `Entity<T>`. External integration remains
source policy/configuration, `Source(...).Map<T>(...)`, `Data.Source(...).Inspect()`, and
`Query`/`Scalar(..., query => query.Lane(...).Sql(...))`. Couchbase SDK clients, CAS tokens, bucket managers, index
management, and query options do not enter ordinary application code.

**Guarantee/correction:** One immutable `MappingPlan` drives managed whole-document storage and explicit physical
maps. Known-key operations execute through native KV; set queries execute through parameterized SQL++ and return exact
receipts; conditional replace uses native opaque CAS; External performs no bucket/scope/collection/index mutation;
ReadOnly rejects before provider I/O; registered SQL++ is accepted only through a read lane and executes with the
provider read-only query option. Unsupported mapping, ordering, atomicity, or durability rejects rather than silently
scanning, weakening, or replaying application work.

**Complete intent surface:** Package reference, `AddKoan()`, a reachable Couchbase cluster and precreated bucket for an
External source, optional source policy, and optional compact map or registered read are the complete user actions.

**Public concepts:** Source selects the physical/policy route; Namespace represents a Couchbase scope; Container
represents a collection; Partition selects a Koan logical set and may realize a scope; Key/Property/Object/Name/Path
express aggregate-to-document bindings; lifecycle/access separate DDL authority from data mutation authority; Lane
expresses provider-enforced read execution; Query/Scalar select the result cardinality; Sql selects SQL++ as the native
binding leaf. No Couchbase-only public persistence abstraction is required.

**Docs read:** The adapter primer requires delight-first behavior, empty-root replacement, one concern owner, bounded
hot paths, truthful claims, and real-provider proof. Architecture principles require Entity-first intent, host-owned
resources, immutable warm plans, and one current path. DATA-0110 fixes the compact provider-neutral mapping and
registered-operation grammar. This DAC card owns the Couchbase provider delta. The project README/TECHNICAL describe
the compatibility identities and existing claims to re-prove, not an implementation to retain.

**Code read:** `IDataAdapterFactory`, `IDataSourceIntegrationFactory`, `MappingPlan`, `RepositoryQueryResult`, and the
source inspection contracts define the current seams. `EntityJsonSerialization` and `ManagedFieldJsonInjector` own
safe polymorphic JSON and managed-field projection. The rebuilt Mongo connector is the closest document-provider
evidence for ownership and compact language, not a template. The retired Couchbase implementation was inventoried by
public type surface and black-box tests only; its repository/bootstrap/cache/control flow is not an authoring input.

**Existing constants/options/contracts:** Provider identity `couchbase`, priority 30, configuration section
`Koan:Data:Couchbase`, connection/bucket/scope/collection/credentials/query-timeout/durability names, SDK dependency,
source policies, mapping plans, neutral records, operation plans, receipts, capabilities, health, discovery, naming,
polymorphism, and managed-field contracts already exist. A current route, client owner, document projection,
query compiler, schema realization, inspector, and registered SQL++ integration need new implementations.

**Reusing:** Ratified package/provider/configuration identities; CouchbaseNetClient; Data source policy and routing;
mapping plans; Entity serialization and managed-field contracts; filter AST; query receipts; neutral-record contracts;
naming; health/discovery contracts; and pinned Testcontainers fixture. No retired Couchbase implementation body is
retained. The deprecated companion transactions package is not part of the target path because transactions are
integrated into the current SDK.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| activation, constants, options, route, discovery, health | `src/Connectors/Data/Couchbase` | one concrete provider authority |
| bounded host-owned cluster/resource owner | `src/Connectors/Data/Couchbase/Runtime` | SDK recommends shared thread-safe cluster/bucket/scope/collection handles; routes require bounded lifetime |
| immutable aggregate/document projection | `src/Connectors/Data/Couchbase/Runtime` | realizes shared `MappingPlan`, polymorphism, managed fields, and Couchbase key encoding once per repository |
| direct-KV repository and SQL++ query compiler | `src/Connectors/Data/Couchbase/Runtime` | native provider hot paths and exact receipt ownership |
| managed/external schema realization | `src/Connectors/Data/Couchbase/Runtime` | bucket/scope/collection/index policy is Couchbase-native |
| source integration, SQL++ binding, and inspector | `src/Connectors/Data/Couchbase/Runtime` plus one public binding leaf | provider-native named reads and bucket/scope/collection metadata |
| focused acceptance cases | `tests/Suites/Data/Connector.Couchbase` | real-provider proof for Entity, mapping, policy, inspection, named reads, CAS, and resource ownership |

**Coalescence:** Closest pattern is the rebuilt Mongo connector's factory → immutable route → host-owned client → one
repository path, but provider execution remains Couchbase-specific. Specificity is Adapter for SDK resources, KV,
SQL++, CAS, transactions, management, and metadata; Framework for policies, plans, receipts, and neutral projection.
Disposition is `REBUILD`: delete every retired Couchbase implementation route. `Koan.Data.Core.Document.DocumentStore`
and its `OnceGate` have no consumer outside the retired Couchbase repository and are deleted as superseded scaffolding,
not adopted as a family substrate. A wider shared family would incorrectly force one JSON/SDK/query lifecycle onto
MongoDB; a narrower repository-owned cluster would reconnect and repeat readiness per Entity.

**Ergonomics:** Ordinary code remains package + `AddKoan()` + Entity verbs. Compact maps use the same words as every
adapter. Inspection returns Source/Namespace/Container/record descriptors rather than bucket-manager types. Named
operations use `Sql` because SQL++ is the explicit native leaf and do not expose query-option ceremony. IntelliSense
has no repository/client/bootstrap branch. Unsupported guarantees fail at composition or the first bounded provider
boundary with a corrective source/container message.

**Constraints satisfied:** Entity statics remain the ordinary surface; no HTTP path is involved; stable identifiers
live in connector constants; tunables remain typed options; provider reads are bounded or explicitly paged; routes and
schema tasks are bounded and host-owned; no placeholder, compatibility, shadow, sync-over-async, exception-text
classification, or hidden Mongo fallback is permitted; README and TECHNICAL change with behavior.

**Risks:** Couchbase Community bootstrap is expensive; query visibility requires explicit scan consistency; collection
and index management permissions differ from data permissions; CAS tokens are opaque; transactions may retry an SDK
delegate and therefore cannot replay arbitrary application callbacks; collection metadata does not provide a fixed
document schema; SQL++ identifier quoting and null/array semantics require real-provider convergence proof.

## Harvested baseline

The retired connector built cleanly but passed only 17/21 cases against `couchbase:community-8.0.2` with
`CouchbaseNetClient` 3.9.4. Four ordinary query, filter-convergence, polymorphism, and provider-bounded-stream cases
failed because the adapter performed filter work without returning `FilterHandled=true`; Data correctly rejected the
unproven receipt. The run took 8 minutes 11 seconds, with repeated selected-route readiness dominating execution.
These are frozen negative lessons and black-box acceptance cases, not implementation inputs.

## Replacement result

The retired implementation was removed before authoring the replacement. The current connector has one factory,
immutable route/document/query plans, one bounded host-owned resource pool, direct KV hot paths, parameterized SQL++,
native CAS, managed/external schema ownership, neutral inspection, and registered read-only SQL++ operations. The dead
shared `DocumentStore` and `OnceGate` scaffolding and the deprecated transactions companion dependency were removed.

Real-provider acceptance against Couchbase Community 8.0.2 and SDK 3.9.4 is green at 25/25 with zero skips. The
observed complete run took 52 seconds after build. The new cases prove compact scalar/object/nested/composite mapping,
preservation of unmapped external content, application-assigned-key truthfulness, External and ReadOnly independence,
durable KV mutation, source-neutral inspection, named record/scalar SQL++ reads, provider mutation rejection, and one
cluster shared across entity containers. Existing CRUD, filter convergence, exact receipts, polymorphism, bounded
streaming, CAS, isolation, naming, health, and participation cases are green.

The provider-neutral source-integration Core slice passes 24/24 and Relational regression passes 16/16 after moving
the shared `.Sql(...)` leaf out of the relational assembly. The broad Core run passed 503/513; its ten failures were
four Windows Event Log permission failures and six tests with no configured AI embedding source, independent of the
Couchbase/shared SQL binding change. Heavy permission postures, restart/fault injection, and soak remain fleet
certification work rather than connector acceptance blockers.

## Execute

1. Pin Couchbase image/version, SDK, cluster/bucket/scope/collection fixture, memory allocation, identities, and create
   `evidence/couchbase/`.
2. Inventory cluster bootstrap, bucket/index readiness, naming/routing, CRUD/query/count, bulk/batch/CAS, Direct,
   inspection/records/registered reads, durability/consistency claims, health, errors, and disposal.
3. Exercise all source postures with precreated external scopes/collections and provider permissions. External cannot
   bootstrap buckets/indexes; read-only cannot mutate through KV/query/management SDK paths.
4. Prove CAS/conditional replace natively, query/index receipts, partial/failure envelopes, cancellation, timeouts,
   pool/cluster ownership, restart/soak, and provider-relative baselines.
5. Compare Document Family rows with MongoDB without importing Mongo-specific assumptions.
6. RED creates one-owner Document/Couchbase remediation cards and blocks; no production edits.

## Verification

- Complete Couchbase/shared suites, strict Forge, packet validator, provider permissions, and heavy bootstrap/fault
  cells execute with timing/prerequisites recorded.

## Definition of done

- [x] Couchbase connector acceptance is green for its exact claims and scope/collection topology.
- [x] CAS, index, lifecycle, and durability meanings are precise and provider-backed.
- [x] Common mapping, policy, receipt, neutral-record, and source-integration behavior resolves to shared Data owners.
- [ ] Fleet permission, restart/fault, soak, Forge, and packet-validator evidence is complete.

## Stop conditions

Insufficient container memory, missing permission posture, skipped bootstrap/fault evidence, or production remediation
blocks certification.
