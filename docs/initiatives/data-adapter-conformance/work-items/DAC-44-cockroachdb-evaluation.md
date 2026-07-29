---
type: SPEC
domain: data
title: "DAC-44 Evaluate and Certify the CockroachDB Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: connector-acceptance-green
  scope: CockroachDB greenfield connector, real-provider acceptance, and Npgsql-family regression
---

# DAC-44 — Evaluate and certify the CockroachDB adapter

| Field | Value |
|---|---|
| Phase / kind | fleet / whole-adapter greenfield rebuild |
| Depends on | DAC-42 |
| Primer scope | dynamically selected CockroachDB manifest |
| Production writes | CockroachDB connector and necessary Npgsql provider-delta seam authorized |
| Owner | Adapter(CockroachDB); Npgsql/Relational rows split |

## Meaningful outcome

CockroachDB is certified as a real distributed provider delta, not assumed green because it speaks PostgreSQL wire
protocol or reuses Npgsql code.

## Execute

1. Pin CockroachDB version/topology, Npgsql driver, security/roles, and create `evidence/cockroachdb/`.
2. Begin with PostgreSQL/Npgsql Family evidence but rerun every provider-bound LIVE/PLAN/FAULT/LIFE case. Shared code
   reduces implementation duplication, not real-provider proof.
3. Audit the currently thin provider-specific suite by dynamic surface/claim expansion; missing cases are RED.
4. Focus on schema/type/index differences, transaction retry/conflict semantics, serializable behavior, generated
   values, bulk limits, cancellation, topology/network faults, and exact native errors.
5. Exercise all four source postures and strict mapping/query/inspection/named-operation cells selected by the manifest.
6. Replace the connector from an empty implementation root. RED creates bounded Cockroach, Npgsql, or Relational
   changes with one owner and real-provider re-entry proof.

## Approved greenfield exploration

**Task:** Replace the CockroachDB connector from an empty implementation root, using the retired connector only for
provider facts, public identities, negative lessons, and black-box regression cases.

**Application intent:** Reference CockroachDB, call `AddKoan()`, and use ordinary `Entity<T>` persistence; optionally
map an aggregate to an existing CockroachDB table, inspect an external source, or invoke a bounded registered SQL read.

**Public expression:** Managed persistence remains package + `AddKoan()` + `Entity<T>`. External integration remains
source configuration, `Source(...).Map<T>(...)`, `Data.Source(...).Inspect()`, and
`Query`/`Scalar(..., query => query.Lane(...).Sql(...))`. No repository, Npgsql, retry loop, or provider registration
enters application code.

**Guarantee/correction:** One immutable `MappingPlan` drives managed Id+object storage and explicit physical maps.
Cockroach executes supported CRUD, query, count, paging, batch, conditional write, inspection, and registered reads
natively. External performs no DDL; ReadOnly rejects Entity writes before provider I/O; opaque SQL requires a
provider-enforced read lane. Cockroach serialization failures surface with their native SQLSTATE and are never
silently replayed. Unsupported PostgreSQL-only mechanics reject or use an explicit Cockroach realization.

**Complete intent surface:** Package reference, `AddKoan()`, a reachable CockroachDB endpoint, optional source
policy/configuration, and optional compact map or registered read are the complete user actions.

**Public concepts:** No new application concept is required. Source, lifecycle, access, read lane, Map, Container,
Key, Property, Object, Name, Path, Query, Scalar, and Sql already express the complete decision.

**Docs read:** The adapter primer requires an empty-root replacement, one owner per concern, real-provider proof, and
bounded hot paths. Architecture principles require Entity-first intent, one current path, immutable warm plans, and
truthful capabilities. DATA-0110 fixes the compact provider-neutral mapping and registered-operation grammar. DAC-44
owns the Cockroach provider delta and cannot reuse PostgreSQL LIVE evidence.

**Code read:** `IDataAdapterFactory`, `IDataSourceIntegrationFactory`, `MappingPlan`, `RelationalSourceIntegration`,
`NpgsqlRepository`, and `NpgsqlRepositoryOptions` are the contract seams. The rebuilt PostgreSQL connector shows the
current family construction boundary but is evidence, not an adapter template. The retired Cockroach factory,
configurator, discovery, module, health, and provenance bodies are not implementation inputs.

**Reusing:** Ratified package/provider/configuration identities; Npgsql dependency; Data source policy, mapping plans,
query coordination, receipts, managed fields, naming, registered-operation catalog, relational neutral readers, and
the rebuilt Npgsql Entity execution family. No current Cockroach execution class or body is retained.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| activation, constants, options, route, discovery, health | `src/Connectors/Data/Cockroach` | one connector package and source authority |
| one Cockroach inspector | `src/Connectors/Data/Cockroach/Runtime` | provider metadata, kinds, and safe bounded sampling |
| explicit stable-order policy | `src/Koan.Data.Relational.Npgsql` | identical Npgsql execution lifecycle with a real PostgreSQL-wire provider delta |
| provider acceptance cases | `tests/Suites/Data/Connector.Cockroach` | real Cockroach proof for Entity, mapping, policy, inspection, and named reads |

**Coalescence:** The closest pattern is the rebuilt PostgreSQL-to-Npgsql contract seam. The Npgsql Family owns shared
wire execution and receives one compiled stable-order policy; Cockroach owns routing, metadata, read-lane realization,
and provider claims. Disposition is `REBUILD`: delete the old configurator, provenance layer, telemetry wrapper,
module, discovery body, factory body, and health body. A wider Framework owner would encode a PostgreSQL-wire detail;
a narrower Cockroach repository would duplicate the entire Npgsql hot path.

**Ergonomics:** Cockroach remains indistinguishable from other Koan Data providers in ordinary application code.
Provider distinctions appear only in package/configuration, native SQL bindings, diagnostics, and failures. One
factory route and one Npgsql repository path keep IntelliSense and agent reasoning compact.

**Constraints satisfied:** Entity statics remain the ordinary surface; no HTTP path is involved; stable identifiers
live in connector constants; tunables remain typed options; reads page at the provider; caches stay bounded and
host-owned; there are no placeholders, compatibility branches, sync-over-async bridges, or hidden PostgreSQL fallback;
README and TECHNICAL change with behavior.

**Risks:** Cockroach lacks PostgreSQL `ctid`, defaults to serializable transactions, and reports retryable conflicts as
SQLSTATE `40001`. Generated identity, JSON-path mutation, schema metadata, read-only transaction syntax, and paging
must be proved against the pinned CockroachDB image. No automatic transaction retry is authorized without a separate
Koan replay contract.

## Harvested baseline

The pre-rewrite suite built cleanly but passed only 4/8 cases against `cockroachdb/cockroach:v26.2.3`. Four Entity,
polymorphism, and isolation cases failed with exact SQLSTATE `42703` because the shared repository emitted
`ORDER BY ctid`. This is the first frozen negative lesson and acceptance case for the replacement.

## Replacement result

The retired connector implementation was removed. The replacement owns only CockroachDB activation, configuration,
routing, discovery, health, inspection, read-lane realization, and the provider's Npgsql construction. It does not
reference the PostgreSQL connector and does not carry a Cockroach-specific repository.

The Npgsql family gained one bounded provider policy, `NpgsqlStableOrder`: PostgreSQL retains physical-tuple order and
CockroachDB orders by the compiled identity roots. This removes the `ctid` failure without admitting arbitrary SQL
hooks or forking the hot path.

The pinned `cockroachdb/cockroach:v26.2.3` acceptance suite proves:

- ordinary CRUD, native predicate query, count, explicit paging, and atomic batch behavior;
- all inherited AODB isolation and polymorphism cells;
- flat, object, nested-path, composite-key, generated-key, External, and ReadOnly mapping postures;
- registered records/scalars through a provider-enforced read lane; and
- provider-neutral container discovery, address resolution, record-shape description, and bounded sampling.

CockroachDB defaults to serializable transactions and identifies retryable serialization conflicts with SQLSTATE
`40001`. The connector deliberately surfaces this native failure; no automatic replay contract has been added.

## Verification

- Real-provider Cockroach connector suite: 17/17 passing on 2026-07-28.
- Npgsql-family relational ownership suite: 16/16 passing on 2026-07-28.
- PostgreSQL real-provider regression suite: 26/26 passing on 2026-07-28 after the stable-order seam.
- Distributed contention, cancellation, topology/network fault, and lifecycle soak remain certification-lane work;
  they are not inferred from the connector suite.

## Definition of done

- [x] Real Cockroach connector acceptance is green with explicit wire-compatible and provider-specific behavior.
- [x] Transaction/retry claims match Cockroach behavior and Koan's current no-replay rule.
- [x] The prior functional coverage deficit is closed with application-meaningful provider cases.
- [ ] Distributed FAULT/LIFE and soak evidence is captured before full fleet certification.

## Stop conditions

Provider unavailable, topology unpinned, PostgreSQL substitution for LIVE, or required production remediation blocks.
