---
type: ENGINEERING
domain: data
title: "DAC-03 Executable Conformance Exploration"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: reviewed
  scope: mandatory pre-implementation exploration for the Data conformance control plane
---

# DAC-03 executable conformance exploration

**Task:** Turn the primer's 81 stable Data acceptance IDs into one executable, claim-driven, deterministic
conformance protocol without changing adapter semantics.

**Application intent:** An adapter author states what the adapter guarantees once; Koan selects the exact obligations,
runs them, and explains why the adapter is green, red, deferred, or unsupported.

**Public expression:** In an xUnit project referencing `Sylin.Koan.Testing`, the adapter declares one manifest and
inherits one executable base:

```csharp
public sealed class SqliteConformance : DataAdapterConformanceSpecs
{
    protected override DataConformanceManifest Manifest =>
        DataConformanceManifest.For("sqlite", claims => claims
            .Observe(DataConformanceProfiles.EntityPersistence)
            .From(DataCaps.Describe(Repository)));

    protected override DataConformancePacket Packet => BuildPacket(Manifest);
}
```

Target and declined profiles are explicit on the same builder. Provider/version/fixture/source identity and evidence
are supplied to the packet; `forge-verify.ps1 -Strict` is the repository action that validates and aggregates it.

**Guarantee/correction:** Each selected claim expands mechanically to the primer IDs and conjunctive evidence kinds.
Unknown profiles/IDs, duplicate rows, unresolved evidence, stale fingerprints, missing expected tests, skipped LIVE,
and false advertised claims fail with a stable classification. A declined profile must carry corrective negative
evidence; it cannot become a silent green skip.

**Complete intent surface:** Reference `Sylin.Koan.Testing`; declare the adapter manifest; provide pinned fixture and
evidence inputs; inherit the executable base; run Forge strict mode. There is no provider registration API, second
capability table, or application runtime configuration for conformance.

**Public concepts:** `DataConformanceManifest` expresses claims; `DataConformanceProfiles` supplies discoverable stable
profile names; `DataConformancePacket` carries reproducible evidence and dependencies; `DataAdapterConformanceSpecs`
executes the catalog. All are test-author concepts in `Koan.Testing`, not application runtime concepts.

**Docs read:**

- `docs/architecture/data-adapter-development-primer.md` is the sole 81-ID/profile/evidence authority and is directly
  relevant.
- `docs/architecture/principles.md` requires one business decision, compile-once plans, thin hot paths, and one current
  truth; directly relevant.
- `docs/initiatives/data-adapter-conformance/work-items/DAC-03-executable-conformance-control-plane.md` fixes scope,
  mutations, and exit conditions; directly relevant.
- `docs/architecture/adapter-forge.md` establishes capability-driven contribution and real-provider proof but contains
  older broad-matrix assumptions; useful strategy, not current authority.
- `docs/toc.yml` confirms architecture navigation; only the primer/initiative additions need focused lint and no new
  public tutorial entry.

**Code read:**

- `src/Koan.Data.Abstractions/Capabilities/DataCaps.cs` is the existing runtime capability owner and must be kept.
- `tests/Suites/_shared/CapabilityConformanceGate.cs` is the closest claim-to-action pattern. Keep its one test-only
  source link-compiled into both TestKits: concrete adapter executables otherwise acquire a fragile transitive runtime
  dependency for a few xUnit dispatch branches. The common semantic protocol becomes authoritative in `Koan.Testing`.
- `tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/AodbConformanceSpecsBase.cs` supplies real-host
  isolation/stream cases and is mapped to stable primer IDs rather than replaced.
- `src/Koan.Testing/EntityConformanceSpecs.cs` is the closest compact inherited-test grammar and the correct package
  owner for the new executable base.
- `scripts/forge-verify.ps1` is the current process orchestrator; its substring discovery caused the recorded extra-cell
  bug and must be rebuilt around exact spec identity plus strict protocol validation.
- `tools/Koan.Packaging/Services/ProductSurfaceCompiler.cs` demonstrates deterministic generated truth and strict
  duplicate/path validation; its projection pattern is reused, not its product-specific schema.

**Reusing:** Existing `Capability`, `CapabilitySet`, `DataCaps`, xUnit inheritance, real `AddKoan()` fixtures, TRX
execution, product-surface generation conventions, and the primer itself already exist. A complete base catalog,
claim manifest, packet compiler/validator, impact query, strict exit taxonomy, and exact test mapping need creation.
No new options type is needed: Forge controls are explicit command parameters and packet identities are required data,
not ambient configuration. Stable schema/resource/trait/exit identifiers need centralized constants.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| generated catalog resource and loader | `src/Koan.Testing/Conformance/` | test-only executable projection of the primer; no runtime adapter dependency |
| `DataConformanceProfiles` | `src/Koan.Testing/Conformance/DataConformanceProfiles.cs` | IntelliSense names for the finite profile grammar |
| `DataConformanceManifest` | `src/Koan.Testing/Conformance/DataConformanceManifest.cs` | one claim declaration and `DataCaps` projection |
| `DataConformancePacket` | `src/Koan.Testing/Conformance/DataConformancePacket.cs` | deterministic verdict, evidence, dependency, and impact owner |
| `DataAdapterConformanceSpecs` | `src/Koan.Testing/Conformance/DataAdapterConformanceSpecs.cs` | one executable theory surface for all catalog IDs |
| protocol constants | `src/Koan.Testing/Conformance/Infrastructure/DataConformanceConstants.cs` | centralized schema/resource/trait identities |
| protocol meta-tests | `tests/Suites/Testing/Koan.Testing.Tests/` | existing solution-owned tests for the owning package |
| strict orchestration/catalog generation | `scripts/forge-verify.ps1` | preserve the one repository entry point without a second catalog |

**Coalescence:** Closest pattern: `CapabilityConformanceGate.cs`. Its decision owner is TestKit, consumers are record
and Vector AODB bases, lifetime is static test metadata, and its hot-path cost is irrelevant outside tests. Keep one
link-compiled source for its behavioral dispatch; absorb the finite catalog, claim projection, packet validation, and
impact rules into `Koan.Testing`; rebuild Forge discovery/strict aggregation; delete substring-based cell identity.
The semantic protocol owner is `Koan.Testing` because this is shared executable test law—not runtime Data policy and
not provider behavior. Moving the dispatcher there would force every concrete adapter host to resolve that transitive
test assembly; moving it into Core would expose xUnit protocol at runtime.

**Ergonomics:** One fluent manifest reads as claims, not infrastructure. Profiles are discoverable constants; runtime
tokens project through `From(...)`; one inherited base supplies cases; one Forge command certifies. The only cognitive
branches are Observed, Target, and Declined because they change release meaning. Packet plumbing stays generated.

**Constraints satisfied:**

- No HTTP surface or inline endpoint is involved.
- No Entity data operation is added; existing real-host Entity tests remain the behavioral path.
- Stable literals are centralized; no tunable options object is warranted.
- No placeholder test passes: missing evidence is RED/DEFER and skipped LIVE is non-green.
- The primer remains the only semantic catalog; generated artifacts carry its fingerprint.
- No SQLite or MongoDB implementation internals are used.

**Risks:** xUnit inherited-theory naming and TRX traits must be verified empirically; strict Forge must preserve the
legacy non-strict AODB command; Vector-specific semantics remain outside the base catalog until DAC-49 ratifies them;
product maturity claims cannot be auto-promoted by an adapter packet and remain subject to the existing human gate.
