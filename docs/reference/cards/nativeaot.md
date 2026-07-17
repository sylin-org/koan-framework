---
type: REF
domain: orchestration
title: "NativeAOT — sovereign native-deployment map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: source-first
validation:
  date_last_tested: 2026-07-17
  status: verified
  scope: GardenCoop win-x64 native executable, business API, lifecycle result, and runtime facts
---

# NativeAOT — sovereign native-deployment map

> One-screen map of the sovereign floor — publishing a self-contained native Koan deployment (no .NET runtime, container, or external server required). The floor of the deployment-footprint ladder. Full detail: [nativeaot-howto.md](../../guides/nativeaot-howto.md) · decision: [ARCH-0093](../../decisions/ARCH-0093-nativeaot-substrate.md).

**What it does** — An AOT-compatible Koan application publishes as a native executable that boots without an installed .NET runtime. The deployment directory may also contain application assets and native connector libraries; NativeAOT does not imply one physical file. Reference = Intent still selects adapters, and the build emits `obj/koan.trimroots.xml` to keep referenced Koan modules reachable. The current public sample proves the full GardenCoop Chapter 1 result on **win-x64**; other RIDs and native connectors remain separate claims until freshly exercised.

## The one canonical pattern

Gate AOT behind `-p:KoanAot=true` (set `PublishAot` **locally** — a global property trips the netstandard2.0 generators with `NETSDK1207`), then publish for a RID. Root your own serialized types so Newtonsoft can reflect over them.

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

## The sample that shows it

[`GardenCoop Chapter 1`](../../../samples/journeys/GardenCoop/01-GardenJournal/) — one native application deployment serving its dashboard, SQLite-backed garden API, lifecycle automation, and composition facts with no external service.
