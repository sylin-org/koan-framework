# Stage 7 — Strategic capability prompt stash (design shapes)

**Purpose**: prompt cards that drive the **second-act strategic capabilities** (expanding
[05 §3](05-strategic-position.md)): act one made Koan *legible* to agents; these make it
*trustworthy and verifiable* — grants, audits, conformance, composition truth. Each card carries
a **proposed API shape** and a **reference usage pattern** in deliberate Koan idiom, as the
starting hypothesis for a design session.

> ⚠️ **EVERY C# BLOCK IN THIS FILE IS A PROPOSED DESIGN SHAPE, NOT AN EXISTING API.**
> Nothing here compiles against current source, by design. This file must be **excluded from
> the snippet-compile lint**, and no session may cite these shapes as evidence of an existing
> surface. The shapes are hypothesis A for the ADR — the design session's job is to challenge
> them.

**Tier**: all cards are **T3 (frontier) for the design/ADR phase**. Each ADR's staged ledger
then yields T1/T2 implementation tasks in the style of [06-prompt-stash.md](06-prompt-stash.md)
— append them there as they're minted. Dependency notes per card.

---

## [DESIGN-PREAMBLE] — paste atop every design session in this file

```text
You are designing a new Koan Framework capability with the architect. Method (non-negotiable):

1. EVIDENCE FIRST: read the named evidence files and the named existing-code anchors before
   proposing anything. Re-derive every claim this card makes; treat the card as naive input.
2. PRIOR ART: survey the named prior art from your own knowledge; state for each what it gets
   right, what it gets wrong, and what Koan's grammar changes about the problem.
3. SHAPES: present 2-3 alternative API shapes (the card's sketch is hypothesis A, not the
   answer). For each: pros/cons, concept-count delta, failure modes. Challenge the architect's
   sketch where it deserves it — no sycophancy.
4. KOAN DX TENETS — every shape must satisfy all seven, or argue explicitly why one is waived:
   (a) entity-grammar first: new nouns are entities (queryable, observable) wherever possible;
   (b) attribute-first declaration on the entity, options for the rest;
   (c) Reference = Intent: referencing the package activates sane defaults, zero wiring;
   (d) capability-graded: behavior differences across adapters are declared tokens, negotiated,
       never silently emulated (ARCH-0084);
   (e) fail-loud: unsupported = descriptive exception, never narrowing;
   (f) self-reporting: the feature announces itself and its elections in the boot report;
   (g) concept budget: state exactly how many new concepts the app developer must learn.
5. DELIVERABLE: a Gen-2-style ADR (Status/Date/Deciders/Scope/Related; empirical probes; staged
   implementation ledger with verify steps per stage). Implementation does NOT start in the
   design session.
6. STOP: if the design wants a new project, justify it against "fewer but more meaningful
   parts" (default: the capability lives inside an existing pillar). If it drifts toward a
   refused lane (workflow engine, model ops, realtime sync, UI scaffolding) — stop and flag.
```

---

## SC1 · Composition lockfile — the behavioral SBOM 〔rank 1: near-free, compounds everything〕

**Gap & assets**: supply-chain tooling diffs *packages*; nothing diffs an app's *behavior
surface*. Koan's composition is already knowable at build time (source-gen `KoanRegistry`) and
described at runtime (Provenance, capability reports) — it just isn't serialized as an artifact.

**Proposed shape** *(design target — does not exist)*:

```jsonc
// koan.lock.json — emitted by an MSBuild target on build; checked in; diffed in CI
{
  "app": { "name": "PantryPal", "koan": "0.17.43", "tfm": "net10.0" },
  "modules": [
    { "id": "Koan.Data.Core", "version": "0.17.43" },
    { "id": "Koan.Data.Connector.Postgres", "version": "0.17.43" }
  ],
  "elections": {
    "data:default": { "adapter": "postgres", "via": "reference-priority" },
    "cache:l1": { "adapter": "memory", "preemptedBy": null }
  },
  "capabilities": {
    "data:postgres": ["Query.Linq", "Write.BulkUpsert", "Write.ConditionalReplace"]
  },
  "configKeys": ["Koan:Data:Postgres:ConnectionString"],
  "entities": [ { "type": "PantryItem", "traits": ["Cacheable", "Embedding", "McpEntity"] } ]
}
```

**Reference usage**:

```bash
dotnet build                      # lockfile refreshed automatically (Reference = Intent)
git diff koan.lock.json           # review: "this PR adds a coherence channel" is now visible
# CI gate: fail when composition drifts without the lockfile committed alongside
```

An agent reads `koan.lock.json` instead of booting the app; an upgrade tool diffs two lockfiles
and reports "0.18 removes `Write.ConditionalReplace` from couchbase — your `ImportJob` claim
path degrades."

**Prompt**:

```text
[DESIGN-PREAMBLE]
DESIGN TASK: the Koan composition lockfile. Evidence: docs/assessment/evidence/
pillar-core-bootstrap.json (Provenance, KoanRegistry), 02-philosophy-dx.md §5 (surface census);
code anchors: src/Koan.Core.Registry.Generators/RegistrySourceGenerator.cs, src/Koan.Core/
Provenance/**, AppRuntime's RegistrySummarySnapshot. Prior art: package-lock.json/Cargo.lock
(identity, not behavior), SBOM/CycloneDX (components, not composition), Terraform plan (the
diff-before-apply UX — closest in spirit), Aspire manifest (deployment topology, not behavior).
DECIDE: (1) build-time MSBuild target vs first-boot emission vs both (hypothesis: both — build
target for CI/agents, boot writes the *resolved* twin for drift detection between them);
(2) schema + stability guarantees (this becomes a public contract — version it);
(3) what is deliberately EXCLUDED (no secrets, no connection strings — config KEYS only);
(4) CI gate ergonomics (a tiny comparer the PR gate runs; where does it live — scripts/ not a
new project). Boundaries: no new pillar; lives in Koan.Core + an MSBuild .targets; no runtime
behavior change. DELIVERABLE: ADR + staged ledger (stage 1 = emit; stage 2 = comparer + CI;
stage 3 = entity/trait section fed from the registry).
```

---

## SC2 · Governed agent access — grants, audit, revocation for MCP 〔rank 2: the flagship〕

**Gap & assets**: the MCP ecosystem's security norm is all-or-nothing bearer access; enterprises
hand-roll scopes and kill switches. Koan owns every ingredient: `[McpEntity]` + transports,
the capability model, the `IAuthorize` seam (SEC-0002), Security.Trust identities (KSVID), and
coherence channels for propagation (the CAEP-epoch revocation design).

**Proposed shape** *(design target — does not exist)*:

```csharp
// Read-only by default; mutation is an explicit, auditable grant.
[McpEntity(Expose = McpAccess.Read)]                       // Query/Get only — the safe default
public sealed class Recipe : Entity<Recipe> { }

[McpEntity(Expose = McpAccess.Read | McpAccess.Mutate, Audit = true)]
public sealed class PantryItem : Entity<PantryItem> { }

// Grants are entities — queryable, revocable, observable. The Koan move.
public sealed class AgentGrant : Entity<AgentGrant>
{
    public string AgentId { get; set; } = "";              // Trust principal (KSVID)
    public string EntityType { get; set; } = "";
    public McpAccess Access { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

// Issue / revoke with the grammar you already know:
await new AgentGrant { AgentId = "kitchen-agent", EntityType = nameof(PantryItem),
                       Access = McpAccess.Mutate, ExpiresAt = DateTimeOffset.UtcNow.AddHours(8) }
      .Save();
await grant.Remove();   // revocation propagates fleet-wide via the coherence epoch — seconds, not TTLs

// Every mutating agent action lands as an audit entity:
var actions = await AgentAction.Query(a => a.AgentId == "kitchen-agent" && a.At > since);
```

**Reference usage**: an agent connects over MCP with a Trust-issued identity; sees only
read tools for `Recipe`, read+mutate for `PantryItem` while its grant lives; every mutation is
an `AgentAction` row; ops revokes one grant (or bumps the agent's epoch) and the change is live
everywhere the coherence channel reaches. Boot report prints the exposed surface:
`mcp: 2 entities · PantryItem[read,mutate,audited] · Recipe[read]`.

**Prompt**:

```text
[DESIGN-PREAMBLE]
DESIGN TASK: governed agent access for Koan.Mcp. Evidence: docs/assessment/evidence/
pillar-periphery-services.json (Mcp section), pillar-web-auth-security.json (Trust, IAuthorize),
pillar-cache.json (coherence channels, epoch idea); memory of direction: the fleet-trust ADR
draft (KSVID, CAEP-epoch-over-coherence). Code anchors: src/Koan.Mcp/McpEntityAttribute.cs +
EndpointToolExecutor (it already rides IEntityEndpointService — the enforcement choke point),
src/Koan.Web.Extensions/Authorization (IAuthorize seam), src/Koan.Security.Trust/**.
Prior art: OAuth token exchange / RFC 9396 RAR (rich authorization requests), Biscuit/Macaroon
attenuation (capability tokens — compare to entity-grants), Kubernetes RBAC (role indirection
cost), MCP spec auth guidance (thin). DECIDE: (1) grant model — entities (hypothesis A) vs
claims-in-token vs both (entities for revocability + audit; token carries identity only?);
(2) default posture (read-only is hypothesis A — challenge: is even read too much by default
for [McpEntity]-less entities? answer: nothing is exposed without the attribute, keep it);
(3) enforcement point — EndpointToolExecutor via IAuthorize, NOT per-transport;
(4) audit write path — entity events vs explicit interceptor; cost on hot path;
(5) revocation latency contract — epoch bump over ICacheCoherenceChannel (this is the second
consumer that triggers the channel's documented promotion to Koan.Core — coordinate with that
move); (6) what Code Mode (Jint sandbox) inherits from grants. Boundaries: no new project if
it fits in Koan.Mcp + Trust; the Web.Admin/ops-tools half is SC7, not this card.
DELIVERABLE: ADR + staged ledger; stage 1 must be shippable alone (attribute Expose flags +
default read-only), grants stage 2, audit stage 3, epoch revocation stage 4.
DEPENDS ON: Trust fabric ADR (in flight); coordinate, don't fork it.
```

---

## SC3 · Conformance-by-declaration — your app inherits a test suite 〔rank 3〕

**Gap & assets**: agents generate code faster than teams can verify it; no framework ships
*semantic conformance tests derived from declared intents*. Koan's TestKits already do exactly
this internally (AdapterSurface FilterConvergence oracle, Jobs 5-tier suite, KoanIntegrationHost)
— for the framework's own entities. Point the same machinery at *the app's* entities.

**Proposed shape** *(design target — does not exist; mirrors the existing TestKit idiom)*:

```csharp
// In the app's test project — one class per entity, batteries inherited:
public sealed class PantryItemConformance : EntityConformanceSpecs<PantryItem>
{
    // Inherited [Fact]/[Theory] batteries, gated by what PantryItem declares:
    //  - round-trip + identity (always)
    //  - query pushdown agreement vs the in-memory reference evaluator (FilterConvergence,
    //    against YOUR elected adapter)
    //  - partition isolation (if partitions/tenancy used)
    //  - cache: fresh-or-null + invalidation-on-write (if [Cacheable])
    //  - embedding: sync-after-save, search round-trip (if [Embedding], container-gated)
    //  - jobs: at-least-once + idempotent re-run (if IKoanJob<T>)
    protected override PantryItem NewValid() => new() { Name = "rice", Quantity = 2 };
}
```

```bash
dotnet test   # the agent's post-change verification harness — zero tests written by hand
```

**Prompt**:

```text
[DESIGN-PREAMBLE]
DESIGN TASK: consumer-facing conformance kits ("Koan.Testing for apps"). Evidence:
docs/assessment/evidence/testsBuild.json (TestKit inventory: Data.AdapterSurface.TestKit,
VectorAdapterSurface, Jobs TestKit, Web AdapterSurface, KoanIntegrationHost); code anchors:
tests/Suites/Data/AdapterSurface/Koan.Data.AdapterSurface.TestKit/FilterConvergence.cs (the
oracle pattern), tests/Shared/Koan.Testing/** (container fixtures, skip-when-no-docker).
Prior art: Rails generated test scaffolds (empty shells — the anti-goal), Pact/contract testing
(consumer-driven contracts — adjacent), Hypothesis/property-based testing (the oracle kinship),
xUnit shared spec-base idiom (what the repo already does). DECIDE: (1) packaging — promote
tests/Shared/Koan.Testing to a shipped Sylin.Koan.Testing package (it currently lives outside
src/ — this is the "new project" exception to argue); (2) trait-gating mechanics — how the base
class discovers [Cacheable]/[Embedding]/partitions on TEntity and activates batteries (the
EntityMetadataProvider already reads traits); (3) data supply contract (NewValid()/mutation
hooks — keep it to ≤2 overrides for the 80% case); (4) container policy — same skip-cleanly
fixtures as the framework's own suites; (5) what the dotnet new template (06 H1) includes by
default. Boundaries: no assertion DSL invention — xunit + the existing AwesomeAssertions; no
app-logic testing claims (this verifies FRAMEWORK-INTENT conformance, say so in docs).
DELIVERABLE: ADR + staged ledger (stage 1 = round-trip+pushdown batteries; stage 2 = trait-gated
cache/embedding; stage 3 = template integration).
```

---

## SC4 · Multi-tenancy as a one-attribute primitive 〔rank 4 — gated on Facet 3〕

**Gap & assets**: .NET tenancy is DIY (EF query filters) or heavyweight (ABP); Marten's
per-tenant story is Postgres-pinned. Koan already has the substrate: validated partitions,
partition-aware cache keys, partition-scoped vector search — and a capability model to grade
isolation honestly.

**Proposed shape** *(design target — does not exist)*:

```csharp
[Tenant]                                       // tenancy = a declared trait of the entity
public sealed class Invoice : Entity<Invoice>
{
    public decimal Amount { get; set; }
}
```

```jsonc
// appsettings — resolution + posture, not plumbing:
{ "Koan": { "Tenancy": {
    "Resolve": "claim:tenant_id",              // web: from the principal; jobs: from work-item
    "Isolation": "Auto",                       // Auto = strongest the adapter supports
    "Strict": true                             // no ambient tenant => fail loud, never cross-read
} } }
```

```csharp
// Background/ops code states tenant explicitly — the existing ambient idiom:
using (EntityContext.Tenant("acme"))
{
    var due = await Invoice.Query(i => i.Amount > 0);      // scoped; cache keys scoped; vectors scoped
}

// Capability-graded isolation, declared and negotiated like everything else:
// DataCaps.Tenancy.PartitionScoped | SchemaScoped | DatabaseScoped
// Boot report: "tenancy: postgres → SchemaScoped (Auto) · strict"
```

**Prompt**:

```text
[DESIGN-PREAMBLE]
DESIGN TASK: first-class tenancy on the partition substrate. Evidence:
docs/assessment/evidence/pillar-data-core.json (EntityContext, PartitionNameValidator,
DATA-0077/0094), pillar-cache.json (partition-aware keys); code anchors:
src/Koan.Data.Core/EntityContext.cs (note the inherit-vs-replace semantics fix, E9 in 06),
CacheKey.For<T>(id, partition). Prior art: ABP tenancy (feature-complete, concept-heavy — count
its concepts as the anti-budget), Marten per-tenant databases, EF global query filters (the
leaky default), Finbuckle.MultiTenant (resolution strategies worth stealing). DECIDE:
(1) [Tenant] as trait vs tenancy profile in options vs both (hypothesis: attribute opts the
entity in; options own resolution/posture); (2) isolation grading — new DataCaps.Tenancy token
group; Auto-election per adapter (partition row-scope everywhere; schema-per-tenant where
relational DDL allows; database-per-tenant where the adapter can route connections) — each
level's guarantees stated in the ADR's honesty table; (3) STRICT posture semantics: no resolved
tenant => throw on [Tenant] entity access (fail-loud), opt-out for migration scenarios;
(4) interaction with existing raw partitions (tenancy reserves a partition namespace? compose?
— probe the validator rules empirically); (5) cross-tenant admin path (explicit
EntityContext.AllTenants() escape hatch with audit, or refuse v1?); (6) jobs/vector/cache
propagation — enumerate each pillar's tenant-scoping seam and verify by reading, not asserting.
HARD GATE: Facet 3 (ambient context semantics) must be settled first — this design rides
AsyncLocal scoping; coordinate with that ADR rather than preceding it.
DELIVERABLE: ADR + staged ledger; stage 1 = partition-scoped tenancy + strict posture + boot
report; later stages add graded isolation per adapter.
```

---

## SC5 · Scales-down / sovereign deployment 〔rank 5 — positioning + hardening〕

**Gap & assets**: every framework scales up; Koan's capability ladder scales *down* (in-memory →
SQLite → distributed), the AI seam runs on local Ollama, g1c1 already dogfoods NativeAOT
single-file publish. The BaaS competitors (Supabase/Convex/Firebase) structurally cannot ship
air-gapped.

**Proposed shape** *(mostly a verified recipe + guarantees, not new API)*:

```bash
# One binary. One folder. No cloud. Same grammar as the cluster deployment.
dotnet publish -c Release -p:PublishAot=true -r linux-x64
./myapp   # SQLite at ./data, local Ollama if present, vector via the in-process tier
```

```text
Boot report (sovereign profile):
  data → sqlite (./data/app.db) · ai → ollama(localhost) [degrades: semantic search off if absent]
  jobs → durable(sqlite) · cache → memory · mcp → enabled(local)
```

**Prompt**:

```text
[DESIGN-PREAMBLE]
DESIGN TASK: make "runs air-gapped" a verified, stated guarantee. This is hardening +
positioning, not a new pillar. Evidence: docs/assessment/evidence/samples.json (g1c1 AOT
dogfood), 05 §6.1 (the Newtonsoft↔AOT tension — the known blocker to resolve, not relitigate
the serializer). Code anchors: samples/guides/g1c1.GardenCoop (publish profile),
Directory.Build.props (trimming posture), the capability ladder docs (JOBS-0005, cache).
WORK: (1) empirically AOT-publish a representative app (entity+web+sqlite+jobs+cache): catalog
every trim/AOT warning and runtime failure; Newtonsoft-under-AOT gets a verified verdict
(works-with-rd.xml? works-with-trimming-disabled-for-NJ? genuinely blocked?) — evidence, not
opinion; (2) define the sovereign capability profile: exactly which pillars/adapters are in
(sqlite/json data, durable-sqlite jobs, memory/sqlite cache, Ollama AI with graceful absence,
local storage, MCP) and what the boot report prints for it; (3) a CI AOT-publish smoke job
(extends 06 B2) so the guarantee can't silently rot; (4) docs: a "sovereign deployment" guide
page + README differentiator line, written AFTER the smoke passes (truth-first).
Boundaries: no [KoanOffline] attribute or new config surface unless the probe proves a real
need — prefer "the default app already does this" as the outcome. DELIVERABLE: probe report →
short ADR (guarantees + supported profile) → the CI smoke + guide.
```

---

## SC6 · App-level AI evals — golden sets as entities, runs as jobs 〔rank 6 — hard boundaries〕

**Gap & assets**: AI client libraries (Spring AI, Semantic Kernel) give you calls; eval SaaS
(LangSmith/Braintrust) gives you platforms; nobody gives a product team *regression tests for
AI behavior wired to their domain data*, local-first. Koan can express it entirely in existing
grammar: entities + jobs + health.

**Proposed shape** *(design target — does not exist)*:

```csharp
// A golden expectation is an entity — versioned, queryable, seedable:
public sealed class SearchGolden : Entity<SearchGolden>
{
    public string Query { get; set; } = "";
    public string[] ExpectedIds { get; set; } = [];
}

// An eval is a job over goldens — same Execute idiom as every other job:
[JobAction("eval", Schedule = "1.00:00:00")]                  // nightly drift watch
public sealed class SearchEval : Entity<SearchEval>, IKoanJob<SearchEval>
{
    public double RecallAt10 { get; set; }

    public static async Task Execute(SearchEval run, JobContext ctx, CancellationToken ct)
    {
        var goldens = await SearchGolden.All(ct);
        run.RecallAt10 = await EvalMetrics.RecallAtK(10, goldens,
            g => SemanticSearch<Product>(g.Query, limit: 10, ct: ct));
        if (run.RecallAt10 < 0.85) ctx.Progress(1.0, "DRIFT: recall@10 below floor");
    }
}

// Drift is a health fact, not a dashboard you build:
// health: ai-eval:search → Degraded (recall@10 0.78 < 0.85 floor, run 2026-06-11)
```

**Prompt**:

```text
[DESIGN-PREAMBLE]
DESIGN TASK: app-level AI evaluation in pure Koan grammar. HARD BOUNDARY FIRST (write it into
the ADR's scope before anything else): this evaluates THE APP'S AI BEHAVIOR (search relevance,
extraction accuracy, classification agreement) — it is NOT model ops; no training, no model
registries, no metric-compute adapters; if the design reaches for AiCapability.MetricCompute
or the parked Eval vertical's service surface, STOP — that lane was shed (05 §5.1). Reuse from
the parked Koan.AI.Eval ONLY data shapes that fit entities (EvalScore record); the parked code
is quarry, not foundation. Evidence: docs/assessment/evidence/pillar-ai-pillar.json (Eval
facade-over-nothing finding), CLAUDE.md data→AI seam. Code anchors: src/Koan.Data.AI/
EntityEmbeddingExtensions.cs, src/Koan.Jobs/** (JobContext.Progress, JobMetric), IHealthContributor.
Prior art: ragas/deepeval (metric vocabulary worth borrowing: recall@k, MRR, faithfulness-lite),
LangSmith (the SaaS UX to deliberately NOT need), pytest-style golden files. DECIDE:
(1) metric helper surface (a small static EvalMetrics — counted against the concept budget) vs
letting apps hand-roll (hypothesis: ship ~5 metrics, no extensibility framework v1);
(2) where results live (eval runs are job entities already persisted — is a separate result
entity needed at all?); (3) health integration contract (floor declared where — on the job
attribute? options?); (4) packaging: inside Koan.Data.AI (hypothesis A — it IS the data→AI
seam) vs new package (argue against). DELIVERABLE: ADR with the boundary section first +
staged ledger (stage 1 = metrics + one dogfood eval in S5.Recs; stage 2 = health floors).
```

---

## SC7 · Agent-operable runtime — ops verbs as governed tools 〔rank 7 — phase 2 of SC2〕

**Gap & assets**: "ChatOps" is glue code everywhere. Once SC2's grants exist and MCP
introspection (06 S1) lands, exposing the framework's existing ops verbs as governed tools is
assembly: Jobs trigger/cancel, cache flush, re-embed, backup — all already have programmatic
surfaces.

**Proposed shape** *(design target — does not exist)*:

```jsonc
// Opt-in, per toolset, governed by the SC2 grant model:
{ "Koan": { "Mcp": { "Operations": {
    "Jobs":  true,     // MyJob.Jobs.Trigger / .Cancel as tools, per-worktype grants
    "Cache": true,     // EntityCache<T>.Flush / tag flush
    "Data":  false     // re-embed, transfers — off by default; most dangerous last
} } } }
```

```text
Agent session (with an ops grant):
  > tool: koan.jobs.trigger { "workType": "ImportJob", "action": "import" }
  > tool: koan.cache.flush  { "entity": "Recipe" }
Every call: identity-stamped AgentAction audit row; revocable mid-session via SC2 epoch.
Boot report: "mcp.ops: jobs,cache → 2 toolsets · grants required · audited"
```

**Prompt**:

```text
[DESIGN-PREAMBLE]
DESIGN TASK: framework-shipped operational MCP toolsets, governed by SC2. PREREQS: SC2 stages
1-2 merged; 06 S1 (introspection) at least ADR'd — this card must not fork either. Evidence:
docs/assessment/evidence/pillar-periphery-services.json (Mcp), CLAUDE.md (.Jobs accessors,
EntityCacheExtensions). Code anchors: src/Koan.Jobs/JobAccessor.cs (Jobs.Trigger/Cancel),
src/Koan.Cache/EntityCacheExtensions (Flush/FlushAll), src/Koan.Web.Admin (the surfaces that
become agent-facing). DECIDE: (1) toolset granularity + naming (koan.jobs.*, koan.cache.* —
follow MCP tool-naming norms); (2) the danger ladder: which toolsets default-on in Development
only, which require explicit grants always (hypothesis: ALL mutating ops require a grant even
in dev — dev convenience is what introspection read-tools are for); (3) idempotency/confirm
semantics for destructive verbs (FlushAll, Cancel) — MCP has no confirm dialog: design the
guard (dry-run parameter? two-step token?); (4) Web.Admin convergence: do the admin HTTP
endpoints and the MCP tools share one service layer (they must — IEntityEndpointService
precedent). Boundaries: no new project (lives in Koan.Mcp behind options); no bespoke RBAC —
grants only. DELIVERABLE: ADR + staged ledger (stage 1 = jobs toolset read+trigger; stage 2 =
cache; stage 3 = data ops behind explicit grants).
```

---

## Sequencing across cards

```text
SC1 (lockfile)        → independent; do first (cheap, compounds all later verification)
SC2 (governed access) → after/with the Trust fabric ADR; triggers the ICoherenceChannel→Core promotion
SC3 (conformance)     → independent of SC1/SC2; pairs with 06 H1 (template)
SC4 (tenancy)         → HARD-GATED on Facet 3 (ambient context); design after, not before
SC5 (sovereign)       → independent probe; its CI smoke rides 06 B2
SC6 (AI evals)        → after the AI vertical collapse (06 S3) so the boundary is physically clear
SC7 (agent ops)       → strictly after SC2 stage 2 + 06 S1
```

Each completed ADR mints T1/T2 implementation cards — append them to
[06-prompt-stash.md](06-prompt-stash.md) under a new "SC implementation" section so the
lesser-model pipeline stays the single execution queue.
