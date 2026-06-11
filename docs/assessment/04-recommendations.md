# Stage 4 — Recommendations toward "less but more meaningful parts"

**Date**: 2026-06-10 · **Method**: synthesis of Stages 0–3, with the highest-risk recommendations
adversarially reviewed by independent verifiers (verdicts + evidence in
[evidence/stage4/](evidence/stage4/); four reviews completed, four spot-verified inline). Where a
reviewer corrected the draft, the corrected form is what appears below.

## §0 Relationship to the in-flight plan

This assessment **extends** `docs/architecture/foundation-consolidation-plan.md` rather than
replacing it. Facet 1 (capability model) is complete; Facet 2 (KoanModule) settled 2026-06-03 as
*additive/opportunistic*. One amendment is proposed, with adversarial review backing:

> **Split Facet 4 into 4a (cuts) and 4b (merges). Start 4a now, in dependency-ordered waves;
> keep 4b after Facet 3 (ambient context).**
> Rationale that *survived* review: the Wave-1 cut set is genuinely delete-only, is mostly inside
> Koan.sln (so the existing green ratchet gates it), removes ~36% of legacy registrars, ~20% of
> `AppHost.Current` sites, and a large slice of the false front-door claims *before* Facet 3
> touches the tree. Rationales that **did not survive** review and must not be used: "shrinks the
> KoanModule migration" (Facet 2 is additive — no registrar is ever obliged to migrate) and
> "deletion is risk-free" for the Orchestration stack (ARCH-0077 is a *migration*, not a cut —
> see Track D).
> The plan's original "Facet 4 harvests Facet 2" rationale was found to apply to **merges**, not
> deletions — and the plan already front-runs deletions in practice (§3a ghost dirs, Facet-1 cuts).

Six tracks. A–B are days-to-a-week and independent; C–F are the consolidation work proper;
G–H are continuous programs.

---

## Track A — Truth restoration (days; highest leverage-to-cost in the repo)

The public face fails verification (25/59 front-door claims FALSE; see 02 §2). Until this is
fixed, every other investment is discounted by a front door that misleads.

1. **README rewrite, truth-first**: real install commands (`Sylin.Koan.*`, one package per
   `dotnet add package` invocation), real version badge (NBGV 0.17.x), delete the ghost APIs
   (`Flow.OnUpdate`, entity-static `SemanticSearch`, `Koan.Messaging.InMemory`, `Post/Put`
   overrides), fix the 404 clone links (org is `sylin-org`), and sell the *actual*
   differentiators (02 §8): capability model, source-gen discovery, Jobs ladder, [Cacheable]
   coherence, the honest capability-graded multi-provider story.
2. **Package fidelity**: publish current `Sylin.*` packages (or stop linking nuget.org until
   then); de-list or deprecate the accidental unprefixed 0.5.2 relics (`Koan.Core`, `Koan.Web` —
   they *silently succeed* and strand newcomers 13 months in the past); document the `Sylin.`
   prefix in every install snippet. Update nuspecs (`net9.0` → current TFM; `sylin-labs` URLs).
3. **principles.md**: rewrite against the real surface or stamp it `status: historical`. Eight of
   its core snippets don't compile; its "validated" stamp is false. The replacement should state
   the consolidation-era canon (fail-loud, capability tokens, KoanModule, dogfood discipline).
4. **Samples truth**: regenerate `CATALOG.md` from `Koan.sln` (delete the four phantom "In
   Development" samples; list the five hidden real ones); delete the 8 ghost dirs; add the
   missing **"clone → open Koan.sln → run S0/S1" path — currently the only working onboarding
   sequence, and it is documented nowhere**; give S18.Prism a README; promote the 4-line
   canonical Program.cs from `status: draft` to normative and make every in-sln sample comply
   (remove the redundant `AppHost.Current ??=` / `AsWebApi()` incantations).
5. **ADR status sweep**: mark superseded ADRs (JOBS-0001/2/3, MESS-0021..0029/0070/0071,
   FLOW family + ARCH-0053 + WEB-0050/0060, OPS-0050 "Superseded by JOBS-0005; Phases 2–3 never
   implemented", ARCH-0046 → ARCH-0086, DATA-0060/0085 reconcile, ARCH-0060 "reaffirmed by
   0075"); adopt one status vocabulary; add a docs-lint rule so a `Superseded-By` header is
   mandatory when a successor lands.

## Track B — Enforcement substrate (week; parallel with A)

The ceiling on every maturity score is the substrate (03 §ladder note). This track protects the
keep-set through everything that follows. *Adversarial note: sln repair is **not** a precondition
for the Track C cuts (verified: zero out-of-sln test projects reference any cut candidate) — run
B and C in parallel.*

1. **Solution truth**: regenerate `Koan.sln` to include all 87 test projects (39 currently
   invisible to the ratchet — including most FilterConvergence connector suites, the Web
   AdapterSurface matrix, Media, Storage, Cache Analyzers/CrossEngine). `scripts/regenerate-sln.ps1`
   exists. Adopt the rule: *in the repo ⇒ in the sln* (samples included — S7.Meridian's silent
   rot is the cautionary tale).
2. **CI re-enable**: PR gate = build + non-container suites; nightly = full container-gated
   matrix; release gates on green, not on build. The local green-ratchet legs (docs-lint,
   doc-example compilation) move into the PR gate — they're already scripted.
3. **One versioning generation**: make the release workflow NBGV-native (ARCH-0085); delete
   `build/versions.props` machinery and the `Update-Versions.ps1` path the *active* GitHub
   workflow still drives. Today the release path and the in-repo canon disagree.

## Track C — Facet 4a: the cut waves (delete-only first, then unwire-then-cut)

Dispositions verified against csproj references, reflection-activation greps, meta-package
nuspecs (no cut candidate ships in `Sylin.Koan`/`Sylin.Koan.App` — verified), and test topology.

**Wave 0 — debris (zero risk, an afternoon)**: the full L0 ledger in 01 §5 — 7 tombstone src
dirs, 8 ghost sample dirs, 5 orphan test trees, `.lscache` files, repo-root litter, dead
`InternalsVisibleTo` grants.

**Wave 1 — verified zero-inbound cuts/parks**:

| Item | Disposition | Notes |
|---|---|---|
| Koan.Data.Cqrs + Mongo outbox | **Cut** | Superseded by Jobs ledger outbox; zero consumers/tests. Mark DATA-0019 superseded. |
| Koan.Data.Direct | **Fold into Koan.Data.Core or cut** | Interfaces/options already live in Core; zero consumers despite CLAUDE.md listing (remove the listing either way). |
| Koan.WebSockets | **Cut** | Zero consumers; SSE won every realtime use. Re-create as Koan.Web.Realtime only when a consumer exists. |
| Koan.Web.Json.Strict | **Cut or absorb as a KoanWebOptions switch** | Serializer decision is recorded ([05 §6.1](05-strategic-position.md)): Newtonsoft is canonical (polymorphism ergonomics); the STJ island is the part that goes. Document the camelCase/ignore-nulls defaults loudly. |
| Koan.Web.Connector.GraphQl | **Attic** (archive branch/tag) | Sole consumer is an archived sample; carries a CVE-treadmill dependency for nobody. |
| Koan.Web.Auth.Roles | **Fold into Koan.Web.Auth or cut** | Zero consumers; Web.Auth already hosts the contributor contracts its bootstrap options feed. |
| Auth connectors Google/Microsoft/Oidc | **Cut until OIDC completes** (Track E fixes the 501) | They configure a flow that cannot finish; Oidc is an empty shell. Keep Discord only if OAuth2-on-live-path is wanted; keep **Test** (the only exercised one). Collapse survivors into one `WellKnown` defaults package; delete the contradictory duplicate descriptor attributes. |
| Recipe pair | **Cut** | Fold ObservabilityRecipe's ~10 useful lines into Koan.Web defaults; mark ARCH-0046 superseded by ARCH-0086. |
| Secrets vertical (3 proj) | **Park out of tree or demote** | Reflection hook in Data.Core is soft (`throwOnError:false` + catch — verified; cutting cannot break Core). Dogfood-or-demote: if secrets matter for the trust-fabric direction, re-enter via KoanModule with a Vault Testcontainers spec; until then it's dormant weight. |
| ServiceMesh + Abstractions + Translation + Inbox.Redis | **Retire** (Inbox is L0 — its client API no longer exists) | No ADR, no tests. Web.Admin's reference is a defensive `GetService` (safe to remove). If mesh returns, it returns through the trust-fabric/ZenGarden direction with an ADR. |
| Koan.Tagging | **Park/move — do not delete** | Quality is good but it has an *external* downstream consumer and zero in-repo usage; per the dogfood rule it doesn't earn framework residency. Move to the consumer's side or a satellite repo; scrub the foreign conventions + dangling ADR citation from its docs if it stays. |
| Koan.Rag + Rag.Abstractions | **Park as explicit incubator or cut** | 8k LOC, zero consumers, no tests; competes with the documented Data.AI RAG path. Cut order note (verified): Rag references AI.Orchestration — cut Rag first or together. |
| AI verticals | **Collapse 19 → ~8** | Keep: Contracts (absorb Contracts.Shared + the Koan.Core/AI SPI → **one contract home**), Koan.AI (+ fold Orchestration & Agents in or into one Composition package), **Koan.AI.Prompt stays — verified load-bearing** (Koan.AI depends on it), Data.AI, AI.Web, Ollama, LMStudio. Models keeps real backing (Ollama/HF) — keep *with* HuggingFace or park both together (HF's only purpose is Models). **Training/Eval/Compute: demote to contracts or park** — facades that can only throw are worse than absent (no-stopgaps rule). ZenGarden AI connector: not in sln, zero consumers — park until the ZenGarden integration ships. Delete dead surfaces: `Pipelines/`, `PipelineAiExtensions`, duplicate `MediaAnalysisEmbeddingBridge`, the two stale AI test husks. |
| PGVector | **Fix-or-remove decision now** | Doesn't compile, out of sln, fix parked on a branch; it also artificially keeps `IVectorFilterTranslator` alive. |
| Storage ResilientStorageDecorator | **Cut** | Self-described "Legacy — superseded by Mode=Replicated"; untested; carries app-specific contract leakage. Replication subsystem: **test-or-demote** (ARCH-0079 spec or experimental flag — ~1k LOC of untested distributed-correctness code must not sit silently in the pillar core). |
| Vector extras | **Cut** | VectorWorkflow/Profiles sub-surface (zero adopters; collapses `SaveWithVector` to one path), `IVectorFilterTranslator`, `VectorEmbeddingAttribute`, merge `Vector<T>`/`VectorData<T>` into one facade. |

**Wave 2 — unwire-then-cut chains**:

1. **Koan.Scheduling** — full verified recipe in [evidence/stage4/scheduling-cut.json](evidence/stage4/scheduling-cut.json):
   migrate the two consumers to `[JobAction(Schedule="@boot", Timeout=..., MaxAttempts=1)] +
   [JobIdempotent]` (S5 seeding; KoanContext maintenance — which gains the recurring cadence
   Scheduling's unimplemented cron never delivered); **drop** the `/.well-known/scheduling`
   endpoint (zero consumers — do not re-point; that mints surface for nobody) and the
   `Koan.Web → Koan.Scheduling` csproj reference (today *every web app* hosts a 1-second poll
   loop for zero tasks); clean the Koan.Core constants residue. Two deliberate behavior changes
   to state in the note: the tasks will now actually run in Production (Scheduling silently
   disabled itself outside Development and nothing ever configured it), and failures retry unless
   `MaxAttempts=1`. Nothing run-before-traffic is lost — `ReadinessGate` was a dead placeholder.
2. **S3 connector** — split the generic Minio provider (testable against MinIO Testcontainers)
   from the ZenGarden endpoint/presign add-on, or rename to make the coupling honest; fix the
   whole-object `OpenRead` buffering while touching it.
3. **Media** — execute the already-scheduled MEDIA-0008 deletion (obsolete output-cache family +
   transition shims); remove the dead Koan.Web reference from Media.Core; relocate
   ProfileMedia/ColdProfileMedia to the archived sample.
4. **Web halo merges** — OpenApi+Swagger into one package (one consumer between them); Admin pair
   into Koan.Web.Admin (the console-UI rationale was never built); fold Transformers into
   Koan.Web (deletes all three `Type.GetType` shims) — *keep activation opt-in via options so
   folding doesn't change behavior for apps that never referenced it*; rename Web.Extensions to
   its real identity (entity capabilities + authorization) and resolve the namespace/type-name
   collisions.

## Track D — Orchestration is a migration, not a cut (ARCH-0077 execution)

Adversarial review **refuted** "delete the stack now": `Koan.Orchestration.Abstractions` has 24
inbound references *including Koan.Core itself* (Koan.Core.csproj:32), every connector carries
`[KoanService]`, and ARCH-0077 (still `Proposed`) prescribes per-connector Aspire repackaging
that doesn't exist yet. Corrected plan:

1. **Accept ARCH-0077 formally** (it's been `Proposed` for 13 months of rot risk) and give it a
   staged ledger like JOBS-0005.
2. **Step 1 — break the kernel inversion now**: move the service-identity surface that
   `ServiceDiscoveryAdapterBase` needs into Koan.Core (resolving one of the three
   `KoanServiceAttribute` collisions in the same move); delete Koan.Core's and Core.Adapters'
   references to Orchestration.Abstractions, including the `MissingTypes.cs` namespace-squat.
   This unblocks the Koan.Core diet (Track G) without waiting for Aspire.
3. **Step 2 — delete the dead surface inside the stack today**: `IServiceAdapter`/`IKoanService`/
   `IDevServiceDescriptor` markers (zero implementors), the five legacy attributes `[KoanService]`
   superseded, diagnostic Koan0049A, the SelfOrchestration subsystem (it is not Aspire and ships
   test scaffolding in a production package), vestigial Aspire refs (PGVector, KoanContext),
   tombstone `InternalsVisibleTo`.
4. **Step 3 — the CLI/Generators/Cli.Core retirement** rides the Aspire hosting-package work, per
   the ADR's own phasing. Until then: merge Cli.Core into Cli (the split serves nobody —
   all-internal with `InternalsVisibleTo` only the Cli), and stop grooming the rest.

## Track E — Finish the in-flight migrations (mechanical, weeks)

The six "unfinished migration" clusters from 02 §6, plus reviewer-refined items:

1. **Adapter registration**: fold each connector's manual `XRegistration` class into its
   registrar/module — one wiring unit per adapter (10 deletions; removes a live
   double-registration divergence the Postgres code itself admits).
2. **One of each**: delete V1 `OrchestrationAwareServiceDiscovery` after migrating the 4 RabbitMq/
   Vault call sites; one singleflight primitive; delete the root duplicate `HealthAggregator`;
   one boot-report surface (Provenance absorbs Core.Adapters' `BootstrapReport`); dissolve
   `Koan.Core.Adapters` (readiness → Core health/readiness, options → Modules, reporting →
   Provenance) — its defining abstraction was already deleted and its docs are fiction.
3. **Auth**: adopt the ASP.NET OIDC/OAuth handlers that the dead `AddKoanWebAuthAuthentication`
   path already configures — this *simultaneously* fixes the OIDC-501 dead end, adds PKCE, and
   deletes ~300 LOC of bespoke flow code; retire the legacy `IKoanAuthEventContributor`
   generation (migrate the two built-ins); excise the SAML stub surface; deduplicate the 3×
   ProviderOptions merge logic; add the missing ARCH-0079 integration spec for the central flow
   against the Test provider — the exact bug class that let OIDC-501 ship.
4. **ES/OS** — *reviewer-corrected design* ([evidence/stage4/es-os-merge.json](evidence/stage4/es-os-merge.json)):
   do **not** collapse to one package (Reference=Intent + per-backend `[KoanService]` container
   metadata require two thin packages). Extract the byte-identical skeleton into the existing
   `Koan.Data.SearchEngine` assembly with a 3-member dialect seam (search-body builder,
   index-body builder, similarity-token map — *not* auth, which is identical); implement
   ExportAll/GetCount/IndexStats once (fixing OS's silently-lost migration capability); fix the
   four drift bugs the twins already accumulated (OS factory cross-claims provider `elastic` at
   equal priority — a live ambiguity bug; options drift; missing OS provenance; dead ES options);
   supersede DATA-0097 §6, whose "transport wiring differs" rationale the code refutes. ~1.0–1.1k
   LOC saved, zero project-count change, two real bugs fixed.
5. **Messaging**: slim Messaging.Core to its real demand (delete `IMessageBusSelector`,
   `IQueuedMessage`+unreachable path, zero-registration `MessagingTransformers`; cache the
   per-send reflection dispatch into typed delegates); **either** add an in-memory provider +
   broker-backed RabbitMq suite (closing the acknowledged ARCH-0079 violation) **or** demote the
   pillar's claim to "RabbitMq integration package". Longer-term: extract the shared cross-node
   signal primitive (Jobs' `JobReadySignal`, Cache coherence channels) — `ICoherenceChannel<T>`
   already documents its own promotion plan, and the trust-fabric revocation direction is the
   second consumer that triggers it.
6. **Capability stragglers**: migrate the AI pillar's three capability mechanisms to ARCH-0084
   tokens (design-needed — the largest un-migrated surface); replace Storage's bespoke 4-bool
   record with capability tokens; close adapter parity drift (Postgres declares BulkUpsert it
   already implements; Couchbase gets ConditionalReplace/FastRemove or an ADR note; Redis stops
   advertising `FilterSupport.Full` for scan-all, or gains cursor paging + native TTL — or is
   demoted to cache/coherence duty only).
7. **Data naming + Entity surface**: finish DATA-0086 (one naming pipeline, ~16 types → ~5);
   retire the partition-string overload matrix in favor of `EntityContext.Partition()` scoping
   (it is verified sugar over one path — the cut is surface, not behavior); fix the
   EntityContext doc/code contradiction (inherit-vs-replace).

## Track F — Fail-loud boot (reviewer-refined policy)

Full verdict in [evidence/stage4/fail-fast.json](evidence/stage4/fail-fast.json). The draft
("fail-fast + KoanException hierarchy") was half-refuted; the corrected policy:

- **Two tiers.** Tier A (stays lenient): assembly-closure loads and manifest type-scan skips —
  absence of a loadable dependency is never fatal (Spring `@ConditionalOnClass` precedent).
  Tier B (**fail-fast by default**): an exception escaping a module's `Initialize()/Register()`,
  and failure of the manifest invoker itself (which today can silently no-op the *entire
  framework*). This is a **consistency fix, not a philosophy change**: the framework already
  shipped fail-fast-by-default for startup services (`FailFastOnStartupFailure=true` with
  per-service `ContinueOnFailure` opt-out) — AppBootstrapper is the internal inconsistency.
- **One new exception type, not a hierarchy**: sealed `KoanBootException` {module, assembly,
  phase, inner} — justified solely because no ILogger exists at that point. **No**
  `KoanDataException` provider-wrapping: message-enrichment at resolution boundaries is the
  established (and praised) pattern; wrapping would touch 11+ connectors' hot paths and break
  user catches of provider exceptions; ARCH-0080 deliberately keeps connectivity out of boot.
- **Escape hatches**: per-module opt-out (KoanModule virtual / attribute, mirroring
  `[KoanStartupService(ContinueOnFailure=true)]`), then a global `KOAN_BOOT_LENIENT=1` /
  `AddKoan(o => o.LenientBoot = true)` — env-var based because no IConfiguration exists yet.
- **Both modes log**: Console.Error + a MODULES-FAILED block in the boot report (the
  `RegistrySummarySnapshot` channel already exists and just isn't wired).
- **The ~20-line first fix ships now** (log + rethrow-unless-lenient in AppBootstrapper.cs:112);
  the swallow-census burn-down (243 sites; worst: SqliteRepository ×23,
  AdapterConnectionResolver's silent config-typo fallthrough, EntityContext transaction dispose)
  proceeds as a tracked metric, not a big bang.

## Track G — Koan.Core diet

(Core-diet reviewer was lost to the spend limit; the tension it would have probed is resolved
here explicitly.) The "count intents, not projects" principle *supports* the split: the plan's
own merge test is "a distinct intent a developer would reference alone", and observability is
exactly that; the metric that matters is the **mandatory** surface (Koan.Core: 224 public types,
73 statics, 5 unconditional OpenTelemetry packages including AspNetCore instrumentation — paid
by every console worker on day one).

1. Extract OTel wiring + ObservabilityOptions into `Koan.Observability` (Reference=Intent:
   referencing it *is* the telemetry intent). `AddKoanCore` never calls it today — the
   dependency weight is pure toll.
2. Move the AI SPI (3 files) into the unified AI contracts home (Track C).
3. Cut the dead strata: fluent background-services control plane (~750 LOC, zero consumers, the
   kernel's only TODOs), root duplicate HealthAggregator pair, `EntityTransferExtensions`,
   `JsonPathMapper`; move other pillars' vocabulary (Actions/Events constant catalogs) to their
   owning pillars.
4. Decide Pipelines' home explicitly (bless-as-kernel + document, or extract) — it is currently
   an undocumented feature squatting in Core, consumed by 5 pillars.
5. Orchestration service discovery (2,460 LOC — the largest directory in the kernel) moves on
   Track D's timeline; the Step-1 inversion fix unblocks it.
6. Remove the Data.AI special-case from `RegistrySourceGenerator` (route through
   `[KoanDiscoverable]`); collapse the generator's bespoke channels onto the generic mechanism
   where descriptors allow.

## Track H — Docs & DX system (continuous)

> **Expanded by [05-strategic-position.md §8](05-strategic-position.md)** — the full premium-DX
> program (narrative spine, information architecture, snippet-truth guarantee, `dotnet new`
> templates, concept-budget discipline, voice rules, agent-facing surfaces, samples-as-curriculum,
> and its sequencing). The items below are the mechanical core; 05 §8 is the design.

1. **Docs tree 27 → ~8 dirs**: keep decisions, guides, reference, architecture, getting-started,
   support, engineering(+workbooks merged), archive. Everything stale/redundant moves to
   archive/ (the proposals dir's own `complete/`/`misaligned/` subfolders are an admission).
   Relocate the 6 loose root working files; move `prior-art/` (defensive publications) out of
   developer docs.
2. **ADR system v2**: template matching the (superior) Gen-2 practice; machine-checkable status
   header; supersession lint; regenerate the index; constrain prefixes.
3. **Skills**: add the missing koan-jobs skill (its own creation triggers are met); refresh
   version-pinned assumptions; fix the debugging skill's non-compiling sample.
4. **Samples ladder**: middle rungs for Messaging (replace the lobotomized S3.Mq), Jobs, and
   Cache as first-class teachable samples; one numbering scheme; all kept samples in sln (= CI).
5. **Boot report**: surface the collected-but-unshown provenance (capabilities per module,
   failed modules); fix the health-line race (probes typically print `overall=Unknown`).

## §9 Longitudinal metrics (re-score on each facet completion)

| Metric | Today (2026-06-10) | Direction |
|---|---|---|
| src projects | 113 (incl. debris) | → ~70 after Tracks C–D |
| Public types | 2,351 (users live in ~27%) | shrink periphery share |
| Duplicate-concept clusters | 18 (5 deliberate / 7 drift / 6 unfinished) | drift+unfinished → 0 |
| Registrar:KoanModule ratio | 90:7 | KoanModule share grows opportunistically |
| Empty catch blocks | 243 (93 files) | boot path → 0; rest tracked |
| Test projects in sln | 48/87 | 87/87 |
| CI test gate | none | PR + nightly |
| Front-door claims FALSE | 25/59 | 0 |
| Root facades in 0–1-consumer packages | 9/17 | ≤2 |

## §10 Guardrails — what NOT to do

- **Don't churn the entity-first front door.** `Entity<T>`/`Data<T,K>`/`Vector<T>`/`.Job`/
  `[Embedding]` are declared coherent by the plan and verified pleasant in practice. Surface
  *width* reductions (overload matrix) yes; semantics changes no.
- **No KoanException hierarchy; no provider-exception wrapping** (Track F evidence).
- **Don't merge ES/OS into one package**; shared core + two thin packages (Track E evidence).
- **Don't re-point the `/.well-known/scheduling` endpoint**; drop it (zero consumers).
- **Don't big-bang the KoanModule migration**; ARCH-0086 is deliberately opportunistic.
- **Don't justify cuts with the registrar-migration argument** (refuted; use the real benefits:
  surface, ambient-site count, front-door truth).
- **Don't delete the Orchestration stack ahead of its bridge** (kernel inversion first, Aspire
  packages before CLI retirement).
- **Don't ship facades over capabilities nothing provides** (Training/Eval today): contracts-only
  or parked until a provider exists — the no-stopgaps rule cuts both ways.
