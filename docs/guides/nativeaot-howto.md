---
type: GUIDE
domain: orchestration
title: "Publishing a Koan app with NativeAOT"
audience: [developers, architects]
status: current
last_updated: 2026-07-20
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-20
  status: blocked
  scope: GardenCoop Chapter 1 win-x64 publish currently stops inside the .NET 10.0.10 ILC analyzer
related_guides:
  - composition-lockfile.md
  - ai-vector-howto.md
---

# Publishing a Koan app with NativeAOT

A Koan app is intended to publish as a self-contained **NativeAOT deployment**—no installed .NET runtime—when
every capability it uses is AOT-compatible. The deployment is a directory: the native executable may
travel with application assets and connector-native libraries. The framework wiring for AOT is decided in
[ARCH-0093](../decisions/ARCH-0093-nativeaot-substrate.md); this guide is the operational recipe.

> **Current boundary:** NativeAOT is experimental and not part of Koan's 0.20 guarantee. With the pinned .NET
> 10.0.302 SDK and 10.0.10 runtime packs, [GardenCoop Chapter 1](../../samples/journeys/GardenCoop/01-GardenJournal/)
> stops inside the ILC analyzer with an `IndexOutOfRangeException` before emitting an executable. The configuration
> below remains a reproducible diagnostic surface. Use self-contained or single-file JIT publication for the current
> candidate.

## Diagnostic commands

```bash
# Linux (x64/arm64) — clang is the linker, found on PATH:
dotnet publish path/to/App.csproj -c Release -r linux-x64 -p:KoanAot=true
```

```cmd
:: Windows (x64) — publish inside the VC dev environment:
call "...\VC\Auxiliary\Build\vcvars64.bat"
dotnet publish path\to\App.csproj -c Release -r win-x64 -p:KoanAot=true -p:IlcUseEnvironmentalTools=true
```

## 1. Opt in (`-p:KoanAot=true`)

The app project gates `PublishAot` behind a `KoanAot` flag so AOT never perturbs the normal
solution build or CI, and never flows `PublishAot` to the netstandard2.0 source generators (a
global `PublishAot` trips `NETSDK1207`). Add to the app `.csproj`:

```xml
<PropertyGroup Condition="'$(KoanAot)' == 'true'">
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

Then publish with `-p:KoanAot=true`. (In-repo dogfood projects also `<Import>` the Koan build
targets explicitly; PackageReference consumers get them automatically.)

## 2. What the framework does for you

Referencing `Sylin.Koan.Core` makes the build emit **`obj/koan.trimroots.xml`** for any trimming/AOT
publish — an ILLink descriptor that roots every Koan module (`preserve="all"`) from the same
`@(ReferencePath)` filter that drives the composition lockfile and the single-file manifest. This is
mandatory under AOT: ILC starts reachability at your entry point, so a Reference=Intent connector
(referenced but never symbol-used) would otherwise be trimmed and its source-generated
`[ModuleInitializer]` would never run — boot would discover no adapters. Whole-assembly preservation
is deliberate because Koan discovery is reflection-deep (registrars are built via
`Activator.CreateInstance`).

You do **not** hand-maintain a trim-roots file for the framework. You only root your **own** entity
and controller types — typically via a small `NativeAotRoots.xml` `TrimmerRootDescriptor` (so
Newtonsoft can serialize them via late-bound reflection):

```xml
<ItemGroup>
  <TrimmerRootDescriptor Include="NativeAotRoots.xml" />
</ItemGroup>
```

```xml
<!-- NativeAotRoots.xml -->
<linker>
  <assembly fullname="MyApp">
    <type fullname="MyApp.MyEntity" preserve="all" />
    <type fullname="MyApp.MyController" preserve="all" />
  </assembly>
</linker>
```

For a single reflection-reached type you can root it inline instead, with
`[DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(MyEntity))]` on a method that runs at
startup — equivalent to a descriptor entry, kept next to the code that needs it.

## 3. Prerequisites

**Linux** (the AOT linker is `clang`):

```bash
sudo apt-get install -y clang zlib1g-dev binutils libicu-dev   # Debian/Ubuntu
```

- `clang` + `zlib1g-dev` — the ILC linker toolchain.
- `binutils` — provides `objcopy` (ILC strips/links symbols with it).
- `libicu-dev` — the .NET **SDK/CLI** needs ICU; without it `dotnet` fails fast on a minimal box.
  (The published app uses `InvariantGlobalization` and does not need ICU at runtime.)

**Windows** (the AOT linker is MSVC `link.exe`):

- Visual Studio (or Build Tools) with the **Desktop development with C++** workload.
- Publish from a **Developer command prompt** (`vcvars64.bat`) and pass
  `-p:IlcUseEnvironmentalTools=true`. ILC's stock toolchain probe (`findvcvarsall.bat`) captures the
  nested `vcvarsall` stderr ("`'vswhere.exe' is not recognized`") into the linker-path variable and
  corrupts it when `vswhere` is off `PATH`; `IlcUseEnvironmentalTools` skips the probe and uses the
  ambient `PATH`/`LIB`/`INCLUDE` that `vcvars64` already set.

## 4. Constraints baked into the floor (so AOT just works)

- **Serialization is Newtonsoft.** `System.Text.Json`'s reflection serializer is disabled by default
  under AOT; Koan's canonical serializer is Newtonsoft, which falls back to late-bound reflection
  (no IL emit). Root your serialized entity types (step 2). MVC responses are serialized by
  Koan.Web's Newtonsoft pipeline.
- **The SQLite adapter is Dapper-free.** Dapper emits IL at runtime (`PlatformNotSupportedException`
  under AOT), so the SQLite read/write path uses the hand-rolled `Koan.Data.Relational.Ado` helpers.
  The server relational adapters (Postgres, SQL Server) keep Dapper — they never ship in a single
  binary; if you somehow AOT one, route it through the same `Koan.Data.Relational.Ado` helpers.
- **No `dynamic`.** The DLR can't bind under AOT. Koan's hot paths avoid it.
- **Native dependencies are unverified for the candidate.** GardenCoop retains `e_sqlite3` packaging intent, but the
  current compiler failure occurs before the application can prove that deployment. Treat every native connector and
  RID as a separate publish-and-run claim.
- **Globalization.** Set `<InvariantGlobalization>true</InvariantGlobalization>` in the app project
  (the floor doesn't need culture data). Otherwise the AOT binary needs ICU present on the target.
  Note the **build/SDK** box still needs `libicu` regardless — `dotnet` itself fails fast without it.

## 4b. Ship the app's content beside the binary

A single binary is still a folder: anything the app reads at runtime must be published next to it. The
SDK auto-includes `appsettings.json` only for the **Web** SDK; a plain `OutputType=Exe` app (and any
side-loaded asset, e.g. an embedding model) needs explicit `CopyToPublishDirectory`:

```xml
<ItemGroup>
  <Content Include="appsettings.json" CopyToPublishDirectory="PreserveNewest" />
  <Content Include="..\..\assets\models\all-MiniLM-L6-v2\model_quantized.onnx"
           Link="models\model_quantized.onnx" CopyToPublishDirectory="PreserveNewest" />
</ItemGroup>
```

Resolve such paths against `AppContext.BaseDirectory` (the exe's directory), never
`Assembly.Location` (empty in a single-file/AOT app).

## 5. Known gaps (avoid or work around)

- **`linux-arm64`** is the appliance/edge RID. Building on an arm64 host is straightforward (same
  steps, `-r linux-arm64`); cross-compiling from x64 needs the aarch64 cross toolchain or a
  `buildx`/arm64 builder.

(All four `[Embedding]` text strategies — `Template`, `Properties`/`AllStrings`, and `FullJson` —
are AOT-clean: text is built by reflection over names + string ops, and `FullJson` uses Newtonsoft.)

## 6. Reproduce and verify after the compiler blocker is removed

The current command is expected to reproduce the ILC analyzer failure. After the pinned toolchain can publish it,
the boot report's `Registry`/`Inventory` blocks must list every discovered module—if the trim roots worked
you'll see all of them. Then exercise a real business path end-to-end; for the current sample:

```powershell
dotnet publish samples/journeys/GardenCoop/01-GardenJournal/GardenCoop.C01.csproj `
  -c Release -r win-x64 --self-contained true -p:KoanAot=true

Set-Location samples/journeys/GardenCoop/01-GardenJournal/bin/Release/net10.0/win-x64/publish
./GardenCoop.C01.exe --urls http://127.0.0.1:5000
# In another terminal: Invoke-RestMethod http://127.0.0.1:5000/api/garden/reminders
```

## 7. Troubleshooting

| Symptom | Cause / fix |
|---|---|
| ILC analyzer throws `IndexOutOfRangeException` | Current .NET 10.0.10 compiler/runtime-pack blocker reproduced by GardenCoop Chapter 1. Do not claim native deployment success; use self-contained or single-file JIT and re-run this proof after the toolchain changes. |
| `NETSDK1207` on a `Koan.*.Generators` project | A **global** `-p:PublishAot=true`. Use the `-p:KoanAot=true` gate (§1) so `PublishAot` is set only on the app. |
| Windows link error mentioning `vswhere` / `link.exe` | Publish inside `vcvars64` with `-p:IlcUseEnvironmentalTools=true` (§3), or install the **Desktop development with C++** workload (MSVC `link.exe`). |
| Boot throws missing-controller/entity `InvalidOperationException` | A reflection-reached type was trimmed — add it to `NativeAotRoots.xml` or `[DynamicDependency]` (§2). Koan modules are auto-rooted; this is for **your** types. |
| `DllNotFoundException: e_sqlite3` | The native library was not published beside the executable. AOT is self-contained by default—do not force `--no-self-contained`; the connector's native bits travel with it. |
| `Reflection-based serialization has been disabled` | Something serialized via `System.Text.Json`. The floor is Newtonsoft (§4); for an MVC app ensure Koan.Web's Newtonsoft pipeline is active. |
| Need a stack trace from the native binary | Publish with `-p:StripSymbols=false` (or `-p:IlcGenerateCompleteDebugInfo=true`) to keep symbols. |

Expect `IL2026`/`IL3050` trim/AOT warnings from ASP.NET Core MVC, Newtonsoft, and the reflection-based
relationship/config paths. Those warnings remain separate from the current fatal ILC analyzer exception.
