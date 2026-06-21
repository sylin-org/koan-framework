# Publishing a Koan app with NativeAOT

A Koan app can publish to a single self-contained **NativeAOT** binary — no .NET runtime, no
container, no servers — when every capability it uses is satisfied by an in-process resource
(SQLite data, sqlite-vec vectors, ONNX embeddings, Channels messaging, Web/MCP). This is the
"sovereign floor" footprint. The framework wiring for AOT is decided in
[ARCH-0093](../decisions/ARCH-0093-nativeaot-substrate.md); this guide is the operational recipe.

> Reference sample: [`samples/guides/g1c2.GardenCoopEmbedded`](../../samples/guides/g1c2.GardenCoopEmbedded) —
> one binary that embeds → stores → semantically searches produce, entirely in-process.

## TL;DR

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
- **Native deps that work under AOT:** ONNX Runtime (P/Invoke), the sqlite-vec `vec0` loadable
  extension (embedded + self-extracted), and `e_sqlite3`.

## 5. Known gaps (avoid or work around)

- **`[Embedding]` without a `Template`** serializes the whole entity via `System.Text.Json` with a
  custom resolver — not yet AOT-ready. Set an explicit `[Embedding(Template = "...")]`.
- **`linux-arm64`** is the appliance/edge RID. Building on an arm64 host is straightforward (same
  steps, `-r linux-arm64`); cross-compiling from x64 needs the aarch64 cross toolchain or a
  `buildx`/arm64 builder.

## 6. Verify it ran

The boot report's `Registry`/`Inventory` blocks list every discovered module — if the trim roots
worked you'll see all of them (not a truncated set). Then exercise a real path end-to-end; for the
reference sample:

```bash
./GardenCoopEmbedded &
curl "http://127.0.0.1:5000/api/produce/search?q=sweet%20red%20fruit&k=3"
# -> 200, semantically-ranked hits, all in one process
```
