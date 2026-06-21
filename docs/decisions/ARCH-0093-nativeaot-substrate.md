# ARCH-0093 — NativeAOT substrate: static discovery roots, hand-rolled relational access, serialization canon

- Status: Accepted
- Date: 2026-06-21
- Deciders: framework architect
- Related: ARCH-0084 (capability model / Reference=Intent), ARCH-0086 (KoanModule + source-gen discovery), the P1.1 composition lockfile, the `X-aot-substrate` single-file work (`8531bef6`), `S2.Sovereign-proof`

## Context

The sovereign-floor mission (P5.1) is "every capability satisfied by an **in-process** resource, in a single deployable with no container runtime." The single-file (JIT) leg shipped first (`8531bef6`): boot discovers Reference=Intent connectors that single-file bundling hides via an embedded `koan.modules.manifest` + `Assembly.Load`.

**NativeAOT is a different mechanism, not a tuning of single-file.** Under ILC there is no `Assembly.Load`, no `.deps.json`, and one statically-linked native image. The `S2.Sovereign-proof` spike published `samples/guides/g1c2.GardenCoopEmbedded` (SQLite data + sqlite-vec vectors + in-process ONNX embeddings + Channels messaging + Web) with `-p:PublishAot=true` and chased the failures empirically. Four were real and each has a clean, framework-level fix; this ADR records them as canon.

## Decisions

### 1. Static discovery roots — a generated trim-root descriptor

ILC starts reachability at the entry point. A Reference=Intent connector is referenced by `<ProjectReference>` but never symbol-used, so ILC trims the whole assembly and its source-generated `[ModuleInitializer]` (`KoanRegistryModule_X`, ARCH-0086) never runs — boot discovers no adapters. Even an assembly kept by a static edge loses members it only constructs reflectively: a registrar is reached via `Activator.CreateInstance(Type)`, whose parameterless `.ctor` ILC drops (`MissingMethodException` at boot).

**`build/Sylin.Koan.Core.targets` now emits `obj/koan.trimroots.xml`** — an ILLink descriptor rooting every Koan module with `preserve="all"`, from the **same `@(ReferencePath)` Koan-filter** that already drives the composition lockfile and the single-file manifest. One module list, three build outputs: lockfile (drift), manifest (single-file discovery), root-descriptor (AOT inclusion). Whole-assembly preservation is deliberate: Koan discovery is reflection-deep (`Activator.CreateInstance` on registrars, `GetTypes()` scans), so member-level trimming would silently strip what reflection needs. The descriptor is only emitted for trimming/AOT publishes.

Empirically confirmed: `[ModuleInitializer]`s **do** fire under AOT for kept assemblies — so rooting the assembly is both necessary and sufficient for discovery.

### 2. AOT is opt-in and does not perturb the normal build

`PublishAot` is set **locally** in the app csproj behind a `-p:KoanAot=true` flag, never as a global CLI property. A global `PublishAot` flows to the netstandard2.0 Roslyn generator `<ProjectReference>`s and trips `NETSDK1207`; a csproj-local property does not propagate, so the generators build normally and the regular solution build (and CI) is untouched.

Windows toolchain note: publish inside the VC developer environment (`vcvars64`) with `-p:IlcUseEnvironmentalTools=true`. ILC's stock `findvcvarsall.bat` captures the nested `vcvarsall` stderr ("`'vswhere.exe' is not recognized`") into the tools-dir variable and corrupts the linker path; `IlcUseEnvironmentalTools` skips that probe and uses the ambient `PATH`/`LIB`/`INCLUDE`.

### 3. Hand-roll the AOT adapters; put Dapper behind a thin shim for the non-AOT ones

Dapper's `GetTypeDeserializerImpl` (and its anonymous-parameter generator) emit IL at runtime — `PlatformNotSupportedException: Dynamic code generation is not supported` on the first SQLite query under AOT. Koan entities persist as a single `(Id, Json)` row, so Dapper's mapping is thin and replaceable.

- **`Koan.Data.Relational.Ado`** (in the relational base, Dapper-free) — `SqlParameters` (ordered named params with Dapper-style IN-expansion, bound through `DbCommand.CreateParameter`) + `AdoCommands` (raw-ADO read/exec/scalar/rows). AOT-targeted relational adapters use these.
- **`Koan.Data.Relational.Dapper`** (new thin package) — `DapperCommands`, the Dapper-backed twin of the same surface and the same `SqlParameters` model, for non-AOT relational adapters (Postgres, SQL Server — servers that never ship inside a single binary) that benefit from Dapper.

The **SQLite adapter is migrated off Dapper** to `AdoCommands` (Dapper `<PackageReference>` removed). This is the user's framing — "hand-roll for any AOT adapter, a little Dapper shim for the non-AOT ones" — and a net dependency removal from the most-used embedded data adapter. Postgres/SqlServer keep Dapper today and may adopt the shim later (a follow-up, not AOT-blocking).

### 4. Serialization canon on the AOT data path is Newtonsoft; no DLR `dynamic`

`System.Text.Json`'s reflection serializer is disabled by default under NativeAOT (`InvalidOperationException: Reflection-based serialization has been disabled`). The framework's canonical serializer is already Newtonsoft (which falls back to late-bound reflection under AOT, no IL emit). The sqlite-vec connector's vector-metadata and stored-vector JSON moved from `System.Text.Json` to Newtonsoft — an AOT fix and a canon-consistency fix. Likewise the `EmbeddingPolicy.FullJson` embedding text (`EmbeddingMetadata.SerializeToJson`, with its property-exclusion resolver re-expressed as a Newtonsoft `DefaultContractResolver`) and the `EmbeddingMigrator` export moved to Newtonsoft — so the embedded JSON matches the entity's persisted `(Id, Json)` form, and the only entity serializer is the canonical one. (The low-level `System.Text.Json` DOM — `JsonDocument`/`Utf8JsonWriter`, used for FullJson depth-truncation — is retained; it is the reflection *serializer* that AOT disables, not the reader/writer.)

The SQLite fallback-create path's `((dynamic)ddl).CreateTableWithColumns(...)` used DLR dispatch, which AOT cannot bind ("`'object' does not contain a definition for...`"); `ddl` is statically a `SqliteDdlExecutor`, so the `dynamic` was gratuitous and is replaced with a direct call.

## Consequences

`g1c2.GardenCoopEmbedded` NativeAOT-publishes on **both win-x64 (~42 MB) and linux-x64 (~40 MB, real Debian 13)** to a single native binary and runs the **whole stack end-to-end**: query embedded by the local ONNX model → sqlite-vec k-NN → SQLite read (Newtonsoft) → MVC JSON response — no container, no servers. Both RIDs return byte-identical semantic-search scores (ONNX inference is deterministic cross-platform), which also validates the framework changes are RID/OS-agnostic (different libc, loader, and linker — MSVC `link.exe` vs `clang`). The native dependencies are AOT-compatible in practice: ONNX Runtime (P/Invoke), the sqlite-vec `vec0` loadable extension, and `e_sqlite3`.

Verification: SQLite connector 3/3 (the full filter-convergence corpus), Data.Core 158/158, Jobs-SQLite 76/76, Bootstrap ARCH-0079 38/38, full-solution build green, and the AOT binary's live semantic search on both win-x64 and linux-x64.

### Deferred (tracked as follow-ups, not on the proven floor's path)

- **`linux-arm64`** — the appliance/edge RID. win-x64 and linux-x64 prove the substrate is RID-agnostic; arm64 is the same recipe on an arm64 host (`-r linux-arm64`). Cross-compiling from x64 needs the aarch64 cross toolchain or a `buildx`/arm64 builder — a packaging step, not a framework one.
- **`X-sqlite-fallback-create-generic`** — the pre-existing `ensure: fallback-create-failed … Value cannot be null (Parameter 'key')` warning on the generic-entity fallback-create path (the primary path recovers).
