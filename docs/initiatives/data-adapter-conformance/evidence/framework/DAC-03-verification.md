---
type: REFERENCE
domain: data
title: "DAC-03 Executable Conformance Control Plane Verification"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: passed
  scope: generated catalog, claim registry, packet protocol, strict Forge, shared TestKits, and solution build
---

# DAC-03 verification

The primer is the only semantic catalog. Forge generated an embedded projection containing exactly 81 cells and 27
profiles, with primer and catalog fingerprints. `Koan.Testing` owns claim projection, deterministic packet compilation,
verdict recomputation, safe evidence references, and dependency impact queries. Forge owns discovery, process execution,
TRX binding, and strict exit classification.

## Reproducible results

| Command | Result |
|---|---|
| `pwsh scripts/forge-verify.ps1 -CatalogOnly` | PASS; 81 cells, 27 profiles; six record and four vector AODB cases bound by exact row key |
| focused `DataConformanceProtocolTests` and Forge boundary test | PASS; 12 passed, one expected no-environment skip |
| complete `Koan.Testing.Tests` | PASS; 25 passed, four existing trait/environment skips |
| focused `CapabilityConformanceGateTests` | PASS; 6/6 |
| record and vector AdapterSurface TestKit builds | PASS; zero warnings/errors |
| Vector InMemory AdapterSurface | PASS; 34/34 |
| Vector SqliteVec AdapterSurface | PASS; 29 passed, five explicit feature skips |
| strict InMemory record Forge with no packet | DEFERRED/exit 2; all six bounded AODB proofs passed; missing packet did not read green |
| initiative integrity plus mutations | PASS; 41 cards, 81 IDs, 22 packet scopes, 15 negative mutations |
| `dotnet build Koan.sln --no-restore --verbosity minimal` | PASS; zero warnings/errors |

## Mechanical guarantees

- Every built-in `DataCaps` token maps to a primer profile with objective cells; reflection detects orphan tokens.
- Every generated cell points to the inherited `DataAdapterConformanceSpecs` verifier; no placeholder pass exists.
- Unknown/duplicate cells, unresolved evidence, stale fingerprints, unsafe artifacts, false advertised claims, and
  skipped live evidence produce distinct non-green outcomes.
- Fixed inputs serialize byte-stably.
- Owner, source, tool, profile, and fixture fingerprint changes invalidate every packet that consumed them while an
  unrelated change does not.
- Strict Forge calls the C# packet validator and classifies its stable marker; it does not reproduce packet semantics.
- The capability dispatcher is one test-only linked source. This avoids a transitive `Koan.Testing` load edge in
  concrete adapter executables; both Vector baselines prove the resulting host graph.
