---
type: SPEC
domain: data
title: "Ratified Koan.Data Compact Public Contract"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: DAC-02 exact public API and observable semantics
---

# Ratified Koan.Data compact public contract

This is the compile-level projection of DATA-0110 and primer §§1–4. It freezes names and observable semantics; it does
not prescribe provider class graphs.

## Smallest source journey

```csharp
builder.Services.AddKoan(koan =>
{
    koan.Data.Source("LegacyErp")
        .Query("orders.recent", query => query
            .Lane("Reports")
            .Sql("select ... where CREATED_UTC >= @since")
            .Parameter<DateTimeOffset>("since")
            .MaxRecords(500)
            .MaxBytes(4 * 1024 * 1024));

    koan.Data.Source("LegacyErp")
        .Map<Customer>(map => map
            .Container("dbo", "CUSTOMER")
            .Key(customer => customer.Id).Name("CUSTOMER_NO")
            .Property(customer => customer.Name.Full).Name("DISPLAY_NM")
            .Property(customer => customer.Profile).Object("PROFILE_JSON"));
});

var source = Data.Source("LegacyErp");
RecordSet result = await source.Query("orders.recent", new { since }, ct);
var customer = result.Project<RecentOrder>();
```

The application makes each decision once. Runtime calls use the registered stable name and parameters; callers cannot
select a native binding, connection override, or read lane.

## Exact public roots

| Public type/member | Contract |
|---|---|
| `AddKoan(Action<KoanApplicationBuilder>)` | enters the one host-owned composition and freezes it after the callback |
| `KoanApplicationBuilder.Data` | returns the Data composition root |
| `DataCompositionBuilder.Source(string)` | declares or selects exactly one named source plan; duplicate declarations reject |
| `Data.Source(string)` | resolves one immutable runtime source handle from the current Koan host |
| `DataSourceBuilder.Query(string, Action<RecordQueryBuilder>)` | declares `Read + Records + Buffered` |
| `DataSourceBuilder.Scalar<T>(string, Action<ScalarQueryBuilder>)` | declares `Read + Scalar + Buffered` |
| `DataSourceBuilder.Map<TEntity>(Action<EntityMapBuilder<TEntity>>)` | declares one aggregate-to-record map for this source |
| `DataSource.Query(string, object?, CancellationToken)` | validates parameters/policy and returns one bounded `RecordSet` |
| `DataSource.Scalar<T>(string, object?, CancellationToken)` | returns exactly one convertible provider scalar |
| `DataSource.Inspect()` | returns the provider-neutral source inspector |

`KoanApplicationBuilder` is the neutral composition root owned by Koan Core. Koan.Data.Core contributes `.Data` as a
C# 14 extension property, so Core never references Data and another pillar can contribute a peer property without a
parallel application-builder hierarchy. The parameterless and `Action` forms of `AddKoan` remain valid. The
one-argument callback overload is additive.

## Source policy and precedence

```text
Koan:Data:Sources:{source}:Adapter
Koan:Data:Sources:{source}:ConnectionString
Koan:Data:Sources:{source}:StorageLifecycle
Koan:Data:Sources:{source}:Access
Koan:Data:Sources:{source}:ReadLanes:{lane}:ConnectionString
Koan:Data:Sources:{source}:ReadLanes:{lane}:ProviderMode
```

`StorageLifecycle` is `Managed | External`; default is `Managed`. `Access` is `ReadWrite | ReadOnly`; default is
`ReadWrite`. They are independent. Configuration establishes defaults; an explicit composition declaration may narrow
authority but may not widen a provider-enforced or host-policy restriction. Conflicting equal-precedence declarations
reject. A registered opaque read requires a named lane whose connection or provider mode is proved read-only at
composition/active validation. Lane selection is declaration-only and frozen into the operation plan.

One Framework effect gate receives the effective source plan before lifecycle callbacks, readiness/provisioning,
transaction creation, or provider dispatch. It covers Entity, batch, transaction, transfer, Direct/connection override,
instruction, patch, conditional write, registered operation, and provider-extension paths. `Unknown` never executes as
a registered read. `External` blocks storage-lifecycle mutation, not mapped data writes. `ReadOnly` blocks every write.

## Provider-neutral inspection

```csharp
IDataSourceInspector Inspect();
Task<StorageContainerPage> Containers(int take, string? continuation, CancellationToken ct);
Task<StorageContainerReference> Resolve(StorageAddress address, CancellationToken ct);
Task<StorageContainerDescriptor> Describe(StorageContainerReference reference, CancellationToken ct);
Task<RecordSet> Sample(StorageContainerReference reference, int take, CancellationToken ct);
```

`StorageAddress` is an ordered source-relative namespace path plus local name. `StorageContainerReference` is opaque and
source-bound. `StorageContainerDescriptor` exposes provider kind, intrinsic traits, effective operations, and optional
record shape. Listing completion is `Complete | MoreAvailable | ProviderLimit`; continuations are opaque/source-bound.
The common API never assumes schemas or tables. Rich native topology stays in an explicit provider extension.

## Neutral result contract

The exact regular buffered types are the primer's `DataField`, `DataProperty`, `DataObject`, `DataArray`, `DataRecord`,
`RecordSetLimits`, `RecordSetCompletion`, `RecordSetByteAccounting`, `RecordSetExecution`, and `RecordSet`.

- Fields and records preserve order; field names may duplicate.
- Ordinal lookup is lossless. Name lookup succeeds only for exactly one matching field.
- Missing is a record presence bit and differs from null.
- Neutral values are the primer's closed scalar/temporal/binary/object/array algebra.
- Every result has positive record, total-byte, value-byte, and materialization-duration limits.
- `MaterializedValueV1` accounting occurs during the one native-to-neutral conversion pass.
- An additional result channel rejects; it is never discarded.
- DTO projection uses one cached ordinal plan for constructors/records or writable properties, without per-row JSON.

## Registered operation contract

`OperationEffect`, `OperationResultKind`, and `OperationDelivery` remain independent in immutable `OperationPlan`.
`Query` fixes `Read + Records + Buffered`; `Scalar<T>` fixes `Read + Scalar + Buffered`. Builders expose `Lane`,
`Parameter<T>`, `MaxRecords`, `MaxBytes`, `MaxValueBytes`, and `Timeout`. Provider/family packages add binding leaves:
`Sql`, `Pipeline`, `Template`, and `Function`. The selected binding, parameter plan, lane, effect, result, delivery, and
bounds are frozen at composition. Duplicate `(source, name)` operations reject.

Registered operations are uncached, not automatically exposed over REST/MCP, and not replayed after possible provider
dispatch. A missing provider-managed artifact fails diagnostically and is never auto-created for an External source.

## Mapping contract

`EntityMapBuilder<TEntity>` exposes `Container(params string[])`, `Container(StorageAddress)`, `Key`, root `Object`, and
`Property`. Binding builders expose `Name`, `Path`, `Object`, `Generated`, `ReadOnly`, and `Codec`. Composite identity uses
`Key(...).Parts(parts => parts.Property(...).Name(...))`.

Compilation produces one immutable, host-scoped `MappingPlan` with one writable authority per logical value. The same
plan drives hydration, writes, filters, sorts, patches, compare-and-set, projections, and indexes. Whole-object, flat,
hybrid, nested-path, generated/composite key, codec, and read-only bindings are variants of this one model. Relationship
loading, change tracking, unit of work, implicit joins, and universal LINQ translation are outside the contract.

## Explainability and atomicity

`Describe`, `Explain`, execution receipts, facts, health, telemetry, and public errors project the same immutable plan
identities. Public diagnostics exclude credentials, parameters, business values, tenant/entity identifiers, and raw
native messages. Native failures are classified by exact driver type/code and target.

Atomicity is claimed only for one proved native transaction/batch boundary. Cross-adapter coordination is explicitly
non-atomic unless a distributed protocol is implemented and proved. A handled/optimized receipt may not conceal a
full-source scan, client page, inert index, or sequential pseudo-batch.

## Ownership and concept cost

The public concept set is Source, policy, inspection, RecordSet, mapping, operation, plan/receipt, and Entity. Framework
owns grammar, policy, plans, materialization, bounds, failure taxonomy, and truth projection. A Family owns repeated
native mechanics. An Adapter owns native translation, resource lifetime, dispatch, and exact native failure mapping.
Vector shares Source Core. The ratified Vector annex owns normalized similarity, execution truth, visibility, lifecycle,
and V-01 through V-24; current provider behavior remains evidence rather than authority.

Every moving part must own a necessary contract guarantee, remove repeated adapter mechanics with identical meaning
and lifetime, or measurably improve a hot path. Existing scaffolding has no compatibility entitlement: absorb, rebuild,
or delete it when it creates duplicate ownership, speculative extension points, warm-path discovery, or dead branches.
