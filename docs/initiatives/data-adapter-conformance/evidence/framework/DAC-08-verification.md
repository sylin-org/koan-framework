---
type: EVIDENCE
domain: data
title: "DAC-08 verification — Diagnostics, claims, lifecycle, and performance"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: passed
  scope: framework-owned diagnostics, claims, resource ownership, scenarios, and benchmark grammar
---

# DAC-08 verification — Diagnostics, claims, lifecycle, and performance

## Result

PASS for the Framework boundary. `Data.Source(name).Describe()` and `.Explain(operation)` are pure projections of the
same frozen route, policy, mapping, operation, descriptor, and claim decisions used by execution. `.Doctor(ct)` is an
explicit bounded non-mutating probe. Source integrations activate lazily, are never created by pure diagnostics, and
dispose with their host.

`DataClaimSet` is the one executable claim declaration. Its deterministic references feed source descriptions,
explanations, runtime diagnostics, composition facts, health, TestKit applicability, and packet construction without
another capability map. Public failures contain only Framework-owned code/kind/commit/retry/replay/correction and an
opaque restricted-evidence reference. Native type/code/target evidence is write-only and bounded inside the host.

Every mutable declaration, readiness state, source integration, mapping/write/name plan, diagnostic ledger, and
native-evidence store is host-owned and bounded. Pure type metadata uses weak type keys and finite per-type contents.
The shared TestKit names all eight fault/lifecycle modules and records cold/warm elapsed time, allocation, provider
dispatch count, and provider work against an explicitly pinned fixture. It defines no cross-provider threshold.

## Application surface

```csharp
var source = Data.Source("LegacyErp");
var description = source.Describe();
var explanation = source.Explain("orders.recent");
var diagnosis = await source.Doctor(ct);
```

`Describe` and `Explain` perform no provider activation or I/O. `Doctor` is active only when the inert provider
descriptor declares the seam; it never provisions or executes a business operation.

## Executable evidence

| Evidence | Result |
|---|---|
| Focused Core diagnostics/policy/readiness/ownership matrix | 140/140 passing |
| Pure warm diagnostics | 100 repeated Describe/Explain pairs retain exact decision/claim identities with zero provider activations |
| Claim identity | exact references agree across runtime descriptions, facts, health, `DataConformanceManifest`, and packet/TestKit projection |
| Doctor and failure boundary | caller cancellation and Framework timeout remain distinct; Doctor dispatches only documented checks; restricted evidence records exact type/code without exception/message fields |
| Host ownership | two-host catalog/name isolation, bounded readiness/diagnostic/native-evidence/name caches, exact supplied-provider cache selection, lazy integration disposal, and weak structural caches pass |
| Fault/lifecycle TestKit | all eight standard modules exist, reference stable catalog IDs, and state live/restart/two-host/minimum-operation requirements |
| Benchmark TestKit | observation captures pinned provider/provider-version/driver/runner, cold/warm phase, elapsed, allocation, dispatch, and provider work; reflection guard proves no global threshold surface |
| Testing protocol | 33 passing; four intentional trait/environment packet skips |
| Relational placement regression | 16/16 passing |
| Data Axis host-owned declaration regression | 56/56 passing |
| Standard host lifecycle | 5/5 passing with Windows Event Log disabled for the restricted runner |
| Responsibility placement | canonical one-page map names one Framework/Family/Adapter owner and explicit failure shape for every concern |
| Static-state/security review | no mutable process-static host decision; public diagnostics contain no connection material, parameters, business values, tenant value, raw native message, or exception object |
| Solution build | restore-free build passes with zero warnings and zero errors |
| Diff hygiene | `git diff --check` passes; repository line-ending notices only |

Commands:

```powershell
dotnet test tests/Suites/Data/Core/Koan.Tests.Data.Core/Koan.Tests.Data.Core.csproj --no-build --no-restore --filter "<DAC-08 focused classes>"
dotnet test tests/Suites/Testing/Koan.Testing.Tests/Koan.Testing.Tests.csproj --no-build --no-restore
dotnet test tests/Suites/Data/Relational/Koan.Data.Relational.Tests/Koan.Data.Relational.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~MappingConformanceSpec|FullyQualifiedName~RelationalOwnershipSpec"
dotnet test tests/Suites/Data/Axes/Koan.Data.Axes.Tests/Koan.Data.Axes.Tests.csproj --no-build --no-restore
$env:Logging__EventLog__LogLevel__Default='None'; dotnet test tests/Suites/Data/Core/Koan.Tests.Data.Core/Koan.Tests.Data.Core.csproj --no-build --no-restore --filter "FullyQualifiedName~StartKoanHostOwnershipSpec"
dotnet build Koan.sln --no-restore
```

## Broad legacy-adapter classification

The broad Data Core diagnostic run produced 463 PASS / 28 RED before the final focused corrections. Two REDs were
host-cache/test-selection assumptions corrected in this card; four were restricted-runner Windows Event Log access
and pass 5/5 with that provider disabled. The remaining 22 are strict handled-filter/count receipt rejections in
current JSON, SQLite, InMemory/vector, transfer, and cross-provider paths. They are Adapter REDs exposed by DAC-05's
honest receipt boundary, not reasons to weaken Core or add compatibility inference. They remain assigned to the
ground-up gold/fleet cards. DAC-08 changed no connector production behavior.

## Ownership proof

- `DataClaimSet` plus `DataCapabilityProfiles` is the sole Data claim/capability-to-profile owner.
- `DataSourceDiagnosticsService` compiles Describe/Explain from frozen plans and owns the Doctor boundary.
- `DataSourceIntegrationService` owns lazy activation and host disposal; integrations own native clients only after activation.
- `DataDiagnostics`, `DataSourceReadinessCoordinator`, `DataNativeEvidenceStore`, `StorageNameCache`,
  `StorageWritePlanCache`, and mapping catalogs are bounded host services.
- Terse registry facades resolve the current composition/host; they retain no process-global application declaration.
- `DataScenarioCatalog` owns reusable scenario applicability; provider fixtures own mechanics and native receipts.
- `DataBenchmarkRunner` owns observation grammar only; a pinned provider fixture owns its expectations.
- The canonical [responsibility map](../../../../architecture/data-adapter-responsibility-map.md) is the P-06 review
  authority for Framework, Family, and Adapter placement.

## Primer-row disposition

| Rows | DAC-08 disposition |
|---|---|
| G-01–G-02 | PASS for distinct bounded host readiness stages, single-flight behavior, detached caller cancellation, failure eviction, and authorized provision/post-validation. Native shape races remain Adapter LIVE proofs. |
| G-03–G-04 | PASS for standard saturation/cancellation modules and Framework timeout/cancellation/resource boundaries. Each provider still supplies live pool/native-release receipts. |
| G-05–G-09 | PASS for exact shared result/failure/scenario grammar and fail-closed claim selection. Atomicity, CAS, durability, soak, and isolation remain claim-relative Adapter LIVE evidence. |
| H-01–H-02 | PASS: pure diagnostics match frozen route/policy/mapping/operation/claims and repeated warm calls perform zero provider activation. |
| H-03 | PASS for the active non-mutating Doctor contract, timeout, cancellation, stable findings, and corrections; provider-native checks remain Adapter evidence. |
| H-04–H-05 | PASS for identical decision/claim references and restricted redacted public projections. |
| H-06 | PASS for exact native type/code evidence, Framework failure vocabulary, and timeout/cancellation/replay separation; provider classifiers remain Adapter proofs. |
| P-01–P-04 | PASS structurally for frozen decisions, compiled/weak/bounded host caches, exact supplied-host resolution, and receipt-enforced honesty. Provider-relative work remains certification evidence. |
| P-05 | PASS for complete pinned observation grammar with no global threshold; gold/fleet fixtures supply comparable baselines. |
| P-06 | PASS through the canonical one-page responsibility map and duplicate-machinery scan. |

No provider claim or live behavior is certified by a fake. Missing provider receipts remain RED or pending on their
adapter cards.
