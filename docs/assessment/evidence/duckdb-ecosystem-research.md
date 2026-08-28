# DuckDB ecosystem research — prior art, adoption, satisfaction, use cases

> **Preserved research** (2026-08-27). Input to [DATA-0123](../../decisions/DATA-0123-embedded-analytics-and-duckdb-connector.md).
> Web-research synthesis across three parallel tracks: adoption/satisfaction, the .NET
> ecosystem, and use-case/scenario fit. All numbers were verified against primary sources on
> 2026-08-27; sources are listed per section. Companion doc:
> [analytics-feature-satisfaction.md](analytics-feature-satisfaction.md) (the feature-class research).

## Part A — Adoption trajectory and user sentiment

**Headline context:** on 2026-08-26 (the day before this research), AWS announced a definitive
agreement to acquire DuckLabs, the company behind DuckDB. DuckDB Labs becomes an AWS subsidiary;
per the official DuckDB blog, "no changes for our projects' roadmap, licensing, and governance
model" — projects stay MIT under the DuckDB Foundation, which is setting up a stakeholder
advisory board. AWS signaled it will push DuckDB into its analytics stack ("fill a gap for
clients" — CEO Matt Garman via CRN).

### Adoption trajectory

- **GitHub (verified via API, 2026-08-27):** 40,730 stars; 3,637 forks; 82,892 commits. Steady
  ~10k stars/year: 20k (Jun 2024) → 25k (Dec 2024) → 30k (Jun 2025) → 40k (Aug 2026). The 40k
  post reports 8M+ unique monthly visitors to duckdb.org (double YoY) and 2 PB+ of
  extension-download traffic.
- **Version cadence:** current stable v1.5.5 (Jul 2026). Since 1.4.0 (Sep 2025 — the first LTS,
  "Andium", EOL Sep 2026), DuckDB alternates LTS and standard lines with monthly-ish patches.
  **v2.0.0 planned for Fall 2026**: DuckDB as a server (Quack protocol), SQL triggers, VARIANT
  type, async I/O, new SQL parser, **new storage format**.
- **Governance:** the DuckDB Foundation (non-profit) holds IP and trademarks for DuckDB,
  DuckLake, and Quack, MIT-licensed "irrevocably." Commercial entities: DuckDB Labs (CWI
  spin-off) and MotherDuck (cloud DuckDB; ~$133M raised, no Series C announced as of Jun 2026 —
  slower cadence than 2022–23; the AWS deal does not include MotherDuck).
- **Extension ecosystem (strong):** httpfs, postgres scanner (read+write), sqlite scanner,
  delta, iceberg (full DML as of 1.5.0), fts, spatial, json, excel, vss (vector), local UI, an
  MCP server, MSSQL extension, and a new ADBC extension (Aug 2026) connecting to 30+ external
  databases. Plus **DuckLake 1.0** (production-ready lakehouse format, Apr 2026) and a signed
  community extension registry.

### Usage signals

- **PyPI:** 66.58M downloads/month (~16.9M/week) per pypistats, 2026-08-27; DuckDB's own 40k
  post says 50M+/month, more than double the 20M of June 2025 (>2x YoY).
- **NuGet:** `DuckDB.NET.Data` ~822K total downloads; latest 1.5.5 published 2026-07-26 — it
  tracks core releases within days. Per-release volume 20–40K for 2025–2026 releases. .NET
  uptake is real but ~2 orders of magnitude smaller than Python.
- **Named embedders/builders:** MotherDuck, Rill (BI), Evidence (BI-as-code), Hex, Deepnote,
  Streamlit, Smallpond (distributed processing on DuckDB+3FS), Arc (time-series warehouse),
  Shaper, Cosmograph, Webduck "DuckDB-as-a-Service" (Aug 2026).

### Satisfaction and sentiment

- **Stack Overflow surveys:** 2024 — usage 1.4%, admired 69.2% (3rd most admired database).
  2025 — usage 3.3% (~2.4x YoY), admired 58.8% (4th, behind PostgreSQL, Valkey, SQLite). High
  admiration, small but fast-growing usage.
- **DB-Engines:** climbed ~55 → ~44 during 2025; trajectory strongly upward into the top-50.
- **Positive themes:** "SQLite for analytics"; querying Parquet/CSV/Iceberg in place with zero
  infrastructure; the local UI (HN 926 points); "DuckDB as the new jq"; "most important
  geospatial software of the last decade" (HN 491 points); practitioner post "Why I dropped
  ClickHouse for DuckDB" (Feb 2026).
- **Negative themes (concrete):**
  - **Single-writer concurrency** — one process may open a file read-write; duckdb/duckdb
    discussion #22288 calls multi-client writes "extremely limiting."
  - **Not for OLTP** — engineering write-ups warn heavy concurrent writes/transactional
    workloads are "the wrong job."
  - **Memory behavior** — OOM surprises in containers; `memory_limit` must be set below
    container limits; DuckDB publishes dedicated memory-management guidance.
  - **Storage format compatibility** — backward compatibility guaranteed since 0.10; **forward
    compatibility unsupported** (newer file in older engine fails); `STORAGE_VERSION` pinning
    available since 1.2.0; v2.0 ships another format — an ongoing embedder tax.
  - **Supply-chain incident** — Sept 2025: DuckDB NPM packages 1.3.3/1.29.2 compromised with
    malware (HN 395 points / 283 comments).
  - **Identity confusion** — recurring threads misreading DuckDB as a Postgres/MySQL
    alternative; MotherDuck concedes "that single-node part scares people the moment you put it
    next to the word production."

### Hype trajectory

No decline signals — the opposite on every external metric. The positioning shift to watch: v2.0
deliberately expands from embedded library toward server territory (Quack, triggers), addressing
the single-writer criticism while creating overlap with Postgres/ClickHouse. Polars is the
common comparison but coverage treats them as complementary. MotherDuck's long-term relationship
with AWS is now an open question.

**Implication recorded at research time:** momentum, MIT license, Foundation-governed stability,
prompt DuckDB.NET releases, and AWS stewardship argue for first-party support; the operational
caveats (single writer, no forward-compatible storage, memory tuning) belong in adapter
documentation and design, not in a decision against adoption.

### Part A sources

- GitHub API — duckdb/duckdb; duckdb.org 40k/30k stars posts; release calendar; FAQ
- duckdb.org — "DuckDB Labs to join AWS" (2026-08-26); AWS Big Data Blog; GeekWire; SiliconAngle; CRN
- "A Preview of DuckDB v2.0" (2026-08-17); The Stack; InfoWorld
- Stack Overflow Developer Survey 2024/2025 (technology); pypistats duckdb; NuGet DuckDB.NET.Data
- duckdb.foundation; CWI/MotherDuck funding releases; MotherDuck "DuckDB outgrows its nest"; forgeglobal funding tracker
- MotherDuck "15+ companies using DuckDB in prod"; awesome-duckdb
- duckdb/duckdb discussion #22288; concurrency docs; memory-management post; storage docs; 1.2.0 announcement; ducklake #550
- Hikmah Technologies "DuckDB in production"; Endjin "DuckDB in depth"; Better Programming; MotherDuck "Self-hosting: road to production"; donaldsimpson.co.uk (Feb 2026); kestra.io embedded-databases guide
- HN (Algolia): local UI 43342712; v2.0 preview 49330781; geospatial 43881468; NPM compromise 45179939; Quack 48111765; new jq 39782356; Smallpond 43200793
- Reddit r/dataengineering ("DuckDB is a weird beast?", "Is someone using DuckDB in prod?")

## Part B — The .NET ecosystem and framework-integration prior art

### DuckDB.NET (github.com/Giorgi/DuckDB.NET)

- **Versioning:** packages at 1.5.5 (published 2026-07-26) — the version number mirrors the
  bundled DuckDB release; releases track core within weeks. Targets net8.0 and net10.0.
- **Distribution 2x2:** `DuckDB.NET.Bindings` (P/Invoke only, no native lib) · `DuckDB.NET.Data`
  (ADO.NET, managed-only — you supply libduckdb) · `DuckDB.NET.Bindings.Full` (bindings + native
  for all RIDs under `runtimes/<rid>/native/`, ~105 MB total on NuGet) · `DuckDB.NET.Data.Full`
  (recommended default). A framework can reference the managed-only package and ship/choose
  native binaries itself. The managed ADO.NET assembly is ~235 KB.
- **ADO.NET maturity:** genuinely mature — `DuckDBConnection` (connection-string builder incl.
  MotherDuck `md:` URLs), `DbCommand`/`DbDataReader`, transactions, parameter binding, a
  documented **Appender API** (allocation-free `AppendRow` since 1.5.5; 20–40% faster writes in
  1.5), and **Apache Arrow result streaming** (`ExecuteArrowStream`,
  `ExecuteArrowBatchesAsync`) since 1.5.5. Dapper works over it as a plain ADO.NET provider.
- **Maintenance:** single primary maintainer (Giorgi Dalakishvili) since 2020, ~700 stars,
  sponsored by DuckDB Labs and the AWS open-source fund. DuckDB's own client matrix classifies
  C#/.NET as a **"secondary" client** maintained by Giorgi — first-party-adjacent, not
  core-team (unlike Go and Rust, which the DuckDB org took first-party).
- **ORM-relevant open issues:** Decimal precision >28 digits (#349), enum vector writer with
  non-consecutive values (#330), aborted-transaction behavior changing in v1.6 (#313), MAUI/iOS
  `DllNotFoundException` (#223), no table name from DuckDBReader (#211 — matters for
  materializers). No open issues on transactions/concurrency.
- **AOT posture:** 1.5.0 migrated all P/Invoke to source-generated `[LibraryImport]` with custom
  marshallers and `[SuppressGCTransition]` — the recommended AOT pattern — but neither assembly
  carries `IsTrimmable`/`IsAotCompatible` annotations. Treat as "works, unverified by CI badge."
  (Companion project DuckDB.ExtensionKit *requires* NativeAOT to build extensions, showing the
  toolchain works.)

### EF Core providers (no official one exists)

- **DuckDB.EFCoreProvider (skuirrels)** — v1.24.0 (2026-08-26), ~43 releases since Jan 2026;
  the most serious effort: LINQ translation, change tracking, native migrations with a
  migrations lock and opt-in table rebuilds for constraints DuckDB can't ALTER, appender-backed
  BulkInsert (~1M rows/s vs ~6–8k rows/s for small SaveChanges batches), typed-conflict-target
  Upsert, `UseDuckLake` profile, Parquet/CSV/JSON querying, NTS spatial. Explicitly "community,
  best-effort, no SLA." Documents the platform truths: single-writer, "database is locked" on
  file conflicts, `:memory:` dying with connection close, slow high-frequency small writes.
- **DuckDB.EFCore (Denis Ivanov)** — v1.0.4 (2026-08-12); basic CRUD/LINQ/migrations; minimal
  docs; early-stage.
- **EnergyExemplar.EntityFrameworkCore.DuckDb** — verified publisher; **read-only analytical
  LINQ** (also `UseDuckDbOnParquet`); writes explicitly unsupported; positioned as "SQLite-like
  but 8–10x faster analytical queries."
- **Core-team position (implicit):** .NET is secondary-tier; no EF provider planned under the
  DuckDB org; the org invested first-party in Go (duckdb-go, transferred from marcboeker Oct
  2025) and Rust (duckdb-rs) — data-plane clients, leaving ORM integration to community.

### Cross-framework prior art — the consistent pattern

**No major framework has shipped a first-party DuckDB CRUD adapter.** Rails:
`activerecord-duckdb-adapter` (community, Red Data Tools). Django: not an official backend;
community backends are **read-only-oriented** (`django-duckdb-readonly` querying Parquet/S3
through the ORM). Prisma: long-standing open feature request (#21281). Laravel: community
Eloquent drivers over `pdo_duckdb`, explicitly OLAP-query-builder positioned. Elixir/Ecto: no
maintained adapter. **Every framework treats DuckDB as a specialized/analytic adapter or leaves
it to raw drivers. Nobody has made it a drop-in transactional CRUD replacement** (the skuirrels
EF provider is closest and explicitly says "not for high-concurrency OLTP").

### Technical constraints a framework must design around

- **Concurrency:** read-write file bound to one process; others may attach only READ_ONLY;
  second writer fails with a file lock error. In-process: MVCC with optimistic concurrency —
  appends never conflict; same-row update/delete conflicts throw "Transaction conflict" at
  commit; retry is the documented remedy. Multi-writer remedies (DuckLake, Quack) are outside
  the embedded model.
- **Memory:** `memory_limit` defaults to 80% of system RAM; `threads` to all cores; container
  cgroup detection can misread; spill goes to `<dbfile>.tmp`. Framework should expose
  memory_limit/temp_directory/threads as first-class settings.
- **Write economics:** bulk-optimized MVCC; small high-frequency writes are the pathological
  case (~6–8k rows/s row-at-a-time vs ~1M rows/s Appender). Entity-CRUD needs write coalescing
  and bulk paths promoted.
- **Storage compatibility:** backward compatible (newer reads older, since v0.10); NOT forward
  compatible; default storage version stable at v64 through v1.0–v1.5; `STORAGE_VERSION` pin
  available; v2.0 imminent with a new format. Mitigations: pin native version to the adapter,
  prefer the LTS line, document export/import upgrade.
- **Extensions:** core extensions (VSS, spatial, httpfs, FTS) autoinstall/autoload at runtime
  from DuckDB's CDN by default — bad for air-gapped/prod; framework should support pre-installed
  extensions and disabling autoinstall. Another per-platform payload to pin and ship.

### Part B sources

- NuGet: DuckDB.NET.Data.Full, DuckDB.NET.Bindings.Full; github.com/Giorgi/DuckDB.NET; duckdb.net docs (getting-started, bulk-data-loading); giorgi.dev 1.5 performance post; Giorgi/DuckDB.NET issues + discussion #172
- learn.microsoft.com EF Core providers page; github.com/skuirrels/DuckDB.EFCoreProvider; NuGet DuckDB.EFCoreProvider / DuckDB.EFCore; denis-ivanov/DuckDB.EFCore; EnergyExemplar NuGet
- duckdb.org docs: concurrency, storage internals, configuration, pragmas, extensions, clients overview; why_duckdb; news index
- marcboeker/go-duckdb (archived → duckdb/duckdb-go); duckdb/duckdb-rs; red-data-tools/activerecord-duckdb-adapter; eracle/django-duckdb-readonly; prisma/prisma#21281; Laravel pdo_duckdb drivers; ruslandoga/duxdb
- StackOverflow #73314603 (bulk insert); Semantic Kernel vector-store connectors (Microsoft Learn); NuGet DuckDB+SemanticKernel search
- MotherDuck Power BI docs; datamonkeysite Power BI connector; duckdb.org ExtensionKit blog (2026-03-20)

## Part C — Use cases and scenario fit

### Canonical production workloads (ranked by case-study frequency)

1. **Embedded analytics inside applications** — the dominant pattern. MotherDuck case-study
   wall: AheadComputing, Stern Risk Partners, Dexibit, Emora Health, UDisc, Trrs (delivery ops
   replacing Redshift), ATM.com (off SingleStore). Non-MotherDuck: Evidence (~33.5k weekly npm
   installs), Rill (BI on DuckDB as default embedded OLAP). The DuckDB Local UI cements the
   local-analytics-app pattern.
2. **Parquet/CSV/JSON lakehouse querying** — local and S3 via httpfs. OpenTimes ("a single
   DuckDB file with views pointing at static Parquet over HTTP") is the clean public example;
   pg_duckdb's pitch is the same at the Postgres level.
3. **ELT / dbt transformation target** — dbt-duckdb maintained under the official duckdb org
   (~1.3k stars); external materializations to Parquet/CSV/JSON on local or S3 with Glue
   registration; "Modern Data Stack In A Box."
4. **BI backends** — Rill, Evidence, Metabase DuckDB driver, Omni-on-MotherDuck.
5. **Notebooks / data science** — zero-copy Pandas/Arrow scan; Jupyter via duckdb_engine.
6. **Local-first analytical apps** — Rill's "Agent-Friendly, Local-First Analytics Stack";
   Csvdb (git-friendly CSV → SQLite/DuckDB).
7. **Feature stores** — Feast DuckDB offline store (point-in-time-correct joins over
   Parquet/Delta), with explicit gaps.
8. **Log/metrics ingestion** — weak as a product category (ad-hoc local log investigation only;
   dedicated log tools pick ClickHouse/VictoriaLogs).
9. **Geospatial** — established niche (spatial extension, 50+ GIS formats; "most important
   geospatial software of the last decade").
10. **Vector search** — growing but explicitly experimental: VSS HNSW docs still say "do not
    use this feature in production environments" (persistence behind experimental flag, WAL
    recovery unimplemented, index must fit in RAM, deletes mark stale).
11. **Full-text search** — `fts` core extension, `match_bm25`; "the FTS index will not update
    automatically when the input table changes" (manual rebuild).
12. **Time series** — common workload, no flagship product (Gardyn IoT case study).

### DuckDB in AI/agentic applications — the fastest-growing cluster

- **MCP servers:** `motherduckdb/mcp-server-motherduck` (511 stars; `execute_query` with
  1,024-row/50K-char caps; read-only default; installs documented for Claude Desktop/Code,
  Codex CLI, Gemini CLI, Cursor, VS Code); `ktanaka101/mcp-server-duckdb` (single `query` tool,
  `--readonly` flag, warning that persistent connections "can hold an exclusive lock on the
  file"). Production agent deployments: GoodShip (logistics analytics agent "with full tenant
  isolation"), Zero Health (medical-coding analytics).
- **Text-to-SQL / agents:** MotherDuck LangChain SQL-agent guide; HF smolagents + DuckDB
  tutorial (Nov 2025); BoxHero AI SQL agent over per-tenant DuckDB-WASM; "Build an AI Agent with
  DuckDB as Its Brain"; a 2026 arXiv paper on the agentic loop for DuckDB-WASM. Community `ai`
  extension ships `ai_complete/ai_summarize/ai_classify/ai_extract/ai_translate` SQL functions.
- **Ecosystem reach:** `LangChain.Databases.DuckDb` on NuGet. RAG-on-DuckDB exists but VSS's
  "not for production" disclaimer keeps it a hobby/scale-up pattern.
- **Verdict:** read-heavy SQL-over-files with row caps, read-only defaults, and MCP packaging is
  exactly the shape vendors are productizing for agents; DuckDB is plausibly the default
  "agent analytics tool" by 2026.

### DuckDB as a PRIMARY application database — the anti-pattern, documented

- **Official positioning:** "designed to support analytical query workloads (OLAP)"; bulk-
  optimized MVCC; embedded in the host process. Concurrency docs: one read-write process;
  read-only mode allows many readers but no writers; appends never conflict; same-row edits
  conflict at commit.
- **Enterprise-readiness verdicts:** "enterprise-ready for analytical use cases with the right
  operational wrapper" but "not a drop-in replacement for PostgreSQL in transactional systems"
  (Dench; the 10,000-orders/minute e-commerce platform is the cited anti-pattern). "DuckDB is
  not a tiny Postgres you embed."
- **Workarounds that exist because people keep trying:** ApeCloud's MyDuck Server (standalone
  mode, buffers writes in Arrow, flushes every 200 ms — "DuckDB is slow at single-row writes");
  MotherDuck's "hypertenancy" (one DuckDB database per customer) used by Hazel and PriceMedic;
  Together AI runs ~40 read replicas for concurrent analytics.
- **Failure modes:** cross-process write locks; optimistic-conflict errors; slow single-row
  INSERTs; a Jan 2026 "Ask HN: Who's using DuckDB in production?" opened about a memory leak.
  Nobody credible ships high-concurrency OLTP on a plain DuckDB file.

### Complementary patterns (the mainstream architecture)

- **pg_duckdb** (official, with Hydra and MotherDuck; ~3.2k stars): analytical queries routed to
  DuckDB over existing Postgres tables (`duckdb.force_execution`), lake file access, MotherDuck
  offload. The sanctioned "Postgres OLTP + DuckDB OLAP" hybrid. (ParadeDB's rival pg_analytics
  is discontinued/archived — the ecosystem consolidated behind pg_duckdb.)
- **DuckLake 1.0** (Apr 2026): production-ready lakehouse format; metadata in any SQL catalog
  (SQLite/Postgres/DuckDB), data on object storage; top-10 DuckDB extension by downloads;
  third-party clients (DataFusion, Spark, Trino, Pandas). v1.0 turns on data inlining for small
  inserts/updates/deletes — actively patching the small-mutation gap.
- **Quack** (May 2026, InfoQ): DuckDB as client/server over HTTP; ~3.5x claimed faster data
  movement than Arrow Flight; production-ready targeted with DuckDB 2.0 (fall 2026). Beta today.
- **SQLite pairing:** DuckDB's sqlite extension can ATTACH a SQLite file and run analytics over
  it (single-writer on writeback); docs explicitly suggest "using DuckDB as an analytics engine
  on top of an existing SQLite application database" — the Koan-shaped pairing.
- **SyncLite:** embedded sync pushing SQLite/DuckDB transactions to central Postgres.

### Framework-design takeaway (recorded before the design work)

What wrappers actually expose: DuckDB.NET (ADO.NET + Appender + Arrow); community EF providers
(analytic-leaning); linq2db, FreeSql.Provider.Duckdb, Shiny.DocumentDb.DuckDb. Python reference
ecosystem: duckdb_engine (SQLAlchemy — caveat: no SERIAL type), Ibis, dbt-duckdb, advanced-
alchemy lists DuckDB as tested. Agent surfaces: one generic query tool, read-only, row-capped —
**a query contract, not CRUD verbs.**

Realistic user expectations for an Entity-CRUD framework adapter: (1) query/read-model first —
aggregations, GROUP BY, windows, joins; (2) append-heavy saves + bulk load (Appender) +
CSV/Parquet ingest/export as first-class verbs; (3) file/URI binding — querying Parquet/CSV on
disk or S3 without ingestion (httpfs), plus optional ATTACH of SQLite/Postgres; (4) agentic
query surface — a read-only, row-capped SQL tool; (5) VSS/FTS flagged experimental, never core.

Mismatch points with row-level Entity CRUD: one writer process per file (provider election must
never let two services attach the same file read-write); optimistic concurrency needing retry
surfacing; UPDATE-heavy CRUD is worst-case; VSS/FTS indexes go stale under mutation; no
replication/RLS/SSL backup story — fine for a local read-model, not a system of record.

**Recommendation carried into DATA-0123:** demand is real and growing, but the demanded shape is
analytics/read-model + bulk-ingest + file-query + agent-query — "SQLite's analytical sibling."
Full CRUD is implementable (single-process, single-writer) but positioned as convenience, not
primary store. Revisit shared/multi-writer when Quack reaches production with DuckDB 2.0.

### Part C sources

- duckdb.org why_duckdb; concurrency docs; VSS extension docs; FTS docs; sqlite extension docs; 2025-03-12 local UI post
- ducklake.select 1.0 announcement; InfoQ Quack article; pg_duckdb GitHub; paradedb/pg_analytics (archived); dbt-duckdb GitHub
- motherduckdb/mcp-server-motherduck; ktanaka101/mcp-server-duckdb; MotherDuck case studies + LangChain agent post + JSON log analysis; duckdb.org community `ai` extension
- buckenhofer.com smolagents post; boxhero.io DuckDB-WASM agent; duckdblab.org agent post
- dench.com enterprise-readiness; Medium "analytics but not transactions"; hey.earth webhook gateway + HN 44427278; HN 42265647 (MyDuck); HN 43392521 (OpenTimes); HN 46889787 (Csvdb); HN 46652264 (production Ask HN)
- evidence.dev; rill-data agent-friendly analytics post + docs; observablehq.com framework; feast docs (DuckDB offline store); basedash.com DuckDB for BI
- duckdb.org spatial announcement + docs; marksblogg spatial review
- definite.app duck-stack post; Medium "when to use DuckDB"; suhasbhairav DuckDB vs SQLite
