# ARCH-0093 — NativeAOT substrate: static discovery roots, hand-rolled relational access, serialization canon

- Status: Accepted
- Date: 2026-06-21
- Deciders: framework architect
- Related: ARCH-0084 (capability model / Reference=Intent), ARCH-0086 (KoanModule + source-gen discovery), the P1.1 composition lockfile, the `X-aot-substrate` single-file work (`8531bef6`), `S2.Sovereign-proof`

> **Implementation update (PMC-050, 2026-08-21) — the proof is machine-re-run now.**
> `scripts/aot-verify.ps1` publishes this sample under ILC and *runs* the binary, and
> `.github/workflows/aot-verify.yml` runs the two container-free cells daily. It is a scheduled lane
> rather than a leg of `scripts/green-ratchet.ps1` deliberately: the ratchet is a manual certification
> boundary, and manual is the status this ADR's own proof had while it was decaying. Publish-and-run
> rather than an ILC compile is also deliberate and measured — reintroducing the `MetadataToken` defect
> produces a *successful publish* and a binary that dies on the first entity it maps, so a compile-only
> gate would have gone green on the exact regression that invalidated this document. Each of the three
> defects listed below has a cell proven against it, red then green. `docs/SURFACES.md` records which
> cells run daily and which need Docker.

> **Implementation update (PMC-049, 2026-08-21) — the substrate reaches the server adapters, and
> §3's Dapper split is superseded.** Every relational backend now NativeAOT-publishes and runs against a
> real store, measured rather than inferred: SQLite, MySQL 8.4 (`MySqlConnector` 2.6.1), PostgreSQL 17 and
> CockroachDB v24.3 (`Npgsql` 10.0.3), and SQL Server 2022 (`Microsoft.Data.SqlClient` 7.0.2), each writing
> and reading one entity through `Entity<T>` with the row confirmed in the container afterwards.
> `samples/fundamentals/AotRelational` is the reproduction and `docs/guides/nativeaot-howto.md` the recipe.
> §3's "hand-roll for AOT adapters, a Dapper shim for the others" no longer describes the tree: PMC-047
> found that every Dapper call site in the three server adapters was untyped or scalar — the compiled
> materializer AOT forbids was never invoked — so Dapper and `Koan.Data.Relational.Dapper` are gone and all
> four adapters execute through `Koan.Data.Relational/Ado`. The decision §3 recorded (do not emit IL on the
> relational path) stands; the two-tier mechanism it chose is retired.
>
> **One provider constraint, and it belongs to the driver rather than to AOT.**
> `Microsoft.Data.SqlClient` refuses globalization-invariant mode outright —
> `System.NotSupportedException: Globalization Invariant Mode is not supported`, thrown from
> `SqlConnection.TryOpen`. A SQL Server application therefore publishes with
> `InvariantGlobalization=false` and carries culture data; the other four backends do not need it. The
> refusal is unconditional driver policy, so it applies equally to a JIT build in invariant mode and no
> amount of AOT work removes it. This qualifies §4's blanket "set `InvariantGlobalization=true`".
>
> **Three framework defects sat on the AOT path and had to be repaired to get this proof**, all in
> `Koan.Core` or `Koan.Data.Core`, none in a provider:
> - `WriteKoanReferenceManifest` wrote `koan.references.manifest` into the RID-specific intermediate
>   directory without creating it, so the *first* `-r <rid>` publish of any Koan application failed with
>   `DirectoryNotFoundException`. Only the first: a second publish succeeded on the directory the failed
>   one left behind, which is why it had gone unnoticed.
> - `MemberInfo.MetadataToken` — used at four sites to recover declaration order — does not exist under
>   ILC and throws. The first entity mapped died with
>   `MappingCompilationException: There is no metadata token available for the given member`. The four
>   sites now share `Koan.Core.Reflection.DeclarationOrder`, which returns the token where the runtime has
>   one and otherwise a constant, leaving LINQ's stable ordering to preserve reflection order. Behaviour on
>   CoreCLR is unchanged.
> - `AppBootstrapper.AddAsm` called `Assembly.GetName()` on every assembly. That materializes the culture,
>   which a globalization-invariant process cannot construct, so the eleven satellite resource assemblies
>   SqlClient ships aborted discovery with `CultureNotFoundException`. Satellites carry no code and are
>   never Koan modules; they are now skipped on the same lenient terms as an unresolvable reference. **This
>   presented as a SqlClient failure and was Koan's.**
>
> **The `MetadataToken` defect had also broken SQLite, which this ADR certified working.** The mapping
> compiler landed 2026-08-06, three weeks after the 2026-07-17 proof below, and nothing re-published. The
> single-binary claim was therefore false for the embedded floor as well as unproven for the servers, and
> only running a publish revealed it — a standing argument for re-measuring a certified capability rather
> than inheriting it.

> **Implementation update (R10-01, 2026-07-17):** GardenCoop revalidated the win-x64 native path
> and exposed two documentation/runtime gaps. `KoanFactJson` now uses its own source-generated
> serialization context, so the public facts endpoint remains available when reflection serialization
> is disabled. Public guidance now distinguishes a self-contained native deployment directory from a
> physical single-file claim; static assets and native connector libraries may remain beside the executable.

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

The SQLite fallback-create path's `((dynamic)ddl).CreateTableWithColumns(...)` used DLR dispatch, which the `Microsoft.CSharp` runtime binder cannot perform under AOT; `ddl` is statically a `SqliteDdlExecutor`, so the `dynamic` was gratuitous and is replaced with a direct call. A captured AOT stack (mutation-confirmed) showed this surfaces two ways: a generic argument throws the expected `RuntimeBinderException` ("'object' does not contain a definition for…"), while a non-generic one throws an *opaque* `ArgumentNullException("key")` from the binder's own `ExpressionTreeCallRewriter` → `Dictionary.TryInsert` — so `grep "(dynamic)"`, not the error text, is the reliable AOT tripwire.

## Consequences

`g1c2.GardenCoopEmbedded` NativeAOT-publishes on **both win-x64 (~42 MB) and linux-x64 (~40 MB, real Debian 13)** to a single native binary and runs the **whole stack end-to-end**: query embedded by the local ONNX model → sqlite-vec k-NN → SQLite read (Newtonsoft) → MVC JSON response — no container, no servers. Both RIDs return byte-identical semantic-search scores (ONNX inference is deterministic cross-platform), which also validates the framework changes are RID/OS-agnostic (different libc, loader, and linker — MSVC `link.exe` vs `clang`). The native dependencies are AOT-compatible in practice: ONNX Runtime (P/Invoke), the sqlite-vec `vec0` loadable extension, and `e_sqlite3`.

Verification: SQLite connector 3/3 (the full filter-convergence corpus), Data.Core 158/158, Jobs-SQLite 76/76, Bootstrap ARCH-0079 38/38, full-solution build green, and the AOT binary's live semantic search on both win-x64 and linux-x64.

### Deferred (tracked as follow-ups, not on the proven floor's path)

- **`linux-arm64`** — the appliance/edge RID. win-x64 and linux-x64 prove the substrate is RID-agnostic; arm64 is the same recipe on an arm64 host (`-r linux-arm64`). Cross-compiling from x64 needs the aarch64 cross toolchain or a `buildx`/arm64 builder — a packaging step, not a framework one.
