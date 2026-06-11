# Stage 6 — Implementation prompt stash

**Purpose**: ready-to-paste prompts that drive the implementation of every issue flagged by the
assessment (Tracks A–H, 04) plus the strategic capabilities (05). **Agentic-optimized for lesser
models**: each prompt is self-contained, evidence-anchored, recipe-shaped, and carries explicit
verification and stop conditions. Frontier-only items are marked — do not feed those to small
models.

**How to use**: paste `[PREAMBLE]` + one prompt card into a fresh session. One card per session.
Tier legend — **T1** = small model, autonomous · **T2** = small model, recipe-driven ·
**T3** = frontier model only.

**Dependency order**: B1 → (A*, C0, CUT waves in any order) → E-series → D/G → H-series.
F1 early (it improves every later session's error visibility). T3 items anytime.

---

## [PREAMBLE] — paste at the top of EVERY session

```text
You are working on the Koan Framework (.NET 10 meta-framework; repo root = the working
directory). Rules for this session — they override your defaults:

1. SCOPE: do exactly the task below. One intent per session. No drive-by fixes, no
   refactoring outside the named files, no "while I'm here".
2. EVIDENCE FIRST: before editing, read the files the task names. Never reference an API you
   have not seen in this session — the repo's older docs contain APIs that do not exist; grep
   before you trust. Any API you use in code or docs must be evidenced by a file:line you read.
3. VERIFY: run the named verification (at minimum: `dotnet build Koan.sln`). A session that
   cannot get back to green REVERTS its changes and reports — never "fix forward" into new scope.
4. OUTPUT CONTRACT: your final summary lists every file touched, and for every claim cites
   evidence ("removed X — verified zero references: grep '<pattern>' = 0 hits"). No vague claims.
5. STOP CONDITIONS — stop and report instead of choosing, if you hit ANY of: a failing test you
   did not expect; an API that does not match this recipe; a second plausible way to do the task;
   a reference to the thing you're removing from a file this recipe did not predict.
6. NO-GO ZONES (do not modify, ever, in a T1/T2 session): src/Koan.Data.Core/Model/**,
   EntityContext internals, src/Koan.Core/Hosting/** (except where a recipe names exact lines),
   RegistrySourceGenerator, capability token definitions, any adapter's query-translation code,
   any public API rename.
7. CONVENTIONS: Newtonsoft.Json is the canonical serializer (do not introduce STJ surfaces).
   Canonical entity verbs: Save / Remove / Query. Canonical module primitive: KoanModule.
   Never manually register framework services. Never add a new Add*() extension where a
   registrar exists. Commit messages: conventional commits (feat/fix/refactor/docs/test/chore).
```

---

## Track B — enforcement substrate (do B1 first; it protects everything else)

### B1 · Solution truth — all test projects into Koan.sln 〔T2〕

```text
TASK: Add every test project on disk to Koan.sln. Today 39 of ~87 test .csproj files are not in
the solution, so `dotnet test Koan.sln` silently skips them (evidence:
docs/assessment/01-cartography.md §2.4).
RECIPE:
1. Inventory: list all **/*.csproj under tests/ (exclude bin/obj). Diff against `dotnet sln
   Koan.sln list`.
2. For each missing project: `dotnet sln Koan.sln add <path> --solution-folder tests/<suite>`
   (mirror the existing solution-folder layout; scripts/regenerate-sln.ps1 exists — read it
   first and prefer it if it covers this).
3. EXCLUDE (do not add — they are husks/orphans pending deletion):
   tests/Suites/Canon/Koan.Canon.Core.Tests, tests/Suites/Data/Vector/ (orphan Weaviate spec),
   tests/Suites/AI/Core/Koan.AI.Tests (no csproj), tests/Suites/AI/Koan.AI.Core.Tests (net8.0,
   references nonexistent packages), tests/Suites/Cache/Unit (no csproj).
4. VERIFY: `dotnet build Koan.sln` green; `dotnet test Koan.sln --list-tests` enumerates without
   container infra (container-gated specs must skip cleanly, not fail).
DONE WHEN: sln contains every live test project; build green; summary lists added projects.
STOP IF: adding a project breaks the build — report which, do not "fix" the project.
```

### B2 · CI PR gate 〔T2〕

```text
TASK: Re-enable CI as a PR gate. Today 5 of 7 workflows in .github/workflows are disabled noop
placeholders and the only active workflow (release-on-main.yml) runs ZERO tests.
RECIPE:
1. Read .github/workflows/*.yml and scripts/green-ratchet.ps1 + scripts/test-all.ps1.
2. Create .github/workflows/pr-gate.yml: on pull_request → checkout, setup .NET (per
   global.json), `dotnet build Koan.sln -c Release`, `dotnet test Koan.sln -c Release` with the
   container-gated suites skipping (they already self-skip without Docker), then
   scripts/docs-lint.ps1 and scripts/validate-code-examples.ps1.
3. Do NOT modify release-on-main.yml in this session (separate task B3).
4. VERIFY: workflow YAML parses (actionlint if available, else careful review); local equivalent
   of each step succeeds.
DONE WHEN: pr-gate.yml exists and each of its steps has been executed locally and passed.
```

### B3 · One versioning generation (NBGV-native release) 〔T3 — release-critical〕

```text
FRONTIER TASK: Rework .github/workflows/release-on-main.yml to be NBGV-native (ARCH-0085),
removing the deprecated build/versions.props + Update-Versions.ps1 path it still drives.
Evidence: docs/assessment/evidence/testsBuild.json (ci + buildInfra fields). Read
scripts/versioning/* (release-from-dev.ps1, Initialize-NbgvBaseline.ps1) — they appear to be the
newer intended path. Add `dotnet test` as a release gate. Also fix packaging/*.nuspec metadata:
net9.0 → current TFM, sylin-labs URLs → sylin-org.
```

---

## Track A — truth restoration

### A1 · ADR status sweep 〔T1〕

```text
TASK: Mark superseded ADRs in docs/decisions/ so no discarded decision still reads "Accepted".
RECIPE — for each pair below: open the OLD file, add directly under the title:
  > **Status: Superseded by <NEW>.** <one-line reason>
and change any Status field to Superseded. Do not edit the NEW files except to verify they exist.
PAIRS:
- JOBS-0001, JOBS-0002, JOBS-0003 → superseded by JOBS-0005
- OPS-0050 → superseded by JOBS-0005 ("Phases 2–3 — cron, locks, windows, bootstrap runner —
  were never implemented")
- ARCH-0046 (Recipe) → superseded by ARCH-0086 (KoanModule)
- MESS-0021..0029, MESS-0070, MESS-0071 → mark "Describes a prior messaging generation; the
  inbox/alias/provisioning features no longer exist in code" (Superseded/Retired)
- FLOW-0070, FLOW-0101..0106, FLOW-0110, ARCH-0053, WEB-0050, WEB-0060 → "Flow pillar removed
  from the codebase" (Retired)
- DATA-0019 (Cqrs) → mark Superseded when/if the C-CQRS cut lands (check src/Koan.Data.Cqrs
  exists; if already deleted, mark now)
- ARCH-0060 → add "Reaffirmed by ARCH-0075" note (its control surface survived the rebuild)
- DATA-0060 + DATA-0085 → cross-reference each other (two ADRs, one decision domain)
VERIFY: scripts/docs-lint.ps1 passes.
DONE WHEN: every listed file carries the banner; summary lists files touched.
```

### A2 · Front-door drift sweep (remaining stale docs) 〔T2〕

```text
TASK: Remove ghost APIs and stale version pins from the remaining user-facing docs. README,
docs/index.md, getting-started/*, principles.md, samples/README.md are ALREADY rewritten — do
not touch them. Targets: docs/architecture/comparison.md, docs/getting-started/
enterprise-adoption.md, docs/guides/semantic-pipelines.md (if present), docs/support/
troubleshooting.md, .claude/skills/quickstart/SKILL.md, .claude/skills/debugging/SKILL.md.
RULES:
1. Ghost APIs to remove/replace wherever found (verify each with grep before claiming):
   Flow.OnUpdate / UpdateResult (Flow pillar deleted) · Todo.SemanticSearch as entity static
   (real: EntityEmbeddingExtensions.SemanticSearch<T>(query, ...)) · Koan.Messaging.InMemory
   (no such package) · .Embed(new AiEmbedOptions...) pipeline stage (does not exist; Tokenize
   already embeds) · koan CLI as `koan` (tool is koan-orchestrate, and the stack is condemned
   by ARCH-0077 — remove the recommendation entirely) · AddKoan(options => ...) (no overload) ·
   `--version 0.6.3` pins (never published; remove or replace with "current").
2. In comparison.md: any 🟩 cell resting on Flow or the .Embed stage downgrades to 🟨 with an
   honest note, or the row is removed.
3. In the two skills: fix the Describe(BootReport, ...) sample to the real signature — read
   src/Koan.Core/IKoanAutoRegistrar.cs first and copy the actual method shape.
4. Remove `framework_version: v0.6.3` and false `validation:` stamps from the front-matter of
   every file you touch (do not add new version pins).
VERIFY: scripts/validate-code-examples.ps1 and scripts/docs-lint.ps1 pass on touched files.
STOP IF: a doc's claim seems true but you cannot find the API — report it, don't guess.
```

### A3 · Repo-root and source-tree litter 〔T1〕

```text
TASK: Delete checked-in litter. List (verify each is untracked-junk or stale before deleting;
`git log --follow -1 -- <path>` for tracked ones):
- Repo root: nul, malicious-project.json, test-project.json, query-*.csx,
  PHASE1_IMPLEMENTATION_RESULTS.md, PHASE2_IMPLEMENTATION_RESULTS.md
- tests/nul; samples/S5.Recs/inspect.json (committed CLI output)
- All *.csproj.lscache files under src/ (~9)
- src/Koan.Web.Auth.Services/ZERO-CONFIG-ANALYSIS.md (working notes in a shipping project)
- Stale TestResults: tests/Suites/Cache/Unit/** (entire dir — contains only a .trx)
VERIFY: dotnet build Koan.sln green.
DONE WHEN: files gone, build green, summary lists deletions with the pre-deletion evidence.
```

---

## Track C — the cut waves

### C0 · Wave 0: debris directories 〔T1〕

```text
TASK: Delete the verified tombstone directories. Evidence: docs/assessment/01-cartography.md §5.
DELETE: src/Koan.Data.Lucene, src/Koan.Cache.Adapter.Memory, src/Koan.Jobs.Core,
src/Koan.Flow.Core (move its TECHNICAL.md to docs/archive/flow-TECHNICAL.md first),
src/Koan.Context, src/Koan.Canon.Core, src/Connectors/Canon (whole tree),
tests/Suites/Canon/Koan.Canon.Core.Tests, tests/Suites/Data/Vector (orphan spec tree),
tests/Suites/AI/Core/Koan.AI.Tests, tests/Suites/AI/Koan.AI.Core.Tests,
samples/S4.Web, samples/S7.TechDocs, samples/S8.Location, samples/S9.Location,
samples/S12.MedTrials.Core, samples/S12.MedTrials.McpService, samples/S13.DocMind.Tools,
samples/KoanAspireIntegration.AppHost.
PRECHECK per directory: confirm no .csproj is referenced by Koan.sln or any other csproj
(grep the directory name across **/*.csproj and Koan.sln; expect 0 hits — if not 0, STOP).
VERIFY: dotnet build Koan.sln green; dotnet test Koan.sln (non-container) green.
```

### CUT-TEMPLATE 〔T2〕 — instantiate with one row of the table below

```text
TASK: <MODE> the project(s) <PROJECTS>.
JUSTIFICATION (verify, then cite in your summary): <WHY>.
PRECHECKS (all must hold, else STOP):
1. grep each project name across **/*.csproj — inbound ProjectReferences must match <EXPECTED-REFS>.
2. grep the key public type names across src/ samples/ tests/ — no live consumers beyond <EXPECTED-REFS>.
3. Confirm packaging/Koan.nuspec + Koan.App.nuspec do NOT list the package (verified clean for
   all rows below as of 2026-06-10).
STEPS:
1. <MODE=cut>: remove project(s) + their test projects; remove from Koan.sln; delete the source
   directories. <MODE=park>: git mv to /attic (create if absent), remove from Koan.sln, add an
   attic/README.md line explaining why. <MODE=attic-tag>: create branch attic/<name> containing
   the project, then cut from dev.
2. Remove the project's lines from docs: modules-overview.md, module-ledger.md, capability-map.md
   (grep the project name under docs/ and clean each hit; for big docs add a "removed/parked
   2026-06" strike-through note instead of rewriting).
3. <EXTRA>.
4. Mark/annotate the ADR named in <WHY> if one is listed.
VERIFY: dotnet build Koan.sln green; dotnet test Koan.sln (non-container) green.
DONE WHEN: project gone/parked, docs swept, build+tests green, summary cites all precheck greps.
```

| Instance | MODE | PROJECTS | EXPECTED-REFS | WHY / EXTRA |
|---|---|---|---|---|
| C1 | cut | Koan.Data.Cqrs + src/Connectors/Data/Cqrs (Mongo outbox) | only each other | Zero consumers/tests; superseded by Jobs ledger outbox (JOBS-0005). EXTRA: mark DATA-0019 superseded. |
| C2 | cut | Koan.WebSockets | none (own tests only) | Zero src consumers; SSE won every realtime use. EXTRA: delete tests/Koan.WebSockets.Tests. |
| C3 | cut | Koan.Web.Json.Strict | none (own tests only) | Decision recorded (05 §6.1): Newtonsoft is canonical; the STJ island goes. EXTRA: delete tests/Koan.Web.Json.Strict.Tests. |
| C4 | attic-tag | Koan.Web.Connector.GraphQl | archived sample S4 only | Sole consumer archived; HotChocolate CVE treadmill for nobody (WEB-0041/0042 note). |
| C5 | cut | Koan.Recipe.Abstractions + Koan.Recipe.Observability | only each other | Superseded bootstrap idiom (ARCH-0046→ARCH-0086); AppDomain-scan anti-pattern. EXTRA: fold ObservabilityRecipe's AddHealthChecks + resilient HttpClient (~10 lines) into Koan.Web's registrar first — read ObservabilityRecipe.cs and port exactly what it does. |
| C6 | cut | Koan.Service.Inbox.Connector.Redis | none | Its client API (HttpInboxStore) no longer exists in src (grep = 0); only consumer is archived S15. EXTRA: mark MESS-0025/ADR-0026 retired. |
| C7 | park | Koan.Secrets.Abstractions + Koan.Secrets.Core + src/Connectors/Secrets/Vault | only each other | Dormant-complete: zero tests/consumers. The reflection hook in Koan.Data.Core (TryInvokeSecretsBootstrap, ServiceCollectionExtensions.cs:100-109) is soft (Type.GetType throwOnError:false + catch) — parking cannot break it; verify those lines before and build after. |
| C8 | cut | Koan.ServiceMesh + Koan.ServiceMesh.Abstractions + src/Services/Translation (+ .Container) | each other + Koan.Web.Admin (defensive) + S8.PolyglotShop (broken) | No ADR, no tests, experimental. EXTRA: in Koan.Web.Admin remove the defensive ServiceMesh surface (KoanAdminServiceMeshSurfaceFactory + its GetService call) — read it first; it null-checks, so removal is a small excision. S8.PolyglotShop is already broken/out-of-sln: move to attic in the same pass. |
| C9 | park | Koan.Tagging | none (own tests) | Good quality but zero in-repo consumers and an EXTERNAL downstream consumer — park to /attic (do NOT delete), keep its tests with it. EXTRA: remove the dangling "ADR-0018" citation in its XML docs. |
| C10 | park | Koan.Rag + Koan.Rag.Abstractions | bootstrap test only | 8k LOC incubator, zero consumers, InternalsVisibleTo a nonexistent test project. Park to /attic pending a real consumer. EXTRA: remove its Bootstrap integration spec reference (tests/Suites/Integration/Bootstrap — delete the Rag spec file). Cut BEFORE/WITH C12 (Rag references AI.Orchestration). |
| C11 | cut | src/Koan.AI dead surface only (NOT the project) | n/a | Delete src/Koan.AI/Pipelines/** (TextPipeline/ImagePipeline/IAiPipelineStage/PipelineContext/StorageResult — zero constructors outside the folder; verify) and src/Koan.AI/PipelineAiExtensions.cs IF grep shows zero external consumers of Tokenize (verify; if consumers exist, STOP). Also delete the duplicate internal Workers/MediaAnalysisEmbeddingBridge in Koan.Data.AI (keep the public root-namespace one — check ServiceCollectionExtensions wiring first). |
| C13 | cut | src/Connectors/Data/Vector/PGVector + its orphan test project | none in sln | Does not compile (csproj's own comment); out of sln; fix parked on branch trusting-mccarthy. Tag attic/pgvector first. EXTRA: then delete IVectorFilterTranslator<TNative> + VectorEmbeddingAttribute from Koan.Data.Vector.Abstractions (their only implementor/consumer was PGVector — verify with grep). |
| C14 | cut | Koan.Storage ResilientStorageDecorator (file-level) | StorageService composition only | Self-described "Legacy — superseded by Mode=Replicated"; zero tests. Read StorageService.cs first; remove the decorator + its StorageFallbackMode/Resilient option branch; keep Replicated mode untouched. |

> **Not in the table (need frontier judgment, see T3 section)**: AI vertical collapse (C-AI),
> Web.Auth.Roles fold, auth connector collapse, Data.Direct fold, Vector facade merge.

### C17 · Scheduling cut 〔T2 — full verified recipe exists〕

```text
TASK: Cut Koan.Scheduling per the adversarially-verified recipe in
docs/assessment/evidence/stage4/scheduling-cut.json — READ IT FIRST and follow its
refinedRecommendation field exactly (migrate S5BootstrapTask and KoanContext's
JobMaintenanceTask to [JobAction(Schedule="@boot", ...)]+[JobIdempotent] jobs; DROP the
/.well-known/scheduling endpoint and Koan.Web's csproj reference to Koan.Scheduling; clean
KoanServiceActions/KoanServiceEvents Scheduling groups in Koan.Core; delete the project; mark
OPS-0050 superseded; state the two behavior changes in the commit message).
VERIFY: build green; tests/Suites/Jobs all green; grep "Koan.Scheduling|IScheduledTask" across
src/ samples/ tests/ = 0 hits (docs hits get the A1-style banner treatment).
```

### C19 · Execute MEDIA-0008's overdue deletion 〔T2〕

```text
TASK: Delete the obsolete media output-cache family that MEDIA-0008 scheduled for removal:
IMediaOutputCache, FileSystemMediaOutputCache, NullMediaOutputCache, MediaCacheHit,
MediaOutputCacheOptions (all [Obsolete], src/Koan.Media.Web) + the transition shims in
MediaController and the registrar that probe both paths. Also: remove the dead Koan.Web
ProjectReference from src/Koan.Media.Core/Koan.Media.Core.csproj (verify zero Koan.Web/
AspNetCore usages in Media.Core first — grep), and move ProfileMedia/ColdProfileMedia out of
src/Koan.Media.Core/Model (their only consumer is an archived sample — verify, then delete).
VERIFY: build green; tests/Suites/Media (~50 specs) green — these tests are the safety net; if
any fail, STOP and report which shim was load-bearing.
```

### C20 · Web halo merges 〔T2, one per session〕

```text
C20a TASK: Merge Koan.Web.Connector.Swagger INTO Koan.Web.OpenApi (UI behind
KoanOpenApiOptions.EnableUi, default true in Development). OpenApi's only consumer is the
Swagger connector (verify). Update the ~13 consumer csprojs (samples + KoanContext) to reference
Koan.Web.OpenApi. Keep both old package ids out of nuspecs (none present — verify).
C20b TASK: Merge Koan.Admin INTO Koan.Web.Admin (the headless split served a console UI that
was never built — grep EnableConsoleUi: gating flag only, zero implementation). Update samples'
redundant double-references.
C20c TASK: Fold Koan.Web.Transformers INTO Koan.Web, PRESERVING opt-in activation: transformers
must remain inert unless explicitly enabled (keep the existing options/predicate gate). Then
delete the three Type.GetType reflection shims (Koan.Web ServiceCollectionExtensions:41,
OptionalTransformerInputFormatterConfigurator, OpenApi TransformerMediaTypesOperationTransformer)
and replace with direct references. Read each shim before deleting; behavior for apps that never
referenced Transformers must not change (default off).
VERIFY (each): build + tests/Suites/Web green.
```

---

## Track D — orchestration (bridge first, no wholesale deletion)

### D1 · Break the kernel inversion 〔T3〕

```text
FRONTIER TASK: Koan.Core must stop referencing Koan.Orchestration.Abstractions
(Koan.Core.csproj:32). Move the minimal service-identity surface ServiceDiscoveryAdapterBase
needs into Koan.Core (resolving one of the three KoanServiceAttribute collisions in the same
move), update the ~24 inbound referencers, delete Koan.Core.Adapters' MissingTypes.cs namespace
squat. Evidence: docs/assessment/evidence/stage4/sequencing.json (corrections #3) and
01-cartography §2.5. This is a dependency-direction redesign touching every connector — design
the target shape first, then stage the moves.
```

### D2 · Delete the orchestration stack's dead surface 〔T2〕

```text
TASK: Within src/Koan.Orchestration.* (the ARCH-0077-condemned stack), delete only the
verified-dead surface — NOT the CLI/planner/providers themselves:
1. IServiceAdapter, IKoanService, IDevServiceDescriptor in Orchestration.Abstractions (zero
   implementors repo-wide — verify with grep) + the Koan0049A diagnostic in
   Koan.Orchestration.Generators that enforces them.
2. The five legacy attributes [KoanService] superseded (ContainerDefaults, AppEnvDefaults,
   EndpointDefaults, HealthDefaults, ServiceId) — ONLY if grep shows no remaining usage in
   src/ samples/ (the generator still parses them — remove that parsing branch too; read
   OrchestrationManifestGenerator first).
3. The SelfOrchestration subsystem in Koan.Orchestration.Aspire (KoanSelfOrchestrationService,
   KoanDependencyOrchestrator, DockerContainerManager, TestHostEnvironment.cs,
   SelfOrchestrationConfigurationProvider) — verify no sample/appsettings activates
   OrchestrationMode=SelfOrchestrate first; if one does, STOP.
4. Vestigial Aspire ProjectReferences in PGVector (if not already cut) and
   Koan.Service.KoanContext (zero code usage — verify).
5. Tombstone InternalsVisibleTo in Cli/Compose ("...Tests" assemblies that don't exist).
6. Merge Koan.Orchestration.Cli.Core into Koan.Orchestration.Cli (all-internal,
   InternalsVisibleTo only the Cli — verify, then collapse).
VERIFY: build green. STOP IF any [KoanService]-annotated connector fails to compile.
```

---

## Track E — finish the in-flight migrations

### E1-TEMPLATE · Fold manual XRegistration into the registrar 〔T2, one adapter per session〕

```text
TASK: In src/Connectors/Data/<ADAPTER>, fold the manual <ADAPTER>Registration static class into
the existing Initialization/KoanAutoRegistrar so there is exactly ONE wiring unit.
RECIPE:
1. Read both files. Diff what each registers. The auto path registers MORE (discovery adapter,
   orchestration evaluator) — the manual path's unique lines (if any) move into the registrar.
2. Find consumers of the manual Add<Adapter>Adapter() extension (grep across src/samples/tests —
   expect: test fixtures only). Update those call sites to rely on AddKoan() via the fixture's
   KoanIntegrationHost, OR if the fixture genuinely needs eager registration, point it at a
   retained thin Add<Adapter>Adapter() that now just delegates to the registrar's Register
   method — choose the FIRST option unless tests fail.
3. Delete the manual class.
VERIFY: build green; that adapter's test suite green (tests/Suites/Data/<Adapter>).
ADAPTERS (one session each): Postgres, Mongo, Sqlite, SqlServer, Couchbase, Json
(Json also: delete the JsonRepo static DI-bypass factory if grep shows zero framework consumers).
```

### E2 · Delete V1 service discovery 〔T2〕

```text
TASK: Remove the legacy OrchestrationAwareServiceDiscovery (V1) from Koan.Core and migrate its
4 remaining call sites. Evidence: 01-cartography (core-bootstrap redundancies).
RECIPE: grep `new OrchestrationAwareServiceDiscovery` — expect 4 hits in the RabbitMq (×2) and
Vault (×2) connectors. Read how V2 (OrchestrationAwareServiceDiscoveryV2 +
IServiceDiscoveryCoordinator, DI-registered) is consumed elsewhere; convert the 4 sites to
resolve the coordinator from DI (constructor injection in those registrars' service factories).
Then delete V1 + IOrchestrationAwareConnectionResolver (verify zero remaining refs) and rename
V2 to drop the suffix (update its registrations; this is an internal type — verify with grep
that no public API leaks the name; if it does, STOP).
VERIFY: build green; Messaging + Secrets-related bootstrap specs green.
NOTE: if C7 (Secrets park) already landed, the Vault sites are gone — adjust expectations.
```

### E3 · One singleflight 〔T2〕

```text
TASK: Unify on Koan.Core.Singleflight.ISingleflightRegistry (the DI one). Migrate the static
Koan.Core.Infrastructure.Singleflight consumers (Postgres/Sqlite/SqlServer repository files —
grep `Infrastructure.Singleflight`) to the registry, delete the static class and the
do-nothing forwarding shim src/Koan.Data.Relational/Infrastructure/Singleflight.cs (zero
callers — verify). CAUTION: the relational call sites are hot paths — preserve the exact
keying and stampede semantics; read DATA-0057 first. If the DI resolve is awkward inside a
static repository context, STOP and report (the inverse direction — promote the static, migrate
CacheClient — is the fallback; do not decide alone).
VERIFY: relational adapter suites + Cache suites green.
```

### E4 · One health aggregator + boot-report surface 〔T2〕

```text
TASK: Delete the dead root-level duplicate health stratum in Koan.Core:
src/Koan.Core/HealthAggregator.cs + the root HealthAggregatorOptions (never registered — verify
ServiceCollectionExtensions.cs:74 region wires only the Observability.Health one). Remove the
full-qualification workaround comments that the collision forced. Then read the AddKoanCore
"legacy contributors bridge" comment block: if HealthContributorsBridge is registered but its
comment says legacy was removed, reconcile — keep IHealthContributor as the public seam (it is
documented in principles.md), delete only the unreferenced bridge pieces (verify each with grep).
VERIFY: build green; Bootstrap integration suite green.
```

### E5 · Auth flow engine swap 〔T3〕

```text
FRONTIER TASK: Replace the hand-rolled AuthController OAuth2/OIDC internals with the ASP.NET
authentication handlers that the dead AddKoanWebAuthAuthentication path (154 LOC, zero callers)
already configures. Fixes OIDC-501 + adds PKCE + deletes ~300 LOC. Then: retire
IKoanAuthEventContributor (migrate RoleListFileContributor + AdminBootstrapContributor to
IKoanAuthFlowHandler, delete LegacyAuthContributorAdapter + AuthEventDispatcher); excise the
SAML stub surface (SamlController 501s + 6 dead ProviderOptions fields threaded through merge/
health); deduplicate the 3× ProviderOptions merge logic into one helper; ADD the missing
ARCH-0079 integration spec: full challenge/callback flow against the Test provider via
KoanIntegrationHost — the spec that would have caught OIDC-501. Evidence:
docs/assessment/evidence/pillar-web-auth-security.json.
```

### E6 · ES/OS shared core 〔T3〕

```text
FRONTIER TASK: Implement the adversarially-corrected ES/OS consolidation in
docs/assessment/evidence/stage4/es-os-merge.json — follow its refinedRecommendation exactly
(shared skeleton into Koan.Data.SearchEngine; 3-member dialect seam: BuildSearchRequestBody /
BuildIndexBody / MapSimilarityToken; ExportAll+GetCount+IndexStats implemented once — fixing
OS's lost capability; KEEP two thin packages; fix the four named drift bugs incl. the OS factory
cross-claiming provider 'elastic'; supersede DATA-0097 §6 via a dated note). The
VectorAdapterSurface matrix is the regression net — both suites must stay green.
```

### E7 · Slim Messaging.Core 〔T2〕

```text
TASK: Remove verified-dead surface from src/Koan.Messaging.Core and fix the hot-path dispatch:
1. Delete IMessageBusSelector (zero refs — verify), IQueuedMessage + the duck-typed
   SendToQueueAsync reflection fallback (zero implementors — verify), MessagingTransformers
   (registry with zero registrations — verify; keep MessagingInterceptors).
2. Replace the per-send GetMethod+MakeGenericMethod reflection (MessagingExtensions.cs ~:103)
   with a ConcurrentDictionary<Type, Func<...>> cached typed-delegate dispatch — semantics
   identical, allocation-free steady state. Read the method fully first.
3. Fix TransportEnvelope<T>'s doc-comment (still cites the deleted Flow pillar).
VERIFY: build green; Cache.Coherence.Messaging + Jobs.Transport suites green (they ride this
code); S3.Mq sample builds.
STOP IF: any test depends on the reflection path's dynamic behavior.
```

### E8 · Adapter capability parity 〔split〕

```text
E8a 〔T2〕 TASK: Postgres — declare DataCaps.Write.BulkUpsert (UpsertMany already implemented at
PostgresRepository.cs ~:633 — read it, confirm semantics match the token's contract as
implemented by Sqlite/SqlServer, then add the token + the IBulkUpsert interface). VERIFY:
Postgres suite + FilterConvergence green.
E8b 〔T3〕 FRONTIER: Couchbase — implement IConditionalWriteRepository (CAS) + FastRemove, or
write the ADR note saying why not (it has real transactions, so CAS is implementable). Hot-path
+ distributed-correctness work.
E8c 〔T3〕 FRONTIER: Redis-as-data decision — stop advertising FilterSupport.Full for
scan-all, or implement cursor paging + native TTL, or demote Redis to cache/coherence-only.
Decision affects public capability surface.
```

### E9 · EntityContext doc/code contradiction 〔T2 — docs half only〕

```text
TASK: src/Koan.Data.Core/EntityContext.cs — the With(...) XML doc says "Replaces any previous
context (does not merge)" but the implementation (~lines 140-146) inherits prev
Source/Adapter/Partition/CacheBehavior. The BEHAVIOR is correct and intended (inheritance);
fix the DOC COMMENT to describe the actual inherit-unless-overridden semantics precisely,
including the mutual-exclusion of source/adapter. Touch only XML comments. Add a unit test
pinning the inheritance behavior in tests/Suites/Data/Core if none exists (grep first).
```

---

## Track F — fail-loud boot

### F1 · The 20-line boot fix 〔T2 — recipe adversarially verified〕

```text
TASK: Implement the corrected fail-loud boot policy from
docs/assessment/evidence/stage4/fail-fast.json — READ its refinedRecommendation and implement
EXACTLY that: in src/Koan.Core/Hosting/Bootstrap/AppBootstrapper.cs (initializer loop ~:112-123
and the manifest-invoker catch ~:227-245): write module type + full exception to Console.Error,
record into the registry summary (RegistrySummarySnapshot channel → boot report MODULES-FAILED
block), and rethrow wrapped in a NEW sealed KoanBootException {module, assembly, phase, inner}
UNLESS env var KOAN_BOOT_LENIENT=1. Leave the assembly-closure catches (~:60,72,92,95) lenient
but counted. Do NOT create any other exception types. Mirror the naming/style of
KoanBackgroundServiceOrchestrator.FailFastOnStartupFailure (read it — it is the in-repo
precedent).
ADD TESTS: a throwing fake IKoanInitializer → KoanBootException with module name; with
KOAN_BOOT_LENIENT=1 → host boots + failure visible in the registry summary.
VERIFY: full non-container test run green (this touches every test's boot path — if unexpected
suites fail, the failing module was relying on silent swallow: STOP and report it, that's a
real finding).
```

### F2-TEMPLATE · Swallow burn-down 〔T2, one project per session〕

```text
TASK: Reduce silent catch blocks in <PROJECT> (census: docs/assessment/evidence/stage2/
ergonomics.json swallowCensus). For each `catch { }` / comment-only catch in the project:
classify — (a) typed+benign capability fallback (catch (NotSupportedException) etc.): keep,
ensure the comment says why; (b) untyped on a degradable path: narrow the exception type AND
add a LogWarning/LogDebug with context; (c) untyped on a correctness path (config parsing,
transactions, DDL): make it throw or log-error — if unsure which, STOP and list it.
Never change behavior beyond adding logging/narrowing without flagging it.
VERIFY: project's test suite green.
START WITH: Connectors/Data/Sqlite (23 sites), Koan.Data.Core/AdapterConnectionResolver.cs
(the silent config-typo fallthrough — this one is class (c): a malformed setting value must at
minimum LogWarning), Koan.Web (9), Koan.Storage (9), Koan.Mcp (9).
```

---

## Track G — Koan.Core diet

### G1 · Extract Koan.Observability 〔T3〕

```text
FRONTIER TASK: Move the OTel wiring (src/Koan.Core/Observability/ServiceCollectionExtensions +
ObservabilityOptions + the 5 OpenTelemetry package refs) into a new Koan.Observability package
(Reference=Intent: referencing it IS the telemetry intent). AddKoanCore never calls it today —
verify nothing breaks by its absence; provide the auto-registrar so referencing the new package
re-enables exactly current behavior. Decide what remains in Koan.Core (health primitives stay).
Update consumers (grep AddKoanObservability). Package-graph change → frontier.
```

### G2 · Cut the kernel's dead strata 〔T1/T2〕

```text
TASK: Delete from src/Koan.Core (verify each with grep across src/samples/tests first;
expect zero consumers):
1. BackgroundServices/FluentApi/** (KoanServices.Do/On/Query + ServiceBuilder — the kernel's
   only TODOs live here) + Actions/KoanServiceActions.cs + Events/KoanServiceEvents.cs IF after
   the FluentApi cut their only remaining consumers are the 3 internal services — if real
   consumers remain, trim to the constants actually used and report.
2. Extensions/EntityTransferExtensions.cs (zero consumers).
3. Json/JsonPathMapper.cs (zero external consumers — verify).
4. Rewrite src/Koan.Core.Adapters/README.md + TECHNICAL.md to describe ONLY what exists
   (readiness pipeline, options binding, boot reporting) — the current docs describe a deleted
   type system (BaseKoanAdapter etc.). Do not restructure the project itself (that is a
   separate frontier task).
VERIFY: build + Core suites green.
```

---

## Track H + §8 — DX system

### H1 · `dotnet new` templates 〔T3〕

```text
FRONTIER TASK: Create the koan-web (and koan-console) dotnet new templates per
docs/assessment/05-strategic-position.md §8.4: canonical 4-line Program.cs, one entity + one
EntityController, Sqlite connector, README block printing the JSON defaults (camelCase,
ignore-nulls, Newtonsoft) and next steps. Decide template packaging (template pack project under
templates/), wire into packaging + release. The template IS the onboarding fix — design choices
here ripple, hence frontier.
```

### H2 · Snippet lint to 100% 〔T2〕

```text
TASK: Extend scripts/validate-code-examples.ps1 coverage to ALL user-facing docs: README.md,
docs/index.md, docs/getting-started/**, docs/architecture/principles.md, samples/README.md,
.claude/skills/**/SKILL.md. Read the script first; add the paths to its file set; run it; FIX
any snippet it flags by correcting the snippet against real source (grep the APIs — never
adjust code to match a doc). Wire the expanded scope into green-ratchet.ps1 and the B2 PR gate.
DONE WHEN: the lint passes on the full set and is part of the ratchet.
```

### H3 · Docs IA collapse 27 → core dirs 〔T2〕

```text
TASK: Restructure docs/ per 05 §8.2. MOVES (git mv; fix inbound links with grep after each):
- proposals/, implementation/, specifications/, patterns/, research/, sessions/, qa/,
  refactoring/, testing/, migration/ (except any v0.x-to-v0.y user guides → guides/),
  external/, templates/ → docs/archive/<same-name>/
- how-to/embeddings.md → merge-check against guides/embedding-best-practices.md, then archive
- prior-art/ → docs/archive/defensive-publications/
- workbooks/ → engineering/ (merge)
- design/ → keep ONLY files touched in the last 60 days (git log -1 --format=%ad per file);
  archive the rest
- Root-level loose files (_inventory.md, ARCHAEOLOGY.md, ASPIRE-INTEGRATION.md, AI-SOURCE-*.md,
  OLLAMA-MODEL-VALIDATION.md) → docs/archive/root-notes/ (ASPIRE-INTEGRATION.md: first grep for
  inbound links from active docs; if linked from getting-started/reference, move to guides/
  instead)
VERIFY: scripts/docs-lint.ps1 (link check) green afterwards — fix every broken link it reports.
DONE WHEN: docs/ top level = getting-started, guides, reference, decisions, architecture,
assessment, support, engineering, api, case-studies, archive.
```

### H4 · Pillar map cards 〔T3 drafts → T1 maintains〕

```text
FRONTIER TASK (one card then reusable): create docs/reference/cards/<pillar>.md one-screen map
cards (what it does · the one canonical pattern · ≤5 attributes you'll use · the escape hatch ·
the sample that shows it) for: data, web, cache, jobs, vector, ai-data, mcp, auth. Source each
claim from the pillar's evidence JSON + current code. After the first card is approved, the
remaining seven become T2 template work.
```

### H5 · Glossary 〔T2〕

```text
TASK: Create docs/reference/glossary.md defining (one short paragraph each, with a code anchor):
entity, partition, set, source, adapter, connector, capability/CapabilitySet, pushdown,
residual, provenance, boot report, registrar/KoanModule, Reference=Intent, lane, gate (jobs),
ledger, coherence, fresh-or-null, ambient context. Each definition must cite the defining type
(file path). Link the glossary from docs/index.md and getting-started/overview.md.
```

### H6 · Verb alias sweep 〔T2〕

```text
TASK: The canonical verbs are decided (principles.md §5): Save, Remove, Query. Sweep ALL docs +
skills + sample READMEs for the non-canonical synonyms used as if canonical (Upsert presented
as the primary save verb, Delete as the remove verb, Where as the query verb) and normalize the
PROSE/snippets to canonical. DO NOT touch C# source code or rename any API — this is a docs
pass only. (The [Obsolete] alias work on the API itself is a separate frontier task.)
VERIFY: H2's snippet lint green.
```

### H7 · llms.txt 〔T2〕

```text
TASK: Create /llms.txt (root): ≤200 lines. Contents: the three-beat story (from README), the
canonical Program.cs + entity + controller + verbs snippets (copy EXACTLY from
docs/getting-started/overview.md — do not retype), the 8-concept budget list, the anti-pattern
list (from CLAUDE.md), the canonical-way table (bootstrap=KoanModule/AddKoan, serializer=
Newtonsoft camelCase, scheduling=Jobs, realtime=SSE), and pointers: CLAUDE.md, .claude/skills/,
docs/getting-started/overview.md, docs/assessment/00-overview.md. Add a line to README's
"Learn it" table pointing agents at it.
```

### H8 · Skills refresh + koan-jobs skill 〔T2〕

```text
TASK: 1) Create .claude/skills/koan-jobs/SKILL.md modeled on koan-caching's structure: the
IKoanJob<TSelf> pattern (copy the verified BenchmarkJob shape from
samples/S14.AdapterBench/Jobs/BenchmarkJob.cs), the .Job/.Jobs accessors, the six attributes,
JobContext verbs, the capability ladder, §17 write-safety, the §19 conveyor rule — source:
CLAUDE.md's Background Jobs section + docs/guides/jobs-howto.md. 2) Add the skill to the
pattern-recognition table in CLAUDE.md and .claude/skills/README.md. 3) Sweep all skills for
`0.6.3` version pins and stale package ids (Koan.AI.Ollama → Koan.AI.Connector.Ollama etc.) —
fix against reality (grep the csproj names).
VERIFY: every snippet in touched skills passes H2's lint.
```

### H9 · Boot report: surface failures + provenance 〔T2 — after F1〕

```text
TASK: Extend the boot report (src/Koan.Core/Hosting/Runtime/AppRuntime.cs +
KoanConsoleBlocks.cs) to render: (1) a MODULES-FAILED block from the F1 failure channel (red,
module + exception type + first line of message); (2) move the raw Console.WriteLine
'ASSEMBLIES|loaded=...' + JSON dump (AppBootstrapper.cs ~:151-170) behind KOAN_VERBOSE_ASSEMBLIES
so stdout stays human; (3) fix the Health line race: print 'probes pending' instead of a racy
overall=Unknown when StartupProbeService hasn't completed. Read all three files fully first.
VERIFY: run samples/S1.Web, paste the new boot output in your summary; Bootstrap suite green.
```

---

## Strategic capabilities (05 §3) — all T3

> **Second-act capabilities** (05 §3.1 — composition lockfile, governed agent access,
> conformance kits, tenancy, sovereign deploys, AI evals, agent ops) have their own
> design-shape cards in **[07-strategic-prompt-stash.md](07-strategic-prompt-stash.md)**.
> As each ADR lands, append its minted T1/T2 implementation cards back here.

```text
S1 FRONTIER: Write the ADR for dev-mode MCP introspection ("the app explains itself to
agents"): a small package exposing entities/adapters/negotiated-capabilities/boot-report as MCP
resources, riding the existing Koan.Mcp transports + provenance registry. Scope: read-only,
dev-mode default, production opt-in. Then implement behind it.

S2 FRONTIER: Produce the wedge-demo artifact: a recorded agent-session transcript building a
working multi-provider AI app on Koan (entity → REST → postgres swap → [Embedding] semantic
search → [McpEntity]), referenced from README. Requires H1 (template) + A/B tracks landed.

S3 FRONTIER: AI pillar consolidation (19 → ~8 projects) per 04 Track C "AI verticals" row:
one contracts home (absorb Contracts.Shared + Koan.Core/AI SPI), fold Orchestration+Agents,
keep Prompt (load-bearing), demote Training/Eval/Compute to contracts-or-parked, decide
Models+HuggingFace together, park the ZenGarden AI connector. Also migrate AI capability
declaration to ARCH-0084 tokens. Run as its own mini-plan with an ADR.

S4 FRONTIER: Web.Auth.Roles + auth-connector consolidation; Data.Direct fold-or-cut;
Vector<T>/VectorData<T> facade merge + workflow-subsystem cut — each needs a public-surface
decision; bundle as one "auth+data surface trim" ADR session after E5.
```

---

## Coverage map (issue → prompt)

| Assessment item | Prompt |
|---|---|
| sln gap / CI off / release untested | B1 / B2 / B3 |
| ADR status lies · stale docs · litter | A1 / A2 / A3 |
| Debris dirs & ghost samples | C0 |
| Zero-consumer cuts/parks | CUT-TEMPLATE C1–C14 |
| Scheduling / MEDIA-0008 / web halo | C17 / C19 / C20a-c |
| Kernel inversion · orch. dead surface | D1 / D2 |
| Dual registration · V1 discovery · singleflight · health dupes | E1 / E2 / E3 / E4 |
| Auth OIDC-501 + flow engines | E5 |
| ES/OS twins | E6 |
| Messaging reflection + dead surface | E7 |
| Capability parity drift | E8a-c |
| EntityContext doc contradiction | E9 |
| Silent boot swallow + 243 catches | F1 / F2 |
| Koan.Core diet | G1 / G2 |
| Templates · snippet lint · IA · cards · glossary · verbs · llms.txt · skills · boot report | H1–H9 |
| Agent-native capabilities · AI/auth consolidations | S1–S4 |
