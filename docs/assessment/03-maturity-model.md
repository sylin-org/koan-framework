# Stage 3 — Maturity model

**Date**: 2026-06-10 · **Method**: synthesis of Stages 0–2 evidence into an explicit ladder;
scores adversarially reviewed in Stage 4. The ladder is calibrated to the framework's *own* canon
(dogfood ≥2 usages, ARCH-0079 integration tests, fail-loud, ARCH-0084/0086 idioms) so a level is
a statement about *this* framework's standards, not a generic CMM.

## The ladder

| Level | Name | Gate (all required) |
|---|---|---|
| **L0** | Debris | Nothing real — empty dirs, orphan files, tests of deleted code. *Action: delete.* |
| **L1** | Experimental | Real code, but fails the dogfood bar (0–1 consumers) **or** has no integration coverage **or** is a facade over capabilities nothing provides. *Action: decide — promote, park, or cut.* |
| **L2** | Functional, pre-canon | Consumed and working, but carries pre-renovation idioms (legacy registrars, AppDomain scans, reflection dispatch, swallowed failures), weak tests, or drifted docs. *Action: renovate or shrink.* |
| **L3** | Settled | Post-redesign architecture (capability model / KoanModule where applicable), ARCH-0079 integration specs, ≥2 real consumers, accurate ADR trail, fail-loud behavior. *The consolidation plan's "settled" definition.* |
| **L4** | Hardened | L3 **plus** enforced gates (tests run in CI on every merge), release fidelity (published packages, truthful versions/docs), and a failure-mode story (no silent swallows on its paths). |

> **Nothing in the repo is L4 today, and that is a system property, not a pillar property:** CI is
> disabled, 39/87 test projects are outside the solution gate, public packages are stale under a
> prefix the docs never mention, and boot-path failures are silently swallowed. The ceiling is
> currently set by the *enforcement substrate*, not by code quality.

## Pillar placements

### L3 — Settled (the proof the target is reachable)

| Pillar | Score | Evidence / residual debt |
|---|---|---|
| **Cache** (7 proj) | **L3** — the reference | Three-band layering, invariant enforced in one place, boot self-check, CrossEngine oracle, explicit ADR supersession. Residue: Gen-1 registrar chain (~350 LOC), 3 projects missing from sln. |
| **Jobs** (Jobs + Transport.Messaging) | **L3** | Freshest code; 5-tier shared TestKit; KoanModule; CAS + TTL capability-graded. Residue: transport spec is fake-proxy only (no broker integration). |
| **Data inner ring** (Abstractions, Core, Relational) | **L3** | One capability vocabulary, one filter AST, one pushdown owner, DATA-0100 encoding contract; canon-grade tests. Residue: Entity.cs 948-line / ~60-static surface, dual naming generations (~16 types), dead Relational `Schema/`, duplicate EntityMetadataProvider. |
| **Data connectors: Mongo, Sqlite, Postgres, SqlServer, InMemory, Json** | **L3** | Uniform anatomy, FilterConvergence oracle, richest capability sets. Residue: capability-declaration drift (Postgres BulkUpsert unadvertised), copy-pasted enums, dual registration paths, 1,732-line SqliteRepository, 23 silent catches in one file. |
| **Vector core + Weaviate, Qdrant, ES, OS** | **L3−** | Residual-is-error invariant, uniform 25-spec matrix. Residue: 3 naming conventions, Abstractions→Core layering violation, zero-adopter VectorWorkflow sub-surface, OS matrix status stale. |
| **Web nucleus** (Web + Extensions) | **L3−** | EntityController→EndpointService→hooks spine, AdapterSurface ×8 integration matrix. Residue: Type.GetType shims, namespace/type-name collisions, Koan.Web→Scheduling reference, Extensions misnamed. |
| **Security.Trust** | **L3** | Best-engineered small project; ADR-section citations per file; unit + E2E via KoanIntegrationHost. Residue: docs claim ES256 ahead of implementation (deferred-by-design, but stated). |
| **Mcp** | **L3−** | Consumed (S16 + tests + fixtures), ADR-backed. Residue: duplicate ICodeExecutor interfaces, dual registrars. |

### L2 — Functional, pre-canon (live strata the renovation hasn't reached)

| Pillar | Score | Why not L3 |
|---|---|---|
| **AI core** (Contracts, AI, Data.AI, Ollama, LMStudio, AI.Web) | **L2.5** | Heavily consumed + tested (151 + 74 tests), but: three capability mechanisms in one pillar (pre-ARCH-0084), contracts in three homes, dead `Pipelines/` surface, duplicate bridge class, stale READMEs, Ollama→ZenGarden dep. |
| **Canon** (Domain + Web) | **L2.5** | Real, tested, sampled; drags 3 fossil dirs + orphan test project; "Domain" name only existed to disambiguate against a now-deleted Core. |
| **Admin pair** (Admin + Web.Admin) | **L2.5** | Consumed by 4 samples, has a Suites test project; headless split justified by a console UI that was never built. |
| **Storage core + Local connector** | **L2.5** | Clean contracts, moderate tests; bespoke 4-bool capability record predates ARCH-0084; contracts live inside the impl package (forces Media.Abstractions to drag the whole pillar). |
| **Media** (Abstractions, Core, Web) | **L2.5→L3−** | Exceptional test suite (~50 specs) and clean Media↔Storage layering, but: scheduled MEDIA-0008 deletion never executed, hallucinated READMEs, dead Koan.Web reference, 1,509-line MediaPipeline. |
| **Web.Auth** | **L2** | Live flow engine is OAuth2-only without PKCE; **OIDC callback returns 501 while the OIDC connectors ship**; two flow engines + two extensibility generations; no integration test of the central flow (exactly the bug class ARCH-0079 exists to catch). |
| **Messaging.Core + RabbitMq** | **L2** | Load-bearing (cache coherence + jobs transport ride it) but pre-renovation: per-send reflection, AppHost.Current locator, static registries, dead surface, 11 ADRs describing deleted features, **zero broker-backed tests — acknowledged canon violation**. |
| **Data.Backup + Web.Backup** | **L2−** | Works, single consumer chain; AppDomain scan the registry was built to replace; 323-line legacy registrar; fictional README; ~no tests for 5k+2.8k LOC. |
| **ZenGarden bridge** (+Core) | **L2** | Tested, documented satellite bridge — but a periphery product's contracts are load-bearing inside mainline adapters, and the client is a 119,907-byte single file. |

### L1 — Experimental / aspirational (decide: promote, park, or cut)

| Cluster | Score | Evidence |
|---|---|---|
| **AI verticals** (Models, Prompt, Orchestration, Agents, Review, Eval, Training, Compute, Contracts.Shared, HF + ZenGarden connectors) | **L1** (Prompt **L1.5**) | All born 2026-05-16; near-zero consumption (S18 references all, uses only Review.ReviewStatus); **Training/Eval can only throw** — no in-repo provider; ZenGarden connector not in sln. Prompt is self-contained, complete, and structurally load-bearing (Koan.AI depends on it). |
| **Rag + Rag.Abstractions** | **L1** | 8k LOC, zero consumers, InternalsVisibleTo a nonexistent test project; competes with the documented Data.AI RAG path. |
| **Secrets vertical** (Abstractions, Core, Vault) | **L1** | Dormant-complete: clean, documented, reflection-wired into boot — zero tests, zero consumers. |
| **ServiceMesh + Translation + Inbox.Redis** | **L1 / L0** | No ADR mentions "service mesh"; zero tests; Translation has hardcoded confidence values; Inbox's client API no longer exists in src (**L0**). |
| **Cqrs + Mongo outbox** | **L1** | Zero consumers/tests; superseded by the Jobs ledger outbox; pre-ARCH-0076 decoration. |
| **Data.Direct** | **L1** | Zero consumers anywhere despite CLAUDE.md documentation; modernized boot module but nothing dogfoods it. |
| **Scheduling** | **L1** | Frozen at "Phase 1" (cron never shipped); 4 of 10 contracts unreferenced; zero tests; strictly subsumed by JOBS-0005. |
| **Recipe pair** | **L1** | One 33-line stub consumer; uses the AppDomain-scan idiom the framework deprecated; superseded by KoanModule. |
| **Web splinters** (Transformers, Json.Strict, WebSockets, GraphQl) | **L1** | Combined real consumption: one archived sample. WebSockets has more test LOC than consumers. |
| **Web.Auth.Roles / Auth provider connectors** | **L1** | Roles: zero consumers. Google/Microsoft connectors configure a flow that 501s; Oidc connector is an empty shell. |
| **Tagging** | **L1** | Competent micro-library, zero in-repo consumers, foreign app conventions in its docs. |
| **Storage replication + ResilientStorageDecorator; S3 connector** | **L1** | ~2.2k LOC of distributed-correctness code with zero tests; S3 is ZenGarden-entangled, buffers whole objects in memory, untested. |
| **PGVector connector** | **L1→L0** | Doesn't compile (csproj says so); out of sln; fix parked on a branch. |
| **Orchestration stack** (7 proj) | **L1 by decree** | ARCH-0077 condemned it; tests already deleted; samples bypass it. Code grooming is irrelevant — the level reflects its status. |

### L0 — Debris

The full ledger is in [01-cartography.md §5](01-cartography.md): 7 tombstone src dirs, 8 ghost
sample dirs, 5 orphan test trees, `.lscache` files, repo-root litter, dead `InternalsVisibleTo`.

### Supporting corpora

| Corpus | Score | Notes |
|---|---|---|
| ADR corpus (content) | **L3** | Gen-2 ADRs are exceptional; the working memory of the project. |
| ADR corpus (hygiene) | **L1.5** | Status fields untrustworthy (superseded ADRs still "Accepted"); 16 prefixes vs 6 sanctioned; index covers 14/279. |
| Guides + skills | **L2** | Load-bearing and current near the redesign frontier; stale elsewhere; skills pin v0.6.3-era assumptions; no jobs skill despite triggers met. |
| Front-door docs (README, principles, getting-started, samples catalog) | **L1** | 25/59 claims FALSE; false validation stamps; phantom samples; ghost APIs. *Actively harmful — worse than absent.* |
| Test platform (design) | **L3.5** | Oracles, TestKits, KoanIntegrationHost — top-decile design. |
| Test execution / CI | **L1** | CI disabled; sln misses 45% of test projects; release gates on build only. |
| Versioning / release | **L1** | Three coexisting generations; the active GitHub workflow drives the deprecated one; public feed stale at 0.8.x; package-ID prefix undocumented. |

## The system-level placement

On a classic technology maturity curve — *prototype → feasibility → consolidation → product →
ecosystem* — Koan is **at the start of consolidation, with the feasibility phase's exhaust still
on the books**. Concretely:

- The **feasibility test succeeded**: the core bet (entity-first + Reference=Intent + capability-
  graded multi-provider) is implemented, tested, and dogfooded across five non-trivial apps. The
  consolidation-era pillars (Cache, Jobs, Data ring) prove the team can produce settled,
  reference-quality architecture *when a pillar gets the full treatment*.
- The **renovation is ~⅓ done by surface**: 90:7 legacy-registrar:KoanModule ratio; three module
  generations, two discovery pipelines, two auth engines, two schema generations still coexist.
- The **enforcement substrate lags the code**: the green ratchet is voluntary and local; nothing
  external gates a merge or a release. Until that flips, every L3 placement is perishable —
  S7.Meridian's silent rot (restored by hand after months outside the sln) is the proof.
- The **public face is below the line**: a newcomer cannot install it, the front-door docs fail
  verification, and the version story contradicts itself. For a framework whose stated ambition is
  external adoption, this is currently the single largest gap between self-image and reality.

**Overall: L2 system with L3 islands and an L1 public face.** The path to "clean, lean
architecture — less but more meaningful parts" does not require new invention; the L3 islands
already define the pattern. It requires *finishing*: executing the cuts already authorized,
driving the migrations already designed, and making the gates already built mandatory.

---

*Next: [04-recommendations.md](04-recommendations.md).*
