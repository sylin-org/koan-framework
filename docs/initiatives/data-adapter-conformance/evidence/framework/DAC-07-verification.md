---
type: EVIDENCE
domain: data
title: "DAC-07 verification — Mapping compiler and Relational Family substrate"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: passed
  scope: framework-owned mapping compiler and provider-neutral relational planning
---

# DAC-07 verification — Mapping compiler and Relational Family substrate

## Result

PASS for the Framework mapping and Relational Family boundary. Applications declare a source/entity shape once with
the compact `Container`/`Key`/`Property` and `Name`/`Path`/`Object` grammar. One immutable `MappingPlan` owns logical
authority, complete identity, physical paths, direction, generation, codecs, compiled member access, structured-value
shape, indexes, and stable receipts.

Identity-plus-object, flat-name, hybrid, and nested physical-path layouts compile through the same descriptor.
Hydration, insert/update values, identity predicates, filters, ordering, patch, conditional write, selective reads,
and indexes reuse the same `MappingBindingPlan` instance and encoding. Duplicate authority, ambiguous paths,
incomplete identity, invalid structured/scalar selection, asymmetric writable codecs, and unsupported consumer use
reject before provider dispatch.

The Relational Family consumes that plan and emits symbolic commands, exact physical parameters, materialization,
schema definitions, and native-proof requirements without SQL or provider SDK types. External lifecycle performs zero
DDL. The previous generic schema interface remains temporarily callable by current connectors, but now compiles a
compatibility `MappingPlan` and delegates to the same orchestration path. The Family contains no reference to
`ProjectionResolver`, `ProjectedProperty`, `IndexMetadata`, or the removed process-static command cache.

## Application surface

```csharp
koan.Data.Source("LegacyErp").Map<Customer>(map => map
    .Container("dbo", "CUSTOMER")
    .Key(customer => customer.Id).Name("CUSTOMER_NO")
    .Property(customer => customer.Name.Full).Name("DISPLAY_NM")
    .Property(customer => customer.Profile).Object("PROFILE_JSON"));
```

`Key(...).Parts(...)` adds a composite identity. `Generated`, `ReadOnly`, and `Codec` are binding-local behavior.
Managed identity-plus-object stores can request the explicit `MappingConvention`; an application declaration always
wins. There is no relationship, unit-of-work, implicit-join, schema-migration, or universal-LINQ surface.

## Executable evidence

| Evidence | Result |
|---|---|
| Mapping and Relational Family oracle | 12 new mapping cases plus four ownership/compatibility cases; 16/16 passing |
| Four mapping shapes and complex values | PASS, including missing versus null, nested objects/collections, and independent root-object identity |
| Identity and codec boundary | PASS for single/composite/generated identity, symmetric codec enforcement, and a legal read-only decode-only codec |
| Consumer equality | PASS: hydration/write/filter/order/patch/conditional/projection/index plans share binding identities and physical encodings |
| Host/performance structure | PASS: host isolation, positive configurable bound, same-plan warm reuse, compiled accessors, and cached per-consumer use plans |
| Relational lowering | PASS for get/query/insert/update/delete/patch/conditional symbolic command plans and mapped filter translation |
| Schema/lifecycle | PASS for exact definition mismatch, missing shape, post-validation, and External zero-mutation rejection |
| Claim honesty | PASS: unsupported TTL is not lowered as an ordinary index; stored/rewrite-free/index claims remain unproved without matching features |
| Adversarial mutation | PASS: forged write path, filter encoding, and index path all fail `RelationalPlanGuard` |
| Source Integration/Direct regression gate | 21/21 passing |
| `Koan.sln` restore-free build | PASS in 21 seconds; zero warnings and zero errors |
| Initiative integrity | PASS: 41 cards, 41 progress rows, 41 roadmap rows, 105 primer IDs, 22 packets; 15/15 mutation cases |
| Diff hygiene | `git diff --check` PASS; repository line-ending notices only |

Commands:

```powershell
dotnet test tests/Suites/Data/Relational/Koan.Data.Relational.Tests/Koan.Data.Relational.Tests.csproj --no-restore
dotnet test tests/Suites/Data/Core/Koan.Tests.Data.Core/Koan.Tests.Data.Core.csproj --no-restore --filter "FullyQualifiedName~DirectDataAccessSpec|FullyQualifiedName~SourceIntegrationSpec"
dotnet build Koan.sln --no-restore
pwsh -NoProfile -File docs/initiatives/data-adapter-conformance/tools/Test-Initiative.ps1
pwsh -NoProfile -File docs/initiatives/data-adapter-conformance/tools/Test-Initiative.Mutations.ps1
```

## Ownership proof

- Data Abstractions owns only immutable, provider-neutral mapping vocabulary, paths, codecs, values, and receipts.
- `EntityMapBuilder<TEntity>` and the source-scoped declaration catalog own the compact application grammar and
  duplicate rejection.
- `MappingPlanCompiler` owns validation, structured expansion, compiled CLR access, stable plan identity, and index
  compilation exactly once.
- `IDataMappingPlans` owns a bounded, host-local source/entity plan set. There is no process-static mapping cache.
- `MappingPlan` owns hydration, writes, identity decomposition, exact consumer resolution, codec reuse, selective
  physical reads, and warm `MappingUsePlan` reuse.
- `RelationalCommandPlanner`, mapped `SqlFilterTranslator`, and `RelationalSchemaOrchestrator` consume only compiled
  mapping facts. `RelationalPlanGuard` rejects any changed path, shape, type, encoding, identity, receipt, or index.
- Relational adapters retain dialect syntax, SDK/native types, connections, exact failure codes, definition probes,
  and execution. Document/KV adapters are not forced to adopt relational concepts.

## Primer-row disposition

| Rows | DAC-07 disposition |
|---|---|
| A-07 | PASS for one declared shape compiled before business dispatch; provider-native declared-shape validation remains an adapter proof. |
| E-01–E-04 | PASS for identity-plus-object, flat-name, hybrid, and scalar nested-path compile/round-trip oracles. |
| E-05–E-07 | PASS for complete identity, generated behavior, read/write authority, structured missing/null behavior, and symmetric query/write codec encoding. |
| E-08 | PASS for exact selective-read planning and native-proof receipt; actual provider pruning remains a LIVE adapter proof. |
| E-09–E-10 | PASS for compile-time conflict and incomplete/invalid-map rejection with typed corrections. |
| E-11 | PASS for shared binding identity across every exposed consumer plus adversarial plan-guard mutations. |
| E-12–E-14 | PASS for honest stored/derived/index plan vocabulary and same-expression/same-encoding enforcement. Native maintenance, applicability, and rewrite-free benefit remain provider plan proofs. |
| E-15 | PASS for native-TTL qualification and unsupported-TTL suppression; expiry behavior remains a LIVE provider proof. |
| P-02–P-04 | PASS structurally for compiled warm access, bounded host plans, one symbolic plan per operation, and no hidden fallback. Provider-relative allocation/dispatch/work budgets remain certification proofs. |
| P-06 | PASS: mapping, materialization, relational command/schema planning, and dialect execution each have one named owner. |

No connector production implementation was changed for DAC-07. Connector source was consulted only after the design
and tests were complete to verify the retained public compatibility call shape; it was not used as implementation
lineage. SQLite and MongoDB still begin from empty implementation roots on their gold cards.
