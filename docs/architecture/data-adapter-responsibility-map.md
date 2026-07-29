---
type: REFERENCE
domain: data
title: "Data Adapter Responsibility Map"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-27
framework_version: v0.20.0
---

# Data adapter responsibility map

The Framework owns a provider-neutral decision once. A Family owns mechanics that have the same meaning, lifetime,
and failure boundary across providers. An Adapter lowers an accepted plan to one provider, performs native work, and
reports the exact outcome. Every concern below has one owner.

| Concern | Framework owner | Family owner | Adapter owner | Placement failure |
|---|---|---|---|---|
| Source declaration and access policy | `DataSourceBuilder`, `DataSourceRegistry`, immutable `DataSourcePlan`, and the first-boundary operation gate | none | declare supported native surfaces; obey the accepted plan | adapter parses Framework source policy or silently permits a rejected effect |
| Provider election and route identity | provider catalog, source registry, and redacted route decision | family may normalize a family-native address only after election | open the elected native target | adapter re-elects, reroutes, or publishes credentials/physical identifiers |
| Reachability, shape validation, and provision authorization | `DataSourceReadinessCoordinator` owns the three bounded host stages | family translates the declared shape and coordinates common validation mechanics | perform the requested native probe/DDL and return its receipt | adapter adds another readiness cache, retry loop, or policy gate |
| Aggregate-to-record mapping | `MappingDeclarationCatalog`, `MappingPlanCompiler`, and host-owned `IDataMappingPlans` | consume the frozen logical/physical plan | bind provider-native values and paths exactly as planned | adapter reflects the Entity, invents a mapping cache, or changes declared meaning |
| Relational command and schema planning | mapping plan plus symbolic relational command/schema contracts | `RelationalCommandPlanner`, `RelationalSchemaOrchestrator`, and plan guard | dialect lowering, parameters, native execution, and exact schema facts | provider repository rebuilds mapping/schema orchestration |
| Materialization and value conversion | `MappingPlan` member access, `MappingValueConversion`, and shared `RecordSetMaterializer` | family may supply a provider-family value reader with identical semantics | expose native values/ordinals and provider-specific conversions the contract requests | adapter ships a second object mapper or hides lossy conversion |
| Query split, fallback, paging, and final shaping | Data query boundary, filter pushdown coordinator, receipt validator, residual evaluator, and `RepositoryFacade` | translate only the complete accepted pushable expression | execute the native query and report handled axes/provider work | adapter performs concealed client paging, filtering, sorting, or N+1 dispatch |
| Entity lifecycle and managed fields | `RepositoryFacade`, host-owned write/read/transform plans, lifecycle pipeline, and final-visible-row rules | none unless a family primitive is truly identical | persist the final physical record and report item/commit outcomes | callbacks, stamps, soft-delete, isolation, or load lifecycle are duplicated below Core |
| Mutation and transaction truth | Framework batch/mutation result contracts and transaction coordinator | family can expose a genuinely shared native transaction primitive | one native dispatch, atomicity/commit facts, positional item outcomes | stronger atomicity is inferred from sequencing or missing outcomes are synthesized |
| Failures and restricted evidence | `DataFailure`, stable corrections, retry/replay/commit vocabulary, and bounded `DataNativeEvidenceStore` | translate a family failure only when semantics are exact | classify native type/code and write restricted evidence without raw prose | native message, command text, business value, secret, or exception object reaches public facts |
| Claims and applicability | `DataClaimSet`, `DataCapabilityProfiles`, and deterministic claim references | add no parallel capability registry | inertly declare exact observed/target/declined provider claims | claims differ between runtime, facts, health, descriptions, or executable tests |
| Describe, Explain, Doctor, facts, and health | `DataSourceDiagnosticsService`, `DataDiagnostics`, shared facts, and stable health vocabulary | provide pure family descriptors when useful | pure descriptor plus explicit non-mutating Doctor probe | Describe/Explain activates a client; Doctor provisions; public output leaks native evidence |
| Client, pool, cache, and integration lifetime | host owns bounded catalogs, source-integration activation, disposal, and cancellation boundary | family may own a host-scoped shared client abstraction | create/dispose native client resources inside that host boundary | mutable process-static runtime state, unbounded key space, orphan client, or caller-cancellation corruption |
| Conformance and performance evidence | shared executable TestKits and consumer compile contracts | family TestKit may provide reusable mechanics | focused real-provider tests, native receipts, and fixture-relative measurements | adapter copies verifier policy, treats a skip as success, or uses a global threshold |

## Adapter review

An adapter is correctly placed only when all answers are yes:

1. Can its production types be described as declaration, native translation, native dispatch, native resource
   ownership, native topology, or exact native evidence?
2. Does every operation consume an immutable Framework or Family plan rather than reconstructing one?
3. Are all caches host-owned, bounded, and removable without changing semantics?
4. Do claims, execution receipts, public diagnostics, and executable tests describe the same behavior?
5. Would deleting adapter-local orchestration leave a missing provider-neutral concern? If so, that concern belongs in
   the Framework or a justified Family before the adapter can pass P-06.

SQLite and MongoDB are reference implementations only when each passes this map from an empty implementation root.
