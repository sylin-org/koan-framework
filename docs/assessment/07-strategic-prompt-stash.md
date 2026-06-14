# Stage 7 — Strategic capability build stash (self-contained, maturity-ordered)

**Purpose**: ready-to-paste session prompts that **build** the second-act strategic capabilities
([05 §3.1](05-strategic-position.md)). Each card is a complete, self-contained session —
research → design validation → implementation → tests → docs — sized and pre-decided so that
**lesser models can execute it end-to-end**. The design is *given* (shapes + reference usage
below); the session's job is to validate it against the real codebase and build it, not to
invent it.

**Greenfield posture (applies to every card)**: Koan is pre-1.0 with no external consumers.
Break-and-rebuild is welcome; a proper architecture set now saves rework later. No `[Obsolete]`
bridges, no compatibility shims, no dual paths — delete superseded code in the same session.
The green ratchet (build + tests) is the only backward contract.

> ⚠️ The C# blocks below are **target shapes** — they do not exist until their session ships
> them. This file stays excluded from the snippet-compile lint; a session may treat its own
> card's shape as the spec, never as evidence of an existing API.

> **▶ Execution artifacts** — this stash is split into one self-contained file per session under
> [`prompts/07/`](prompts/) and tracked in [`prompts/PROGRESS.md`](prompts/PROGRESS.md). This file
> stays the canonical source for the maturity ladder, gates, and target shapes; the split cards
> are derived. Edit a card here, then regenerate or hand-patch its file.

---

## The maturity ladder — why this order

Each phase makes the next one cheaper and safer. Run phases in order; cards inside a phase are
independent unless marked.

```text
GATE  (from 06): B1 sln-truth · F1 fail-loud boot · A2 doc-truth sweep   ← run these first
P1  SELF-DESCRIPTION   the app states what it is        → P1.1 lockfile · P1.2 MCP introspection
P2  VERIFICATION       changes prove themselves          → P2.1 conformance kits
P3  TRUST              agents become governed principals → P3.1 grants/audit · P3.2 agent ops
P4  DOMAIN POWER       capabilities apps build on        → P4.1 tenancy* · P4.2 AI evals**
P5  REACH              the story ships                   → P5.1 sovereign/AOT · P5.2 wedge demo
   * P4.1 additionally gated on the ambient-context hardening (Facet 3)
  ** P4.2 additionally gated on the AI vertical collapse (06 S3)
```

Composition logic: P1 gives every later session (and every agent) ground truth to read; P2
gives every later session a harness to prove itself against; P3 turns the MCP surface from a
demo into a governed product and unlocks P3.2/P5.2; P4 ships the capabilities customers build
on, safely, because P1–P3 exist; P5 packages the proof.

---

## [SESSION-PREAMBLE] — paste atop EVERY session in this file

```text
You are implementing a designed capability for the Koan Framework (.NET 10 meta-framework;
repo root = working directory). Koan's grammar: Entity<T> is the universal noun (data, REST,
cache, jobs, embeddings, agent tools); packages self-register ("Reference = Intent",
source-generated registry); adapters declare capabilities that are negotiated and fail loud
(ARCH-0084); the app self-reports at boot. Your card gives you the TARGET SHAPE — implement it.

METHOD — work in this order, completing each step before the next:
1. RESEARCH (read, don't trust): read every file your card's ANCHORS list names, plus the
   evidence JSON it cites. Verify each assumption the card makes; if reality differs, adapt the
   plan and record the delta in your final summary. Never reference an API you haven't seen.
2. PLAN: write a short plan-of-record into the session (files to create/modify, test list,
   boot-report line, docs touchpoints). Where the card marks DECIDED, do not re-litigate;
   where it marks DEFAULT, you may deviate only with a one-paragraph justification.
3. IMPLEMENT — Koan DX tenets are acceptance criteria:
   (a) entity-grammar first: new nouns are entities wherever possible;
   (b) attribute-first declaration; options for posture, never for wiring;
   (c) Reference = Intent: referencing the package/feature activates sane defaults;
   (d) capability-graded: provider differences are declared tokens, negotiated, never faked;
   (e) fail-loud: unsupported/misconfigured = descriptive exception naming the fix;
   (f) self-reporting: add the boot-report line your card specifies;
   (g) concept budget: the developer-facing surface must match the card's shape — no extra
       public types without justification.
   Greenfield rule: replace, don't bridge. Delete superseded paths in this session.
4. TEST: unit tests for logic + at least one ARCH-0079 integration spec through real AddKoan()
   (KoanIntegrationHost — see tests/Shared/Koan.Testing). Container-dependent specs must
   skip cleanly without Docker.
5. DOCUMENT: update the feature's guide page + the relevant .claude/skills entry + CLAUDE.md
   utilities list if applicable. Every snippet you write into docs must compile — copy from
   your own tests.
6. VERIFY: dotnet build Koan.sln green; dotnet test Koan.sln (non-container) green;
   scripts/docs-lint.ps1 green if docs touched.
FINAL SUMMARY: files touched; deltas from the card; evidence citations (file:line) for every
claim; the boot-report line as actually printed.
IF BLOCKED: prefer the simplest design that satisfies the tenets and note the compromise;
revert-and-report only if the build cannot be made green.
```

---

# PHASE 1 — SELF-DESCRIPTION

## P1.1 · Composition lockfile (`koan.lock.json`)

**Why now**: cheapest card, and every later session + every agent gains ground truth: "what is
this app, exactly?" — answered without booting it.

**UX/DX after this session**:

```bash
dotnet build                      # koan.lock.json refreshed automatically
git diff koan.lock.json           # PR review now SHOWS composition drift:
                                  #   + "cache:coherence": { "channel": "messaging" }
```

**Target shape**:

```jsonc
// koan.lock.json (project root, checked in) — stable, versioned schema
{
  "schema": 1,
  "app": { "name": "S5.Recs", "koan": "0.17.43", "tfm": "net10.0" },
  "modules":   [ { "id": "Koan.Data.Connector.Postgres", "version": "0.17.43" } ],
  "elections": { "data:default": { "adapter": "postgres", "via": "reference-priority" } },
  "capabilities": { "data:postgres": ["Query.Linq", "Write.BulkUpsert"] },
  "configKeys": [ "Koan:Data:Postgres:ConnectionString" ],   // KEYS only — never values
  "entities":  [ { "type": "Anime", "traits": ["Embedding"] } ]
}
```

**Session prompt**:

```text
[SESSION-PREAMBLE]
BUILD: the Koan composition lockfile.
ANCHORS: src/Koan.Core.Registry.Generators/RegistrySourceGenerator.cs (what the build-time
registry knows) · src/Koan.Core/Provenance/** · src/Koan.Core/Hosting/Runtime/AppRuntime.cs
(RegistrySummarySnapshot) · docs/assessment/evidence/pillar-core-bootstrap.json.
DECIDED: two emitters, one schema — (1) an MSBuild target shipped by Koan.Core
(Sylin.Koan.Core.targets) writes koan.lock.json at build from the source-gen registry +
referenced-module metadata; (2) at boot, the host writes the RESOLVED twin to
obj/koan.lock.resolved.json (actual elections + negotiated capabilities) and the boot report
prints one line: "composition: <n> modules · lockfile ok|DRIFT(<keys>)" comparing the two.
Config VALUES never appear; config KEYS consumed do. Schema carries "schema": 1.
DEFAULT: comparer ships as scripts/compare-koan-lock.ps1 (exit 1 on drift) wired into the PR
gate if .github/workflows/pr-gate.yml exists; entities/traits section read from the registry's
entity metadata if available at build time, else emitted only in the resolved twin (note which).
IMPLEMENT in Koan.Core (no new project). TESTS: unit (schema serialization, comparer cases:
module added / capability removed / key added) + integration: KoanIntegrationHost app asserts
the resolved twin contains its known module + election. DOCS: docs/guides/composition-lockfile.md
(generate snippets from your tests) + a line in docs/getting-started/overview.md's
"When something goes wrong" + README "differentiators" bullet.
```

## P1.2 · Runtime introspection over MCP — "the app explains itself to agents"

**Why now**: converts the boot-report investment into the agent-native differentiator; every
later phase (grants, ops, demo) builds on this surface. Read-only by design — governance
arrives in P3.

**UX/DX after this session**:

```text
Agent connects to a dev-mode Koan app over MCP and reads resources:
  koan://app            → name, version, environment, uptime
  koan://composition    → the resolved lockfile twin (P1.1)
  koan://entities       → [ { type, traits: [Cacheable, Embedding, McpEntity], adapter } ]
  koan://capabilities   → negotiated CapabilitySet per source
  koan://boot-report    → the rendered report, as text
  koan://health         → aggregated health facts
The agent doesn't read docs; it asks the running app.
```

**Target shape** *(app developer's view — zero code)*:

```jsonc
// Reference Sylin.Koan.Mcp → introspection resources are ON in Development, OFF elsewhere:
{ "Koan": { "Mcp": { "Introspection": "Development" } } }   // Development | Always | Off
```

**Session prompt**:

```text
[SESSION-PREAMBLE]
BUILD: read-only MCP introspection resources in Koan.Mcp.
ANCHORS: src/Koan.Mcp/** (McpServer, HttpSseTransport, resource handling — read how tools vs
resources are modeled in the existing McpRpcHandler) · src/Koan.Core/Provenance/** ·
src/Koan.Core/Hosting/Runtime/AppRuntime.cs · Koan.Data.Core's EntityMetadataProvider (entity
traits) · Data<T,K>.Capabilities · docs/assessment/evidence/pillar-periphery-services.json
(Mcp section).
DECIDED: MCP *resources* (not tools — read surface), named koan://app, koan://composition,
koan://entities, koan://capabilities, koan://boot-report, koan://health. Posture option
Koan:Mcp:Introspection = Development (default) | Always | Off; fail-loud if Always in
Production without explicit config. No secrets/config values anywhere in any resource —
config keys only (same rule as P1.1). Boot report line: "mcp.introspection: on(dev) · 6 resources".
DEFAULT: implement as one IntrospectionResourceProvider in Koan.Mcp (no new project);
capabilities resource enumerates per-source CapabilitySets via the existing facade; entities
resource reads the registry/metadata provider — if trait enumeration requires reflection,
cache it once at startup.
TESTS: integration spec via KoanIntegrationHost + the MCP test fixture (tests/Fixtures/
Koan.Mcp.TestHost): request each resource over the in-proc transport, assert shape + the
no-secrets rule (resource bodies must not contain any configured connection string value —
write that as an explicit test). DOCS: extend docs/guides/mcp-http-sse-howto.md + the
mcp-integration skill; add the README beat-3 sentence ("ask the running app") if absent.
```

---

# PHASE 2 — VERIFICATION

## P2.1 · Conformance-by-declaration — apps inherit a test suite

**Why now**: from here on, every later card's sample integration gets verified by the kit it
ships. Agents get their post-change harness.

**UX/DX after this session**:

```csharp
// In the app's test project — one class per entity; batteries arrive by inheritance,
// activated by what the entity declares:
public sealed class AnimeConformance : EntityConformanceSpecs<Anime>
{
    protected override Anime NewValid() => new() { Title = "Cowboy Bebop" };
}
// dotnet test → round-trip, pushdown-vs-reference-oracle, paging, [Cacheable] coherence,
//               [Embedding] sync (container-gated) — zero tests written by hand.
```

**Target shape**:

```csharp
// Shipped package: Sylin.Koan.Testing  (promotion of tests/Shared/Koan.Testing)
public abstract class EntityConformanceSpecs<TEntity> where TEntity : Entity<TEntity>, new()
{
    protected abstract TEntity NewValid();
    protected virtual TEntity Mutate(TEntity e) => e;      // optional second hook, no more
    // [Fact]/[Theory] batteries: RoundTrip, QueryPushdownAgreesWithReferenceEvaluator,
    // Paging, PartitionIsolation (if partitions used), CacheFreshOrNull + InvalidateOnWrite
    // (if [Cacheable]), EmbeddingSyncAfterSave (if [Embedding], container-gated).
}
```

**Session prompt**:

```text
[SESSION-PREAMBLE]
BUILD: consumer conformance kits as a shipped package Sylin.Koan.Testing.
ANCHORS: tests/Shared/Koan.Testing/** (KoanIntegrationHost, container fixtures, skip-cleanly
pattern — this project PROMOTES to src/, keep its namespaces) · tests/Suites/Data/AdapterSurface/
Koan.Data.AdapterSurface.TestKit/FilterConvergence.cs (the oracle pattern to reuse, NOT
duplicate — reference its evaluator) · src/Koan.Data.Core Model metadata (trait detection) ·
docs/assessment/evidence/testsBuild.json.
DECIDED: new project src/Koan.Testing (the justified exception to no-new-projects: it is a
distinct intent a developer references alone, test-side only, IsPackable). Move
tests/Shared/Koan.Testing into it; update ALL test csproj references (this is the
break-and-rebuild move — no duplicate copy left behind). EntityConformanceSpecs<TEntity> with
exactly two override hooks (NewValid required, Mutate optional). Batteries are trait-gated via
entity metadata; each battery cites the invariant it pins in its xdoc. Container-dependent
batteries reuse the existing fixtures and skip cleanly.
DEFAULT: pushdown-agreement battery delegates to the existing InMemoryFilterEvaluator as the
reference; if reuse across the project boundary is awkward, link the source file rather than
duplicating logic (justify whichever you do).
DOGFOOD (required): add conformance classes for one entity in samples/S1.Web and one in
samples/S5.Recs; they must pass.
TESTS: the kit's own meta-tests (a deliberately-broken fake entity fails the right battery).
DOCS: docs/guides/testing-your-app.md + add the kit to the dotnet-new template card's notes
(06 H1) + README differentiator line ("your app inherits a test suite").
```

---

# PHASE 3 — TRUST

## P3.1 · Governed agent access — grants, audit, revocation

**Why now**: P1.2 made the app legible to agents; this makes agent access a *governed,
revocable, audited grant*. The flagship differentiator. Coordinates with Security.Trust —
if the Trust fabric ADR has landed, use its identity types; if not, build against `IIssuer`/
`Identity.Current` as they exist today.

**UX/DX after this session**:

```csharp
[McpEntity]                                   // exposure now defaults to READ-ONLY
public sealed class Recipe : Entity<Recipe> { }

[McpEntity(Expose = McpAccess.Read | McpAccess.Mutate, Audit = true)]
public sealed class PantryItem : Entity<PantryItem> { }

// Grants are entities — the Koan move. Queryable, revocable, observable:
await new AgentGrant { AgentId = "kitchen-agent", EntityType = nameof(PantryItem),
                       Access = McpAccess.Mutate,
                       ExpiresAt = DateTimeOffset.UtcNow.AddHours(8) }.Save();
await grant.Remove();                          // live revocation, fleet-wide

var trail = await AgentAction.Query(a => a.AgentId == "kitchen-agent");
```

```text
Boot report: "mcp: 2 entities · PantryItem[read,mutate,audited] · Recipe[read] · grants: 1 active"
```

**Session prompt**:

```text
[SESSION-PREAMBLE]
BUILD: governed agent access for Koan.Mcp (grants, audit, revocation).
ANCHORS: src/Koan.Mcp/McpEntityAttribute.cs + EndpointToolExecutor + RequestTranslator (the
enforcement choke point — ALL agent calls flow through IEntityEndpointService here; enforce
once, never per-transport) · src/Koan.Web.Extensions/Authorization (IAuthorize seam, SEC-0002)
· src/Koan.Security.Trust/** (Identity.Current, IIssuer, TrustClaims) ·
src/Koan.Cache.Abstractions ICacheCoherenceChannel (revocation transport) ·
docs/assessment/evidence/pillar-web-auth-security.json + evidence/stage4 if present.
DECIDED:
1. Default posture flips: [McpEntity] alone = Query/Get only (McpAccess.Read). Mutation
   requires Expose declaring it. BREAKING by design — update S16.PantryPal accordingly and say
   so in the summary.
2. Grants are entities: AgentGrant : Entity<AgentGrant> { AgentId, EntityType, Access,
   ExpiresAt? } in Koan.Mcp. Effective access = attribute Expose ∩ grant (no grant needed for
   Read; mutation always needs a live grant). Cache the grant lookup; invalidate on write
   (use the cache pillar, not a bespoke dictionary).
3. Audit: when Audit=true, every MUTATING agent call writes AgentAction : Entity<AgentAction>
   { AgentId, EntityType, Verb, EntityId, At } — written through the normal entity path so it
   is queryable/streamable like everything else. Read calls are never audited (volume).
4. Agent identity: from the MCP session's authenticated principal (Koan.bearer/Trust when
   configured; in Development, the dev-identity persona). No principal + mutation attempt =
   fail-loud with the exact message naming the missing grant.
5. Revocation: grant Remove()/expiry must take effect on running nodes within seconds. DEFAULT
   transport: piggyback the cache pillar's invalidation of the grant cache (deleting the
   entity invalidates via the existing decorator + coherence). If [Cacheable] on AgentGrant
   achieves this with zero new machinery, USE THAT and document it as the mechanism — do not
   build an epoch system in this session (that belongs to the Trust fabric work).
TESTS: integration via Koan.Mcp.TestHost — read allowed w/o grant; mutate denied w/o grant
(assert the error message names grant + entity); mutate allowed with grant; revoke mid-session
→ next call denied; audit row written exactly once per mutation. DOGFOOD: S16.PantryPal grants
its agent mutate on PantryItem only.
DOCS: docs/guides/mcp-governed-access.md + mcp-integration skill + boot-report line.
```

## P3.2 · Agent-operable runtime — ops verbs as governed tools 〔requires P3.1〕

**UX/DX after this session**:

```jsonc
{ "Koan": { "Mcp": { "Operations": { "Jobs": true, "Cache": true } } } }   // opt-in per toolset
```

```text
Agent (holding an ops grant):
  tool koan.jobs.trigger  { "workType": "ImportJob", "action": "import" }   → job id
  tool koan.jobs.status   { "id": "…" }                                     → ledger state
  tool koan.cache.flush   { "entity": "Recipe" }                            → flushed count
Every mutating call: AgentAction audit row (P3.1). Destructive verbs take {"confirm": true}.
Boot report: "mcp.ops: jobs,cache · grants required · audited"
```

**Session prompt**:

```text
[SESSION-PREAMBLE]
BUILD: framework-shipped operational MCP toolsets, governed by P3.1's grant model.
ANCHORS: src/Koan.Jobs/JobAccessor.cs (Jobs.Trigger/.Cancel/.Where statics) ·
src/Koan.Cache/EntityCacheExtensions (Flush/FlushAll) · P3.1's grant/audit types ·
src/Koan.Mcp tool registration path (read how [McpEntity] tools register; ops toolsets follow
the same registration idiom).
DECIDED: toolsets are opt-in via Koan:Mcp:Operations (all default OFF, including Development —
read tools from P1.2 cover dev convenience). Tool names: koan.jobs.{trigger,cancel,status},
koan.cache.{flush,flushAll}. ALL ops verbs require an AgentGrant with EntityType "@ops:jobs" /
"@ops:cache" (reuse the grant entity; the @ops: prefix namespaces operational grants —
validate the prefix). Destructive verbs (cancel, flushAll) require parameter confirm:true,
else return a descriptive refusal listing what WOULD happen (the dry-run is the default).
All mutating ops write AgentAction rows.
DEFAULT: no Data toolset in this session (re-embed/transfer is a later card once demand
exists — note it in the guide as deliberately absent).
TESTS: TestHost integration — toolset off ⇒ tools absent from list; on without grant ⇒
fail-loud naming "@ops:jobs"; trigger with grant ⇒ job actually runs (assert via ledger);
flushAll without confirm ⇒ dry-run text. DOGFOOD: enable Jobs ops in S14.AdapterBench.
DOCS: extend docs/guides/mcp-governed-access.md with the ops section; boot-report line.
```

---

# PHASE 4 — DOMAIN POWER

## P4.1 · Multi-tenancy primitive 〔gate: Facet 3 ambient-context hardening settled〕

**UX/DX after this session**:

```csharp
[Tenant]                                          // one attribute opts the entity in
public sealed class Invoice : Entity<Invoice> { public decimal Amount { get; set; } }
```

```jsonc
{ "Koan": { "Tenancy": { "Resolve": "claim:tenant_id", "Strict": true } } }
```

```csharp
// Web requests: tenant resolved from the principal automatically.
// Background work states it explicitly — the ambient idiom you already know:
using (EntityContext.Tenant("acme"))
{
    var due = await Invoice.Query(i => i.Amount > 0);   // data, cache keys, vectors: all scoped
}
// No resolved tenant + Strict ⇒ TenantRequiredException naming the entity and the fix.
// Boot report: "tenancy: 3 entities · resolve=claim:tenant_id · isolation=partition · strict"
```

**Session prompt**:

```text
[SESSION-PREAMBLE]
GATE CHECK FIRST: read the ambient-context state — src/Koan.Data.Core/EntityContext.cs and any
Facet-3/ambient ADR under docs/decisions/. If inherit-vs-replace scope semantics are still
contradictory/undocumented, STOP and report "gate not met" — this card rides those semantics.
BUILD: first-class tenancy on the partition substrate.
ANCHORS: src/Koan.Data.Core/EntityContext.cs (+ PartitionNameValidator, DATA-0077) ·
CacheKey.For<T>(id, partition) in Koan.Cache.Abstractions · Vector partition scoping in
src/Koan.Data.Vector/VectorData.cs · docs/assessment/evidence/pillar-data-core.json.
DECIDED:
1. [Tenant] attribute (Koan.Data.Abstractions) opts an entity in. Tenancy maps to a RESERVED
   partition namespace: partition = "t:{tenantId}" (verify the validator accepts ':'; if not,
   choose the closest legal separator and document). Raw partition use on [Tenant] entities
   composes as a sub-partition under the tenant: probe how partitions compose today, then
   implement "t:{tenant}.{partition}" or equivalent legal form.
2. Resolution: Koan:Tenancy:Resolve = "claim:<name>" (web principal) | "header:<name>" (dev
   only, warn at boot) ; ambient override via EntityContext.Tenant(string) returning the same
   IDisposable scope idiom as Partition(). Web resolution = a pipeline contributor using the
   existing IKoanWebPipelineContributor seam.
3. Strict=true default: access to a [Tenant] entity with no resolved tenant throws
   TenantRequiredException (sealed, names entity + the config/scoping fix). Cross-tenant admin:
   EntityContext.AllTenants() escape scope that (a) requires Strict-off OR an explicit options
   allowlist, (b) is named in the boot report when used at startup. Keep it minimal.
4. Isolation grading: v1 ships partition-scoped isolation ONLY (every adapter supports it).
   Declare token DataCaps.Tenancy.PartitionScoped on the facade so the surface is
   capability-graded from day one; schema/database isolation are later cards — write the token
   names into the ADR-style design note but DO NOT implement them.
TESTS: unit (resolution, strict-throw, scope composition) + integration: two tenants on sqlite
— full isolation across Query/Get/Save, cache keys distinct (read a cached entity under both
tenants, assert no bleed), vector search scoped if [Embedding] present (container-gated).
DOGFOOD: add a [Tenant] entity to samples/S1.Web with a header resolver + README note.
DOCS: docs/guides/multi-tenancy.md + glossary entries (tenant, isolation) + boot-report line.
```

## P4.2 · App-level AI evals 〔gate: AI vertical collapse (06 S3) done — the boundary must be physical〕

**UX/DX after this session**:

```csharp
public sealed class SearchGolden : Entity<SearchGolden>      // golden sets are entities
{
    public string Query { get; set; } = "";
    public string[] ExpectedIds { get; set; } = [];
}

[JobAction("eval", Schedule = "1.00:00:00")]                 // evals are jobs — nightly drift watch
public sealed class SearchEval : Entity<SearchEval>, IKoanJob<SearchEval>
{
    public double RecallAt10 { get; set; }
    public static async Task Execute(SearchEval run, JobContext ctx, CancellationToken ct)
    {
        var goldens = await SearchGolden.All(ct);
        run.RecallAt10 = await EvalMetrics.RecallAtK(10, goldens,
            g => SemanticSearch<Anime>(g.Query, limit: 10, ct: ct), g => g.ExpectedIds);
    }
}
// Floor breach ⇒ health: "ai-eval:search → Degraded (recall@10 0.78 < 0.85)"
```

**Session prompt**:

```text
[SESSION-PREAMBLE]
SCOPE BOUNDARY FIRST (copy into your plan verbatim): this evaluates THE APP'S AI BEHAVIOR.
No training, no model registries, no model-side metrics, no MetricCompute capability, no reuse
of any parked Koan.AI.{Eval,Training,Compute} service surface. If the design reaches for any of
those, stop that thread — entities + jobs + a small static metrics helper is the entire budget.
BUILD: app-level AI evaluation in Koan.Data.AI (no new project).
ANCHORS: src/Koan.Data.AI/EntityEmbeddingExtensions.cs · src/Koan.Jobs/** (JobContext,
JobMetric, health integration if any) · src/Koan.Core IHealthContributor ·
docs/assessment/evidence/pillar-ai-pillar.json (the facade-over-nothing finding — the
anti-pattern this card replaces).
DECIDED: static class EvalMetrics with exactly five metrics v1: RecallAtK, PrecisionAtK, MRR,
ExactMatchRate, JaccardOverlap — pure functions over (golden, retrieved) pairs, generic over
the app's entity/golden types via delegates (see shape). No extensibility framework, no
interfaces, no registry. Eval runs are ordinary IKoanJob entities the APP writes (the framework
ships metrics + docs + one health helper, not an eval engine). Health: a small
EvalHealth.Report(name, score, floor) helper that pushes a health fact; Degraded below floor.
TESTS: unit per metric (known fixtures incl. edge cases: empty golden set, k > results) +
integration: a fake-embedding eval job runs via the jobs pillar and lands a health fact.
DOGFOOD (required): a real SearchGolden seed (5 queries) + nightly SearchEval job in
samples/S5.Recs, asserted by a conformance-style test.
DOCS: docs/guides/ai-evals.md (lead with the boundary: "this is not MLOps") + ai-integration
skill section + boot report only if a floor is configured ("ai-eval: 1 suite · floor 0.85").
```

---

# PHASE 5 — REACH

## P5.1 · Sovereign / scales-down deployment — "runs air-gapped", verified

**UX/DX after this session**:

```bash
dotnet publish -c Release -p:PublishAot=true -r linux-x64
./myapp        # one binary · SQLite at ./data · local Ollama if present · no cloud, no compose
```

```text
Boot report (sovereign profile):
  data → sqlite(./data/app.db) · jobs → durable(sqlite) · cache → memory
  ai → ollama(localhost) [absent: semantic search degrades loud, app runs]
```

**Session prompt**:

```text
[SESSION-PREAMBLE]
BUILD: make "runs air-gapped" a verified guarantee — this card is an empirical probe that ends
in CI + docs, not a new API surface.
ANCHORS: samples/guides/g1c1.GardenCoop (existing NativeAOT publish dogfood — read its csproj
and any rd.xml/trimming config) · Directory.Build.props · docs/assessment/05-strategic-position.md
§6.1 (the Newtonsoft↔AOT tension: produce a verified verdict, do NOT relitigate the serializer).
WORK, in order:
1. PROBE: create a minimal probe app (entity + EntityController + sqlite + [Cacheable] + one
   IKoanJob) under tests/ or samples/ (DECIDED: samples/S2.Sovereign — the number is free);
   publish with PublishAot=true; catalog EVERY trim/AOT warning and runtime failure into the
   session summary. For each Newtonsoft-related failure: try (a) TrimmerRootDescriptor rd.xml,
   (b) DynamicDependency attributes at the throwing sites, (c) JsonSerializerSettings without
   reflection-emit. Record what actually works — evidence over opinion.
2. FIX: apply the minimal framework-side fixes the probe demands (rd.xml shipped by Koan.Core?
   attributes at specific sites?). Greenfield rule applies: if a small piece of framework code
   is fundamentally AOT-hostile and replaceable, replace it; if a pillar is out of scope for
   sovereign v1 (e.g. a connector that cannot trim), EXCLUDE it from the supported profile and
   say so — the profile is allowed to be narrow, it is not allowed to be vague.
3. GUARANTEE: write the supported sovereign profile into docs/guides/sovereign-deployment.md:
   exactly which pillars/adapters are in (sqlite/json data, durable-sqlite jobs, memory cache,
   Ollama-with-graceful-absence, local storage, MCP) and the boot-report shape.
4. ENFORCE: add an AOT-publish smoke job to CI (publish S2.Sovereign, run it, curl /api/health)
   so the guarantee cannot rot. Wire into pr-gate as a nightly if publish time is heavy.
DOCS: the guide + a README differentiator line, written AFTER the smoke passes (truth-first).
```

## P5.2 · The wedge demo — an agent transcript 〔requires: P1–P3 + 06 Tracks A/B/H1〕

**Session prompt**:

```text
[SESSION-PREAMBLE]
BUILD: the wedge-demo artifact — a real, replayable agent-session transcript that builds a
working multi-provider AI app on Koan in one session, proving the strategy's headline.
PREREQS (verify, else stop): dotnet-new template exists (06 H1) · packages or source path
documented truthfully (06 Track A) · P1.2 introspection + P3.1 grants merged.
WORK: script the session as a checklist FIRST (entity → REST → run + boot report → swap sqlite
to postgres → [Cacheable] → IKoanJob import → [Embedding] + semantic search → [McpEntity] +
grant + an agent reading koan://entities and mutating with audit), then EXECUTE it with a
coding agent against a scratch app, capturing the full transcript (commands, boot reports,
diffs, the agent's tool calls). Edit only for secrets/noise — authenticity is the point; if a
step fails, FIX THE FRAMEWORK GAP (file it) and re-run; the published transcript must be real.
SHIP: docs/case-studies/agent-wedge-demo/ (transcript + the final app source as a sample or
gist link) + README link under the three-beats section.
```

---

## Card → prerequisite map (quick reference)

```text
P1.1 lockfile          ← 06 B1 (sln truth)
P1.2 introspection     ← 06 F1 (fail-loud), P1.1 (composition resource)
P2.1 conformance       ← 06 B1; pairs with 06 H1 template
P3.1 grants/audit      ← P1.2; coordinates with Trust fabric ADR
P3.2 agent ops         ← P3.1
P4.1 tenancy           ← Facet 3 settled (HARD GATE) + P2.1 (kit verifies isolation)
P4.2 AI evals          ← 06 S3 (AI collapse) + P2.1
P5.1 sovereign         ← 06 B2 (CI) for the smoke
P5.2 wedge demo        ← P1–P3 + 06 A/B/H1
```

As each card ships, update [05 §3.1](05-strategic-position.md)'s table status and re-score the
affected pillar in [03-maturity-model.md](03-maturity-model.md).
