---
type: REFERENCE
domain: framework
title: "R02 Capability Baseline Evidence"
audience: [architects, maintainers, ai-agents]
status: current
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: tested
  scope: focused build, test, package-install, and public-claim probes at 4471e9c7
---

# R02 capability baseline evidence

This is the reproducible execution record behind [`CAPABILITIES.md`](CAPABILITIES.md). It records
what was run and what the result proves; it is not a release certification. The assessed snapshot is
commit `4471e9c7ffeaa2cd198a62589a9763c4555d9b7f` on Windows with the .NET 10 SDK.

## Focused execution results

All commands ran from a clean repository checkout in Release configuration. `--no-restore` means the
repository's previously restored dependency graph was reused; each invocation still built its project
graph unless stated otherwise.

| Surface | Command / probe | Result | Interpretation |
|---|---|---|---|
| Shortest repository path | `dotnet build samples/S1.Web/S1.Web.csproj -c Release --nologo` | PASS; 0 errors, 10 warnings, 76.1 seconds | The source-checkout application builds, but a cold source build is not honestly a 60-second guarantee on this machine. |
| Bootstrap | `dotnet test tests/Suites/Integration/Bootstrap/Koan.Tests.Integration.Bootstrap/Koan.Tests.Integration.Bootstrap.csproj -c Release --no-restore --nologo` | DID NOT COMPLETE; stopped after 304 seconds with no test output | Discovery and reporting code exist, but this invocation cannot certify the focused bootstrap suite. |
| Entity/data core | `dotnet test tests/Suites/Data/Core/Koan.Tests.Data.Core/Koan.Tests.Data.Core.csproj -c Release --no-restore --nologo` | PASS; 283/283 | Core entity and repository semantics have substantial automated coverage. This does not certify every external adapter. |
| Web/API | `dotnet test tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.InMemory.Tests/Koan.Web.AdapterSurface.InMemory.Tests.csproj -c Release --no-restore --nologo` | PASS; 70/70 | The in-memory web adapter contract is verified. External providers were not exercised here. |
| Jobs | `dotnet test tests/Suites/Jobs/Koan.Jobs.Tests/Koan.Jobs.Tests.csproj -c Release --no-restore --nologo` | PASS; 74/74 | In-process jobs semantics are verified; distributed transports and every durable ledger are not. |
| Cache | `dotnet test tests/Suites/Cache/CrossEngine/Koan.Tests.Cache.CrossEngine/Koan.Tests.Cache.CrossEngine.csproj -c Release --no-restore --nologo` | PASS; 14/14 | The tested cross-engine cache contract agrees for its configured engines. This is not a fleet-wide production certification. |
| AI unit behavior | `dotnet test tests/Suites/AI/Unit/Koan.Tests.AI.Unit/Koan.Tests.AI.Unit.csproj -c Release --no-restore --nologo` | PASS; 152/152 | Focused AI abstractions and policies are verified in isolation. |
| AI/data integration | `dotnet test tests/Suites/Data/AI/Koan.Data.AI.Tests/Koan.Data.AI.Tests.csproj -c Release --no-restore --nologo` | FAIL; 78 passed, 1 failed | `VectorModelGuardIntegrationSpecs.Registry_round_trips_the_full_model_lifecycle` throws `ObjectDisposedException: IServiceProvider` through `Data<T>.Repo`. Host-lifecycle safety is not verified. |
| Vector adapter | `dotnet test tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.InMemory.Tests/Koan.Data.VectorAdapterSurface.InMemory.Tests.csproj -c Release --no-restore --nologo` | PASS; 33/33 | The in-memory vector adapter contract is verified. External vector engines were not exercised. |
| MCP | `dotnet test tests/Suites/Mcp/Koan.Mcp.Conformance.Tests/Koan.Mcp.Conformance.Tests.csproj -c Release --no-restore --nologo` | PASS; 72/72 | The conformance suite verifies the core MCP contract. Explorer, all transports, and production authorization configurations remain separate surfaces. |
| Identity | `dotnet test tests/Suites/Integration/Identity/Koan.Identity.Tests/Koan.Identity.Tests.csproj -c Release --no-restore --nologo` | PASS; 113/113 | Core identity behavior is verified. Current OIDC code uses the ASP.NET handler path; real external providers were not exercised. |
| Testing kit | `dotnet test tests/Suites/Testing/Koan.Testing.Tests/Koan.Testing.Tests.csproj -c Release --no-restore --nologo` | PASS with exclusions; 10 passed, 3 skipped | The conformance harness works for the tested path; embedding and cache conformance cases were skipped. |
| Observability | `dotnet test tests/Suites/Observability/Koan.Observability.Tests/Koan.Observability.Tests.csproj -c Release --no-restore --nologo` | PASS; 1/1 | The package has executable evidence, but one test is too thin for an operations support claim. |

Build warnings included unresolved XML documentation references, nullable analysis, an obsolete cache
compatibility shim, an unread logger, and unreachable controller code. They do not invalidate the
focused results, but they weaken the claim that the foundation is already polished.

## Clean package-install probe

A disposable application was created outside the repository with `dotnet new web` and exact package
references to `Sylin.Koan.Core`, `Sylin.Koan.Web`, and
`Sylin.Koan.Data.Connector.Sqlite` version `0.17.0`. Restore failed because:

```text
Sylin.Koan.Web 0.17.0
  -> Sylin.Koan.Data.Abstractions 0.17.0
  -> Sylin.Koan.Core (>= 0.17.3 && < 0.18.0)
```

NuGet currently exposes `Sylin.Koan.Core` 0.17.0, not 0.17.3, so the package graph produces NU1605
and cannot restore as a coherent application. The same probe reported NU1903 for
`SQLitePCLRaw.lib.e_sqlite3` 2.1.11. The disposable probe directory was removed after the result was
recorded.

Consequences:

- source checkout is the only demonstrated installation path at this snapshot;
- the package-first quickstart is presently unavailable, not merely lagging;
- package version coherence and the transitive advisory belong at the top of R04;
- no capability is promoted to `supported-foundation` or `supported-extension` while the public
  package graph is incoherent.

## Public-claim audit

| Claim family | Classification | Current evidence-safe wording |
|---|---|---|
| Koan is a .NET meta-framework centered on `Entity<T>` and reference-as-intent composition. | supported | Product direction and implementation structure agree; capability-specific maturity still varies. |
| A source checkout reaches a meaningful CRUD application in one command. | supported | `samples/S1.Web` builds and is the shortest demonstrated repository path. Do not promise a universal elapsed time. |
| Adding current public packages is a working new-project path. | overstated | The 0.17.0 package set is currently version-incoherent; retain the intended recipe as unavailable until fixed. |
| The boot report tells developers exactly what Koan did. | overstated | Koan reports discovered composition and elections on a best-effort basis; entity inventory is lazy and some reporting failures are intentionally non-fatal. |
| Every adapter/provider listed in the README is covered by a current convergence oracle. | overstated | Core and selected in-memory/cross-engine paths are verified; the full external-provider fleet was not certified by R02. |
| Unsupported negotiated operations fail loudly. | supported for the capability contract | `CapabilityNotSupportedException` and capability sets encode this behavior; provider-specific completeness remains test-scoped. |
| Jobs, cache, identity, and MCP are only prototypes. | understated | Each has focused passing suites and useful implemented paths, although packaging prevents a supported release claim. |
| AI and semantic behavior are foundation-ready. | overstated | Unit and in-memory vector evidence is strong, but a current lifecycle integration test fails and external engines are unassessed. |
| Current OIDC still returns a placeholder 501. | obsolete | Current code and tests use maintained ASP.NET authentication handlers; old assessment prose must not be reused as current truth. |
| Composition is source-generated with no runtime reflection. | obsolete/overstated | Source generation is preferred; manifests, assembly discovery, and runtime fallbacks remain intentional deployment paths. |

## Evidence limits

R02 did not run container-backed databases, brokers, vector engines, distributed job transports,
external identity providers, upgrade/migration scenarios, package publishing, or a full solution test
pass. A project, adapter, sample, or ADR proves existence or intent—not current compatibility. Private
downstream experience informed the questions only and is not evidence in this record.

## Ranked disposition and evidence-gap backlog

This ranking is the handoff to R03/R04. It does not authorize implementation ahead of the semantic
contract.

| Rank | Disposition | Surface | Required proof or correction |
|---|---|---|---|
| P0 | repair/simplify | Packaging | Publish one atomic, version-coherent, advisory-reviewed package set; prove restore, build, run, CRUD, and package metadata from an external clean room. |
| P0 | repair/simplify | Host lifecycle | Reproduce and fix the disposed-service-provider AI failure; prove repeated host create/dispose cycles and remove global-host leakage from integration paths. |
| P0 | repair/simplify | Bootstrap testability | Diagnose the silent five-minute bootstrap run; establish a bounded fail-fast lane covering discovery, ordering, lenient mode, and structured explanation. |
| P1 | keep/harden | Entity/data | Ratify the R03 Entity Semantics Contract, then turn the verified core into the first supported foundation with provider-scoped compatibility evidence. |
| P1 | keep/harden | Web, jobs, cache, MCP, identity | Preserve the strong focused cores; define package, failure, security, and provider boundaries before support promotion. |
| P1 | repair/simplify | Explanation/operations | Make one structured composition and negotiation model feed startup, health, lockfile, agent resources, and review output; test redaction and degraded states. |
| P1 | repair/simplify | Provider verification | Publish generated evidence matrices and bounded container lanes; never derive provider support from project existence. |
| P1 | repair/simplify | Testing kit | Explain skips, separate fast/reference/container lanes, and prove an external application can inherit useful conformance from packages. |
| P2 | keep/harden | Messaging | Add a provider-neutral contract for delivery, retry, topology, and failure; certify in-memory and RabbitMQ independently. |
| P2 | incubate | AI/vector/semantic | Keep APIs available for learning, but with experimental labeling until lifecycle, model negotiation, durability, and external-engine lanes pass. |
| P2 | repair/simplify | Documentation | Retire or clearly date stale assessments and sample catalogs; generate front-door maturity/provider statements from this ledger where practical. |
| P3 | incubate/archive | Unsupported breadth | Archive planned-only samples and claims that have no owner or executable path; do not expand breadth until lower foundations graduate. |

The practical product sequence is therefore package/host/bootstrap reliability, then the Entity
semantic contract and shared explanation model, then provider and extension graduation. This protects
the promised experience: meaningful small steps whose hidden complexity remains inspectable rather
than merely invisible.
