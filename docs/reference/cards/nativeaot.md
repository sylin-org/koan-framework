---
type: REF
domain: orchestration
title: "NativeAOT — experimental deployment boundary"
audience: [developers, ai-agents]
status: current
last_updated: 2026-07-20
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-20
  status: blocked
  scope: GardenCoop win-x64 publish currently stops inside the .NET 10.0.10 ILC analyzer
---

# NativeAOT — experimental deployment boundary

> NativeAOT is an experimental Koan deployment path, not a 0.20 guarantee. With the pinned .NET 10.0.302 SDK and
> 10.0.10 runtime packs, GardenCoop Chapter 1 currently stops inside the ILC analyzer with an
> `IndexOutOfRangeException` before producing an executable. Use self-contained or single-file JIT publication for
> the current candidate. The retained implementation map is documented in
> [nativeaot-howto.md](../../guides/nativeaot-howto.md); [ARCH-0093](../../decisions/ARCH-0093-nativeaot-substrate.md)
> remains the dated substrate decision, not executable proof.

**Intended result** — An AOT-compatible Koan application can publish as a native executable that boots without an
installed .NET runtime. The deployment directory may also contain application assets and native connector libraries;
NativeAOT does not imply one physical file. Reference = Intent still selects adapters, and the build emits
`obj/koan.trimroots.xml` to keep referenced Koan modules reachable. No RID or complete Koan application currently has
a candidate-grade NativeAOT claim.

## Retained diagnostic pattern

This is the implementation shape under investigation, not a promised working candidate recipe. Gate AOT behind
`-p:KoanAot=true` (set `PublishAot` **locally** — a global property trips the netstandard2.0 generators with
`NETSDK1207`), publish for a RID, and root application-owned serialized types.

```xml
<!-- App.csproj -->
<PropertyGroup>
  <InvariantGlobalization>true</InvariantGlobalization>          <!-- floor needs no culture data -->
</PropertyGroup>
<PropertyGroup Condition="'$(KoanAot)' == 'true'">
  <PublishAot>true</PublishAot>
</PropertyGroup>
<ItemGroup>
  <TrimmerRootDescriptor Include="NativeAotRoots.xml" />          <!-- YOUR entity + controller types -->
</ItemGroup>
```

```bash
# Linux (clang is the linker, on PATH):
dotnet publish App.csproj -c Release -r linux-x64 -p:KoanAot=true

# Windows (publish inside the VC dev environment):
#   call "...\VC\Auxiliary\Build\vcvars64.bat"
dotnet publish App.csproj -c Release -r win-x64 -p:KoanAot=true -p:IlcUseEnvironmentalTools=true
```

## ≤5 knobs you'll use

| Knob | What it does |
|---|---|
| `-p:KoanAot=true` | Opt in. Sets `PublishAot` locally so the normal solution build and CI are untouched. |
| `koan.trimroots.xml` (auto) | The framework's discovery roots — emitted for any trim/AOT publish; you never hand-edit it. |
| `NativeAotRoots.xml` (`TrimmerRootDescriptor`) | **You** root your own `[Embedding]`/entity/controller types so Newtonsoft can serialize them. |
| `<InvariantGlobalization>true` | The floor needs no ICU at runtime (the build/SDK box still needs `libicu`). |
| `-p:IlcUseEnvironmentalTools=true` (Windows) | Use the ambient `vcvars64` toolchain; ILC's `findvcvarsall` corrupts the linker path when `vswhere` is off `PATH`. |

**Constraints baked in (so it just works):** serialization is **Newtonsoft** (`System.Text.Json` reflection is AOT-disabled); the **SQLite adapter is Dapper-free** (Dapper emits IL → `PlatformNotSupportedException`) via `Koan.Data.Relational.Ado`; no DLR `dynamic`. Linux prereqs: `clang zlib1g-dev binutils libicu-dev`.

## The escape hatch

No AOT toolchain (or a connector that isn't AOT-clean yet)? Publish **single-file JIT** instead — `-p:PublishSingleFile=true --self-contained` — and Koan still discovers the bundled Reference=Intent connectors via the embedded `koan.modules.manifest` + `Assembly.Load` (the same module list, a different mechanism). Or simply move **up** the footprint ladder: the same app runs unchanged on one Docker host, k8s, or cloud — "sovereign" is the *floor*, not the only rung.

## Current sample boundary

[`GardenCoop Chapter 1`](../../../samples/journeys/GardenCoop/01-GardenJournal/) retains the smallest AOT opt-in and
application-root descriptors so the upstream/compiler boundary remains reproducible. Its ordinary JIT application
path is verified; its native publish is not.
