---
type: EVIDENCE
domain: data
title: "DAC-06 exploration — Source Integration, RecordSet, and registered reads"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: pre-implementation placement and ergonomics record
---

# DAC-06 exploration — Source Integration, RecordSet, and registered reads

**Task:** Implement the provider-neutral source-only inspection, bounded record, DTO projection, and registered-read
contract without requiring or simulating an Entity repository.

**Application intent:** “Let me safely see what this source contains, keep one useful read under a business name, and
consume faithful bounded records without learning its provider API.”

**Public expression:**

```csharp
builder.Services.AddKoan(koan =>
{
    koan.Data.Source("LegacyErp").Query(
        "orders.recent",
        query => query
            .Lane("Reports")
            .Sql("select ORDER_NO as OrderId where CREATED_UTC >= @since")
            .Parameter<DateTimeOffset>("since")
            .MaxRecords(500)
            .MaxBytes(4 * 1024 * 1024));
});

var source = Data.Source("LegacyErp");
var page = await source.Inspect().Containers(100, null, ct);
RecordSet recent = await source.Query("orders.recent", new { since }, ct);
IReadOnlyList<RecentOrder> typed = recent.Project<RecentOrder>();
```

The application references Data Core plus the selected connector/family binding package, declares the named source
in ordinary configuration, and runs inside one composed Koan host. `Sql` is a relational binding leaf and remains a
Family implementation seam; the common builders expose only the narrow native-binding protocol required for such
extensions.

**Guarantee/correction:** A registered read has one immutable source, name, effective `Read` decision, lane, binding,
typed parameter plan, timeout, and positive bounds. Runtime parameters are exact. Neutral records preserve shape,
order, duplicate names, missing/null, provider type names, nested values, completion, and deterministic accounting.
Unsupported inspection, unknown/opaque execution without a proved lane, ambiguous address/name, wrong-source
reference/continuation, additional result channel, scalar cardinality mismatch, invalid neutral value, or exceeded
non-partial scalar bound fails with a typed correction before unsafe execution or public return. No dispatched
operation is replayed.

**Complete intent surface:** Add the relevant connector/binding reference; configure the source and any provider-
enforced read lane; declare each operation once inside `AddKoan(koan => ...)`; call `Data.Source(name)` for inspection,
query, or scalar; optionally project a `RecordSet` to a DTO. There is no Entity, repository, cursor, connection override,
runtime lane selection, REST/MCP exposure, or mapping prerequisite.

**Public concepts:** `KoanApplicationBuilder` exists solely as the neutral composition callback root;
`DataCompositionBuilder`/`DataSourceBuilder` select source and operation; `DataSource` is the runtime handle;
`StorageAddress`/reference/descriptor/page express neutral topology; `RecordSet` and its field/record/value/limit/
completion types preserve data and bounds; `OperationPlan` and binding/parameter types carry the provider contract;
provider/family binding leaves add native payload decisions without changing application runtime syntax.

**Docs read:**

- `docs/engineering/index.md` redirects to the current contributor workbooks; relevant only as the required entry.
- `docs/architecture/principles.md` requires business-intent APIs, one owner, compile-once plans, semantic honesty,
  standard .NET substrate, and one current path; directly governing.
- `docs/architecture/data-adapter-development-primer.md` §§1–4 and D/F/P rows define the exact experience, closed
  value algebra, accounting, source-only profile, and ownership; normative.
- `docs/decisions/DATA-0110-compact-data-adapter-language.md` freezes `Source`, `Query`, `Scalar`, `Lane`, compact
  binding leaves, and no repeated context; normative.
- `evidence/framework/public-contract.md` plus `consumer-contract.cs` freeze the compile-level public roots and
  observable semantics; normative acceptance fixture.
- `docs/toc.yml` and repository `README.md` establish the current documentation path and Entity-first default; Source
  Integration is the deliberate external-system workflow exception.

**Code read:**

- `Koan.Core/ServiceCollectionExtensions.cs` owns composition and currently has only parameterless and zero-argument
  callbacks; it is the correct owner for the missing neutral application builder overload.
- `Koan.Core/Composition/KoanCompositionScope.cs` already supplies the flow-local host owner used by terse pillar
  builders; reuse it without exposing `IServiceCollection` on the public builder.
- `Koan.Data.Core/Lifecycle/EntityLifecycleBuilder.cs` is the closest host-composed builder pattern: declaration syntax
  resolves the active composition and registers one host-owned plan.
- `Koan.Data.Core/DataSourceRegistry.cs` and `Sources/DataSourcePlan.cs` already own immutable source policy, redacted
  route identity, lane identities, and first-effect demand; reuse unchanged as the source ceiling.
- `Koan.Data.Core/Direct/DirectSession.cs` is an expert SQL/`DbConnection` path with dictionary→JSON DTO conversion;
  relevant as mechanics to absorb into the shared ordinal projector, not as Source Integration architecture.
- `IDataAdapterFactory`/`DataProviderCatalog` are Entity-persistence selection only. A separate
  `IDataSourceIntegrationFactory : IAdapterFactory` is required so source-only connectors do not fake repository
  creation.

**Reusing:** `KoanCompositionScope`, `DataSourceRegistry`, `DataSourcePlan`, `DataReadLanePlan`, `IAdapterFactory`
identity, `AppHost` failure semantics, Data failure taxonomy, runtime fact recorder, `DirectOptions` defaults as an
input only, and existing source-route diagnostics. Existing constants cover source policy and are extended rather
than duplicated.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| `KoanApplicationBuilder` and typed `AddKoan` overload | `src/Koan.Core/KoanApplicationBuilder.cs`; existing Core `ServiceCollectionExtensions.cs` | The neutral composition root is Core law and cannot belong to Data. |
| Record/value/limit/completion contracts and typed failures | `src/Koan.Data.Abstractions/Records/**` | Providers and Data share inert neutral vocabulary; no Core runtime dependency. |
| inspection address/reference/descriptor/page contracts | `src/Koan.Data.Abstractions/SourceIntegration/Inspection/**` | Common source topology must not carry relational/document family words. |
| operation plan, binding, parameter, execution, reader, factory seams | `src/Koan.Data.Abstractions/SourceIntegration/Operations/**` | Source-only adapters need a typed contract independent of `IDataRepository`. |
| composition builders/catalog | `src/Koan.Data.Core/SourceIntegration/Composition/**` | Data owns compact declaration grammar, duplicate rejection, and immutable plans. |
| source runtime/inspector/executor/materializer | `src/Koan.Data.Core/SourceIntegration/Execution/**` | Data owns policy, selection, validation, bounds, accounting, projection, and receipts. |
| `Data.Source` non-generic root | `src/Koan.Data.Core/Data.SourceIntegration.cs` | Exact compact runtime expression beside, not inside, generic Entity Data. |
| source-integration options/constants/facts | existing Data Core options and `Infrastructure/Constants.cs` | Tunable bounds and stable diagnostics stay with their project owners. |
| fake source-only provider and D/F tests | `tests/Suites/Data/Core/Koan.Tests.Data.Core/Specs/SourceIntegration/**` | Proves flat/hierarchical/ambiguous/bounded/fault paths without provider production changes. |

**Coalescence:** Closest pattern: `EntityLifecycleBuilder` for composition ownership and `DirectSession` for current
record mechanics. Lifecycle builder is kept as evidence of host-scoped declaration. Direct's reflection→dictionary→
JSON path is absorbed by the one ordinal `RecordSet` projector where applicable; it is not generalized as the new
source architecture. Specificity is Framework Data law for grammar/materialization/policy and a source-only adapter
seam for native execution. `IDataRepository` is too narrow and would force an Entity shim; generic Koan Core is too
wide for record semantics. Superseded dictionary→JSON DTO conversion and silent duplicate-name overwrite are deleted
from applicable Direct paths.

**Ergonomics:** The common journey has three concepts visible in order—source, business-named operation, result.
Inspection reuses Source and neutral storage terms. IntelliSense after `Query` shows only lane, parameters, bounds,
timeout, and connector binding leaves; runtime calls cannot choose the binding or lane. The code model is legible to
agents because effect/result/delivery are fixed by the entry verb and every native choice is an explicit final leaf.

**Constraints satisfied:**

- No HTTP endpoint work.
- Entity-first remains the ordinary persistence API; Source Integration is explicitly source-centered and has no
  Entity repository shim.
- Stable identifiers extend Data constants; bounds use typed options and immutable effective plans.
- Every buffered path is positively bounded; no public cursor or hidden full materialization.
- README/TECHNICAL, the work card, and verification evidence are updated with the implementation.
- One public top-level type per file; feature folders hold the new source files.

**Risks:** The ratified `AddKoan(Action<KoanApplicationBuilder>)` root is absent and DAC-06's original allowlist omitted
Koan.Core. Implementing it anywhere else would violate the ratified owner, so the card is amended for exactly one new
Core type and the existing overload owner. Provider/family binding leaves and real read-lane enforcement cannot be
proved in Framework-only code; fake bindings prove the seam now, and gold/fleet cards supply native bindings. The
closed algebra deliberately rejects vendor objects instead of widening `object?`.
