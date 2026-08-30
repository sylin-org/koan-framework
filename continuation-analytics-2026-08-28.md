# continuation.md — analytics pillar session handoff

_Date: 2026-08-28. Branch: `dev` (release train fast-forwarded to `main`)._

## Where the work stands

The DuckDB connector + Analytics pillar is **shipped and releasing**. ANL-4 at 1.0.2, ANL-5
(facet/delta doors) at 1.0.4, and **ANL-6 (explain / history / shape / freshness doors) at 1.0.6**
(Release run `33192173394` green, `PUBLISH|DONE|87`, Analytics 1.0.6 live on nuget.org).

### ANL-6 — explain, history, shape, freshness (latest work)

- `{recipe}/explain` — serve/compute/refuse with the exact corrective, composed SQL, bounds,
  parameters, sink capabilities (`facets`/`delta`/`parquet`); side-effect-free by contract (a spec
  pins that a never-refreshed projection stays cold after an explain).
- `{recipe}/history` — per-projection ledger ring (50) written in the refresh transaction with a
  `trigger` column: `loop` | `http` | `programmatic` | `backfill-on-read`.
- `{recipe}/shape` — columns with CLR types, declared parameters, posture; pure catalog read.
- `?maxAge=90s/15m/2h/1d` — per-ask freshness tolerance (tightens `ServeWithin`, never loosens);
  served answers carry `MaterializedUtc`; the results door derives `ETag` + `Last-Modified` +
  `Cache-Control: no-cache` and answers 304 to `If-None-Match`.
- `WithParameterDefault<T>(name, value)` — parameterized projections refresh through declared
  defaults (refresh has no ask-time values; ask-time values still win).
- Sink: schema ensured ONCE per recipe per sink instance behind a `SemaphoreSlim` gate — per-call
  autocommit DDL through pooled connections hit intermittent DuckDB "catalog write-write conflict
  on CREATE", and the first gate version deadlocked (gate not reentrant; EnsureCore now calls the
  ownGate:false overload). Suite 36/36 x3 stable after both fixes.
- Card `cards/analytics-explain-history-shape-freshness.md`; spec `AnalyticsDelightDoorsSpec`.

### DUCK-1 — DuckDB capability delight pass (latest work, released at 1.0.8)

- Refresh writes go through the engine's **Appender** via a staging sibling
  (`{table}_staging`), moved into the live table inside the refresh transaction; the chunked
  INSERT remains as the typed fallback (column types without an appender mapping). All
  projection/facet/delta specs green prove atomicity and stamp semantics survived.
- The materialization sink strips `Pooling`/`Cache` keys like the entity path always did
  (`DuckDbConnections.StripLocalKeys`) — the ANL-6 catalog-conflict class is structurally gone.
- Declared extension allow-list: `Koan:Data:DuckDb:Extensions` (array config), loaded per
  connection by an `ExtensionLoadingConnection : DuckDBConnection` wrapper — loads do NOT
  persist across DuckDB.NET connections (empirically established). Fail-closed corrective names
  the extension and the pre-install/autoinstall choice.
- Engine options `MemoryLimit`/`Threads`/`Extensions` NOW ACTUALLY BIND: `AddKoanOptions`
  without a configPath never bound the section, and `DuckDbOptionsSetup` was a hand-rolled
  key-by-key binder that skipped them. Check the sibling connectors for the same gap.
- `Mode=ReadOnly`: read-only open, never creates, refuses writes. Files-as-tables (parquet glob,
  hive partition, CSV sniff) pinned by `DuckDbCapabilitySpec`. WAL-after-clean-stop pinned
  (backup = file copy). `Koan:DuckDb:Home` deliberately deferred (card: connector-fleet).

### The shared-index incident (read before committing here again)

A second workstream (WEB-0073: EntityController PUT route-id governance, `EntityUpsertRequest`
2-arg arity, Koan.Web endpoints + AdapterSurface test kit; and a separate announcement-initiative
docs pass) worked in this tree CONCURRENTLY. Through the shared git index, their refactor got
fused into this session's stamp commit, initially leaving HEAD uncompilable (their Mcp translator
fix was staged but uncommitted). Recovery: committed their staged translator fix as its own
`refactor(mcp)` commit (noted authorship), verified their AdapterSurface suites green (59/59,
81/81), and the combined history released as one train. Their remaining uncommitted files
(samples locks, `docs/initiatives/README.md`, `.gitignore`, SKILL.md, untracked `evals/`,
`docs/initiatives/announcement/`, `samples/recipes/`, root `Recipe.cs`) are still theirs — leave
them. LESSON: with a live concurrent session, never `git add -u` / `git commit --amend` broadly;
commit by explicit pathspec and re-check `git status` immediately before every commit.

### ANL-5 — facet and delta doors (latest work)

- `GET {recipe}/facets?by=column` — distribution (distinct values + counts, engine-side GROUP BY,
  bucket-capped answers say so). With `&since=wm1.…` the question flips to **movement** — counts
  over rows a materialization wrote after the cursor — and the envelope carries `Mode`,
  `ChangesConsidered`, and `DeletesInvisible: true` (stated, never implied: updates count once at
  their new value; deletions are invisible in a derived store).
- `GET {recipe}/delta?since=` — changed rows plus the **next watermark on every response**
  (`Watermark: { given, current }`); consumers never construct watermarks, the server keeps no
  per-consumer state. Malformed cursors refuse with the expected `wm1.<ms>` shape.
- Sink: per-row `_koan_stamp` (unix ms of the writing refresh; refreshes rewrite wholesale, so
  "changed" = "written by a materialization after W"); back-fit to existing tables via
  information_schema + ALTER; stripped from all reads and excluded from Parquet export.
- Capability shape: `ReadFacetsAsync` on `IAnalyticsProjectionSink`; `IAnalyticsChangeTracking`
  optional (Parquet precedent) — engines without stamps degrade loudly.
- Surface parity: `Analytics.Facets/Delta` in code, HTTP doors, MCP `analytics.facets`/`analytics.delta`.
- Card: `docs/initiatives/analytics/cards/analytics-facet-delta-doors.md`; spec:
  `AnalyticsFacetDeltaSpec` — analytics suite **28/28**; DuckDb 49/49; SQLite 49/49.

### Parameterized questions (ANL-4, shipped at 1.0.2)

One declared question now answers a family of slices:

```csharp
Analytics.Question<Todo, string>("by-priority", q => q
    .WithParameter<int>("min-priority")
    .Where(t => t.Priority >= Analytics.P<int>("min-priority"))
    .Count());

await Todo.Analytics.Run("by-priority", new Dictionary<string, object?> { ["min-priority"] = 5 });
```

- `Analytics.P<T>(name)` marker (module spelling) + `AnalyticsParameter.Value<T>` (Abstractions
  spelling) — the binder in `Koan.Data.Abstractions/Analytics/AnalyticsContracts.cs` recognizes **both**
  node shapes (the marker set is a closed contract; Abstractions cannot reference the module, so
  matching is by declaring-type full name + static + single string literal).
- `AnalyticsParameterBinder.Bind<TEntity>` substitutes markers with typed ask-time constants **before**
  filter compile; both the DuckDb and Sqlite composers call it inside `TryCompose` (shared seam, bound
  by logical name).
- Refusals are `NotSupportedException` with corrective text: missing values name the required
  parameters; supplied-but-undeclared values refuse **before compute** in `AnalyticsExecution.Run`
  (the guard sits above composition, so questions with no Where clause are covered too).
- Ask-time values flow from all three doors: `Run(name, parameters, ct)`, MCP `analytics.ask`
  (parametersJson), and the results HTTP door (query params).
- Specs: `AnalyticsParameterSpec` (bind / missing / undeclared) — **Analytics suite 22/22**.

Two bugs the specs caught, both fixed:
1. The binder originally matched only `AnalyticsParameter.Value<T>`; `Analytics.P<T>` nodes were never
   substituted and threw at filter-compile time.
2. Undeclared values were only detected inside the Where visit — a parameterless question silently
   ignored extras. The pre-compute guard in `AnalyticsExecution.Run` closes that.

### Release state (confirmed green)

- Commits on `dev`: `75f06b322` (feature) + `3b3da7083` (95-file dependency-floor cascade,
  `stamp-dependents` run to quiescence: second pass reported `0 stamped, 103 unchanged`).
- Local plan (`artifacts/release/release-plan.json`): **publish=95**, including
  `Sylin.Koan.Data.Analytics 1.0.2`, `Sylin.Koan.Data.Analytics.Web 1.0.2`,
  `Sylin.Koan.Data.Connector.DuckDb 1.0.2`, `Sylin.Koan.Data.Abstractions 1.0.22`,
  `Sylin.Koan.Data.Core 1.0.41`, `Sylin.Koan.Data.Connector.Sqlite 1.0.22`.
- `main` fast-forwarded (`66a5977dd..3b3da7083`) and pushed; Release workflow run `33170662100`
  **succeeded** (plan/pack/prove 11m4s + publish 3m7s, job log ends `PUBLISH|DONE|95`), and
  `Sylin.Koan.Data.Analytics 1.0.2` + `Sylin.Koan.Data.Connector.DuckDb 1.0.2` are verified live on
  nuget.org's flat-container index.

## Verification at handoff

- Full solution build: 0 errors (3 pre-existing warnings).
- Suites: Analytics **22/22**, DuckDb connector **49/49**, SQLite connector **49/49**.
- `scripts/docs-lint.ps1`: 7 errors, all in `docs/initiatives/announcement/work-items/*` —
  pre-existing baseline from another workstream, untouched here.

## Unrelated working-tree content — DO NOT commit blindly

The tree carries uncommitted work from a **different workstream** that this session did not touch:

- `Recipe.cs` (repo root), `samples/recipes/` — a RecipeApi semantic-search sample.
- `docs/initiatives/announcement/` — announcement initiative docs (source of the docs-lint errors).
- `evals/agent-race/`, `evals/evals/` — eval harness scaffolding.
- `.gitignore` (`samples\recipes.db` line), `.agents/skills/koan/SKILL.md`, nine `samples/**/koan.lock.json`
  build-refresh diffs.

Leave these for whoever owns them; don't sweep them into analytics commits.

## Open items (all dispositioned in docs/initiatives/analytics/OPEN-ITEMS.md)

- Linux assembled-host AOT — deferred to CI `aot-verify` lane (native linux-x64 AOT already proven
  in the ANL-0 container spike, 4.8MB binary).
- Entity-plane projection rows rework — future direction, design recorded in the investigation doc.
- DuckDB 2.0 storage bump, scheduler-pillar cron spelling, window functions in typed grammar —
  external/deferred; raw lanes cover window functions today.

## Next actions for the next session

1. ~~Confirm releases~~ — DONE: ANL-4 at 1.0.2 (run `33170662100`) and ANL-5 at 1.0.4 (run
   `33175468109`), both green, packages verified on nuget.org.
2. Next delight tier, designed but not built (see OPEN-ITEMS.md): `explain` door, refresh
   `history`, answer `shape`, freshness negotiation (`maxAge`) + policy-derived HTTP caching
   headers — all four expose facts the surface already owns; one door per pass, MCP mirror in the
   same stroke.
3. If the analytics initiative is considered closed, archive `docs/initiatives/analytics/`
   per the initiative lifecycle, and fold the recipe/guides into the next docs release pass.
4. Candidate follow-ups recorded in OPEN-ITEMS.md: HAVING on grouped asks, projection row TTL,
   `Ask` over non-relational stores with a corrective refusal template.
5. The release "prove" step surfaced two non-blocking compiler warnings worth a cleanup pass:
   an unused `Microsoft.Extensions.Hosting` using in `AnalyticsModule.cs` and a possible null
   reference return in `AnalyticsController.cs` (results door parameter extraction).
