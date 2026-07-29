---
type: EVIDENCE
domain: data
title: "DAC-07 exploration — Mapping compiler and Relational Family substrate"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: pre-implementation placement and ergonomics record
---

# DAC-07 exploration — Mapping compiler and Relational Family substrate

**Task:** Implement one provider-neutral aggregate-to-record mapping language and one compiled plan that a relational
Family can consume without learning, copying, or reinterpreting the application grammar.

**Application intent:** “Use my ordinary Entity model with this physical record shape, and make every read, write,
query, patch, conditional write, projection, and index agree about that decision.”

**Public expression:**

```csharp
builder.Services.AddKoan(koan =>
{
    koan.Data.Source("LegacyErp").Map<Customer>(map => map
        .Container("dbo", "CUSTOMER")
        .Key(customer => customer.Id).Name("CUSTOMER_NO")
        .Property(customer => customer.Name.Full).Name("DISPLAY_NM")
        .Property(customer => customer.Name.First).Name("FIRST_NM")
        .Property(customer => customer.Profile).Object("PROFILE_JSON"));
});
```

The same grammar expresses identity-plus-object through root `Object`, a flat shape through scalar `Name` bindings,
a hybrid through scalar and subtree `Object` bindings, and a scalar inside a physical structured value through
`Path`. `Key(...).Parts(...)` expresses a composite identity; `Generated`, `ReadOnly`, and `Codec` add only their
distinct behavior.

**Guarantee/correction:** Compilation produces one immutable source/entity plan with one complete identity decision,
one writable authority per logical value, unambiguous physical paths, compatible codecs, and compiled CLR accessors.
Missing container/identity/location, duplicate or overlapping authority, partial identity, incompatible codec or
property types, an unreadable insert value, an unwritable hydration value, and undeclared codec asymmetry fail with a
typed correction before provider dispatch. Hydration and every physical consumer resolve the same binding instance
and encoding. External lifecycle can validate but cannot create, alter, or repair shape. Selective-read, stored-
projection, expression-index, and TTL claims remain false unless a provider supplies the separate native proof.

**Complete intent surface:** Reference Data Core and the selected connector; configure the source policy; declare one
map per source/entity inside `AddKoan(koan => ...)`; use ordinary Entity operations inside the selected source context.
Provider authors resolve the host-owned plan, ask it for write/hydration/query/index uses, and lower the returned
physical address/path/value decisions. Relationships, change tracking, unit of work, implicit joins, schema migration,
and universal LINQ translation are not added.

**Public concepts:** `EntityMapBuilder<TEntity>` owns the compact declaration grammar; mapping path, physical path,
binding, codec, identity, descriptor, exception, use, and receipt types are provider-neutral; `IDataMappingPlans` is
the host-owned provider seam; `MappingPlan` is the single compiled semantic owner. The Relational Family contributes
relational value, command, schema, materialization, and mapped-filter plans; adapters contribute dialect lowering and
execution only.

**Docs read:**

- `docs/engineering/index.md` redirects to the current contributor workbooks; relevant as the required entry.
- `docs/architecture/principles.md` requires business-intent APIs, compile-once execution, semantic honesty, one
  current path, and one owner; directly governing.
- `docs/architecture/data-adapter-development-primer.md` §§1.3–1.4 and A/E/P rows define the exact four shapes,
  identity, codec, lifecycle, consumer-equality, and hot-path contract; normative.
- `docs/decisions/DATA-0110-compact-data-adapter-language.md` freezes `Container`/`Key`/`Property`/`Name`/`Path`/
  `Object` and the behavior modifiers; normative.
- `evidence/framework/public-contract.md` and `consumer-contract.cs` freeze the compile-level surface and ownership;
  normative acceptance fixtures.
- DAC-07 and the framework scorecard identify A-07, E-01–E-15, and P-02–P-04/P-06 as the executable correction.

**Code read:**

- `SourceIntegration/Composition/DataSourceBuilder.cs` is the exact source-selected declaration owner; `Map` belongs
  beside `Query` and `Scalar` and returns the same builder for one-source chaining.
- `DataOperationCatalog` and `KoanCompositionScope` demonstrate host-owned, duplicate-rejecting declarations. A
  separate mapping declaration catalog is required because operation names and entity map identity have different
  keys and freeze rules.
- `Filtering/FieldPath*` already owns strict logical CLR path resolution. Mapping can reuse its canonical logical path
  facts while keeping physical path vocabulary independent.
- `ProjectionResolver`/`ProjectedProperty` are shallow, process-static scalar metadata. They remain a quarantined
  compatibility surface for adapters awaiting their own replacement card; they are not used by the new Family path.
- `RelationalSchemaOrchestrator` currently derives shape and indexes from `ProjectionResolver`; its new plan overloads
  must consume `MappingPlan`, compare definitions, and enforce the External lifecycle ceiling.
- `SqlFilterTranslator` currently accepts a caller-owned column resolver. A mapped overload must resolve logical paths
  from `MappingPlan` and pass a physical path plus shared codec encoding to the dialect.
- `RelationalCommandCache` is an unbounded process-static string cache; new mapping/command plans are host-owned and
  bounded, so it is not reused.
- Relational abstractions already isolate dialect and DDL contracts. They are extended only with inert plan-lowering
  vocabulary; connection mode, SDK types, DDL syntax, and native error codes remain adapter-owned.

**Reusing:** `KoanCompositionScope`, `StorageAddress`, `FieldPath`/`FieldPathResolver`, source `StorageLifecycle`,
`DataSourcePlan.Demand`, the source-integration host composition pattern, immutable record conventions, existing
relational DDL/dialect seams, and standard expression compilation. Existing Data Core constants/options are extended
for the mapping-plan bound rather than adding literal limits.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| mapping descriptors, paths, codecs, receipts, and typed failures | `src/Koan.Data.Abstractions/Mapping/**` | Providers and Families need inert, provider-neutral meaning without Data Core orchestration. |
| fluent map builders and declaration catalog | `src/Koan.Data.Core/Mapping/Composition/**` and existing `DataSourceBuilder` | Source-selected application grammar and duplicate rejection are Framework composition law. |
| compiler, bounded host plan service, accessors, hydration/write/read-use plans | `src/Koan.Data.Core/Mapping/Runtime/**` | Reflection, conflict checks, structured values, and consumer equality compile once per host. |
| mapping options/constants and service registration | existing Data Core option/constants/registration owners | Cache bounds and host lifetime remain explicit and centralized. |
| relational symbolic binding/command/materialization plans and mapped translator | `src/Koan.Data.Relational/**` | Repeated relational mechanics belong to the Family; emitted plans contain no provider SQL. |
| inert relational lowering contracts and definition-complete schema vocabulary | `src/Koan.Data.Relational.Abstractions/**` | Adapters need narrow physical-path/dialect/DDL inputs without inheriting runtime state. |
| mapping/Family oracles and native spies | `tests/Suites/Data/Relational/**` | Four shapes, every consumer, isolation, bounds, lifecycle, and lowering can be proved without a connector. |

**Coalescence:** The closest declaration pattern is Source Integration composition and is reused for host ownership.
The closest logical member resolver is `FieldPathResolver` and is reused for canonical paths. Shallow projection
metadata is not widened: the mapping compiler replaces it as semantic owner, and new Relational Family paths accept
only `MappingPlan`. The existing generic schema surface may retain a compatibility wrapper while legacy adapters are
still compiled, but that wrapper must compile a plan and delegate; it cannot maintain independent mapping logic.
Provider-specific SQL repositories and Npgsql/SQLite implementation source are not inputs and no connector code is
moved into the Family.

**Ergonomics:** The ordinary declaration reads as model selection followed by physical location. An expression already
names the logical property, so only `Name`, `Path`, or `Object` is offered next. Container and source are stated once.
Advanced identity and codec choices remain discoverable on their binding, while adapters receive one small compiled
plan rather than a parallel builder API. Agents can trace a value by stable plan/binding identifiers from declaration
through command and receipt without learning a provider.

**Constraints satisfied:**

- No connector production path is read as design input or written by DAC-07.
- No relationship, unit-of-work, change-tracking, join, or universal-query surface is introduced.
- Mapping and runtime caches are host-scoped, positively bounded, immutable after freeze, and reused warm.
- External shape mutation is rejected before DDL dispatch.
- Selective projection/index/TTL optimization is claim-gated and never inferred from metadata alone.
- One public top-level type per file; feature folders hold the new surface.

**Risks:** Existing adapters still call compatibility schema/projection surfaces until their own rewrite/evaluation
cards. Removing those symbols now would violate DAC-07's no-connector-write boundary and break unrelated packages.
They are therefore marked as legacy and excluded from the new Family authority. A native provider must prove exact
structured-path and index lowering; the Framework cannot announce those profiles merely because a map compiles.
