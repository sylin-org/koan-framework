---
type: REF
domain: orchestration
title: "NativeAOT — sovereign single-binary map"
audience: [developers, ai-agents]
status: current
last_updated: 2026-06-21
framework_version: v0.17.0
validation:
  date_last_tested: 2026-06-21
  status: verified
  scope: docs/reference/cards/nativeaot.md
---

# NativeAOT — sovereign single-binary map

> One-screen map of the sovereign floor — publishing a Koan app to one self-contained native binary (no .NET runtime, no container, no servers). The floor of the deployment-footprint ladder. Full detail: [nativeaot-howto.md](../../guides/nativeaot-howto.md) · decision: [ARCH-0093](../../decisions/ARCH-0093-nativeaot-substrate.md).

**What it does** — When every capability an app uses is satisfied by an **in-process** resource (SQLite data, sqlite-vec vectors, ONNX embeddings, Channels messaging, Web/MCP), the app publishes to a single NativeAOT binary that boots the whole stack with nothing else installed. Reference = Intent still selects the adapters; the build does the AOT-specific wiring **for you**: referencing `Sylin.Koan.Core` emits `obj/koan.trimroots.xml` (an ILLink descriptor rooting every Koan module `preserve="all"`, off the same `@(ReferencePath)` filter as the composition lockfile and single-file manifest). That is mandatory under AOT — ILC starts reachability at your entry point, so a never-symbol-used Reference=Intent connector would otherwise be trimmed and its source-gen `[ModuleInitializer]` would never run (boot would discover no adapters). Proven on **win-x64 and linux-x64** with byte-identical results. Verified once, this stays falsifiable: the boot report lists every discovered module.

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

[`samples/guides/g1c2.GardenCoopEmbedded`](../../../samples/guides/g1c2.GardenCoopEmbedded) — one binary that embeds → stores → semantically searches produce, entirely in-process: `curl "…/api/produce/search?q=sweet red fruit"` → `200`, ranked hits, no container, no servers.
