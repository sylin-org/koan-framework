---
type: REFERENCE
domain: operations
title: "NativeAOT deployment boundary"
audience: [developers, operators, architects, ai-agents]
status: current
last_updated: 2026-08-22
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-21
  status: verified
  scope: win-x64 publish and run of AotRelational against SQLite, PostgreSQL, CockroachDB, MySQL and SQL Server; re-run nightly by aot-verify
---

# NativeAOT deployment boundary

A Koan application can publish as a **NativeAOT deployment** — a native executable with no installed
.NET runtime — when every capability it uses is AOT-compatible. The deployment is a directory: the
executable travels with application assets and any connector-native libraries.

This sits outside the 1.x compatibility guarantee, and it is measured rather than assumed.

## What is proven

[AotRelational](../../../samples/fundamentals/AotRelational/) writes and reads one entity through the
ordinary `Entity<T>` surface, and publishes to a native **win-x64** executable that runs against
**SQLite, PostgreSQL, CockroachDB, MySQL and SQL Server**. The application code is identical across
all five; the connector reference and a connection string decide which store answers.

`.github/workflows/aot-verify.yml` republishes and re-runs that sample nightly, so the claim decays
loudly rather than quietly.

## What it costs

`Microsoft.Data.SqlClient` refuses globalization-invariant mode, so a build that references the SQL
Server connector carries ICU culture data and is correspondingly larger. The other four connectors
publish invariant.

A connector's ordinary support status says nothing about whether it is trim- or AOT-safe. Those are
separate properties, and only the five above have been measured.

## What is not measured

Only **win-x64**. Linux x64 and linux-arm64 — the appliance case — have not been published or run,
so nothing is claimed for them. The cross-compilation toolchain differs enough that the win-x64
result does not carry over by inference.

## Publishing

[Publishing a Koan app with NativeAOT](../../guides/nativeaot-howto.md) is the operational recipe:
the pinned toolchain, the per-platform linker, and the diagnostic commands for reading an ILC
failure. The framework wiring behind it is decided in
[ARCH-0093](../../decisions/ARCH-0093-nativeaot-substrate.md).

For a smaller artifact without the native step, self-contained single-file publication remains
available:

```powershell
dotnet publish App.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

Application composition and Reference = Intent behave identically under every publication mode.
