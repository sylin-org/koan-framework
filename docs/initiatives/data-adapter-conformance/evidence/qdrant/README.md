---
type: REFERENCE
domain: data
title: "Qdrant Conformance Packet"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: behavior-green-strict-deferred
  scope: live Qdrant vector adapter evidence
---
# Qdrant conformance packet

Status: behavioral PASS; strict packet DEFERRED.

The empty-root adapter passes all 28 live Vector and isolation cells against `qdrant/qdrant:v1.18.3` with zero skips.
The solution builds with zero warnings and errors, and the shared Data Vector, InMemory Vector, and SqliteVec regression
suites are green. Strict Forge observes every Qdrant row as passed, then defers because the versioned
`conformance.json` artifact has not been generated.

This directory does not claim a strict certificate. `claims.json`, `evidence.json`, and `dependencies.json` remain
pending until the shared packet-generation control plane can produce and validate the missing artifact.

Reproduce the behavioral result:

```powershell
dotnet test tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.Qdrant.Tests/Koan.Data.VectorAdapterSurface.Qdrant.Tests.csproj --no-restore --nologo --verbosity:minimal
pwsh -NoProfile -File scripts/forge-verify.ps1 -Adapter Qdrant -Plane vector -Strict -NoBuild -Output table -DeadlineSeconds 120
```

See [surfaces.md](./surfaces.md), [probes.md](./probes.md), and [remediation.md](./remediation.md) for the bounded claim
and implementation record.
