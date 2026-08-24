---
type: GUIDE
domain: engineering
title: "Capability directory"
audience: [maintainers, architects, ai-agents]
status: current
last_updated: 2026-08-24
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-24
  status: reviewed
  scope: pillar-by-pillar inventory assembled from docs/reference/*/index.md, the capability map,
    and one external production consumer (gposingway/bundlingways-emporium.v2); expressions are
    verbatim from reference docs - this page owns no claims of its own
---

# Capability directory

What each pillar lets an application **do**, expressed at its smallest honest size, with the
packages involved, today's evidence, and the next shaping step. Three sibling documents carry
different cuts of the same truth and stay authoritative for it:

- [`product-surface.md`](../reference/product-surface.md) — generated package/maturity truth.
- [`capability-map.md`](../reference/capability-map.md) — outcome → exact package routing ("add AI").
- [`docs/capabilities/`](../capabilities/index.md) — the routed node tree agents fetch at runtime.

This page adds what none of them hold: the **verb-level surface** (pipelines, recipes, receipts,
hooks) in one place, an honest evidence column, and the shaping backlog derived from it.

**Evidence legend:** `verified` = protocol cold-run against published feed · `blind-run` =
exercised in the 2026-08-23 parallel evaluator sweep, node unmarked · `assessed` = documented,
not cold-run · `not assessed` = capability-map label · `shipped?` = source exists, no claim anywhere.

---

## Core — composition and environment

| Capability | Expression | Depth | Evidence |
|---|---|---|---|
| One-line composition | `builder.Services.AddKoan();` | compiles module constitution once; freezes composition | verified (every cold run) |
| Application declarations | `AddKoan(() => { Order.Lifecycle.BeforeUpsert(...); });` | rejected after freeze, correctively | assessed |
| Environment gating | `KoanEnv.Gate.Enforce(new KoanMagic(...))` · `.DevelopmentOnly` | consent flags; refusal names remedy | assessed |
| Explanation surfaces | `/health/live`, `/health/ready`, `/.well-known/Koan/facts`, `koan://facts`, `koan.lock.json` | one facts envelope, many projections | verified |
| NativeAOT publish | `<PublishAot>true</PublishAot>` + app trim roots | metadata pin automatic via Core targets | verified (win-x64 web+console) |

## Data — persistence

| Capability | Expression | Depth | Evidence |
|---|---|---|---|
| Entity CRUD | `new Todo{...}.Save()` · `Todo.Get(id)` · `Todo.Query(x => ...)` · `todo.Remove()` | lifecycle hooks at boundary; provider-elected store | verified |
| Paging / streaming | `Todo.FirstPage(size)` · `Todo.Page(n,size)` · `Todo.AllStream()` | streams only where provider advertises bounded paging | verified |
| Batch upsert | `Todo.UpsertMany(items)` | atomicity = provider capability | assessed |
| Polymorphic entity sets | `class Media : Entity<Media>` + `Media<TVariant>` companions | `__koan_type` hint; root-set filter/page with pushdown | assessed |
| Adapter/source pinning | `[DataAdapter("sqlite")]` · `Koan:Data:Sources:{name}` | unreferenced adapter fails naming choices | verified (correction leg) |
| Store cutover | `Data.Source("Mongo").PromoteToDefault()` → `Plan`/`Run` | cross-store copy w/ readback proof; never auto-rollback | not assessed |
| Relationships | `[Parent]` + `GetParent<T>`/`GetChildren<T>` | direct edges only; scan rejection | not assessed |
| Soft delete | `Sylin.Koan.Data.SoftDelete` opt-in | recycle-bin/restore/purge grammar | not assessed |
| Backup | `Sylin.Koan.Data.Backup` | **no product claim** — do not teach as product | — |
| Conformance testing | `EntityConformanceSpecs<Todo>` | trait batteries skip when provider lacks capability | assessed |

## Web — HTTP projection

| Capability | Expression | Depth | Evidence |
|---|---|---|---|
| Entity HTTP API | `[Route("api/todos")] class TodosController : EntityController<Todo>;` | governed endpoint set incl. `/query`, `bulk`, PATCH; single seam | verified |
| Filter/sort/page over HTTP | `?filter={"status":"Pending"}&page=&pageSize=` (+`size` alias) | filter tree pushed down where declared; malformed → 400 | verified |
| Count/navigation metadata | `X-Total-Count`, RFC `Link` headers | toggled by `[Pagination]` policy | verified |
| Relationship expansion | `?with=...` | direct edges; 422 fail-closed; 413 caps | assessed |
| Context contributors | `IWebContextContributor` (`context.Where<Photo>(...)`) | AND-composed predicates flow into Data reads | assessed |
| Hook pipeline | `IModelHook` · `ICollectionHook` · `IRequestOptionsHook` · `IEmitHook` · `IEntityEnricher` · `IEntityTransformer<T,TShape>` | ordered, same-shape, content-negotiated | assessed |
| Terse CRUD/audit/moderation | `Sylin.Koan.Web.Extensions` (`[RestEntity]`) | explicit realizations only | not assessed |
| OpenAPI | `Sylin.Koan.Web.OpenApi` → `/openapi/v1.json` | swagger UI dev-only by default | shipped? |
| Server-sent events | `Sse.Stream(values)` | typed JSON/text/envelopes; replay NOT implied | shipped? |
| Admin surface | `Koan.Web.Admin` | — | shipped? |
| OpenGraph cards | `Koan.Web.OpenGraph` | — | shipped? |

## Trust & identity

| Capability | Expression | Depth | Evidence |
|---|---|---|---|
| Sign-in providers | config under `Koan:Web:Auth:Providers` | OIDC/OAuth2 config-only or connectors; incomplete intent = fail-fast startup | verified (test connector path) |
| Access rules on entities | `[Access(read:"authenticated", remove:"is:admin")]` | same gate projected to REST **and** MCP; row rules via `EntityAccess<T>` | verified (HTTP leg) |
| Tenant isolation | `using (Tenant.Use(id)) { ... }` | ambient across data/cache/storage/context carriage; fails closed | blind-run (IDOR proven) |
| Field protection at rest | classification attributes + provider | transform-at-rest | not assessed |
| Workload tokens | `Sylin.Koan.Security.Trust` | issue/verify machine tokens | not assessed |
| Durable person identity | `Sylin.Koan.Identity(.Web)` · MFA · passwords | survives provider changes | shipped? |

## Work — jobs and communication

| Capability | Expression | Depth | Evidence |
|---|---|---|---|
| Job-as-entity | `class SendDigest : Entity<SendDigest>, IKoanJob<SendDigest>` | ledger is the queue; no worker infra | verified (restart survival) |
| Actions/chains/gates | `[JobAction]` · `[JobChain("a","b")]` · `[JobGate]`/`[JobPool]` · `[JobIdempotent]` · `[ParallelSafe]` | named actions, lanes, coalescing, concurrency control | assessed |
| Durable execution demand | `[JobPersistence(JobPersistenceMode.DataStore)]` | rejects at composition if unhonored | assessed |
| Schedules + selection submit | `SendDigest.Jobs.Trigger("daily")` · `digests.Where(...).Submit()` · `QueryStream(...).Submit()` | backpressured, order-preserving, receipt-bearing | assessed |
| Execution controls | `context.Progress(.5,"half")` · `context.Backoff(ts, key:)` | keyed backoff; one terminal decision | assessed |
| Deterministic job tests | `JobsTestDriver.From(host.Services)`, fake clock | production engine, no wall time | assessed |
| Entity events | `order.Events.Raise<OrderApproved>()` — lifts over selections and streams | settlement receipts (`WaitForSettlement`) | assessed |
| Transport distribution | `order.Transport.Send(ct)` · named channels (`channel:"priority"`) | snapshot semantics; RabbitMQ connector durable queues | assessed (RabbitMQ not assessed) |
| Scheduling | `Koan.Scheduling` | — | shipped? |

## State & content — cache, storage, media

| Capability | Expression | Depth | Evidence |
|---|---|---|---|
| Cacheable reads | `[Cacheable]` on entity | profiles/provider election; Redis/Sqlite adapters | shipped? (adapters present, node unrun) |
| Entity-owned files | `[StorageBinding("main")] class Document : StorageEntity<Document>` | text/binary create, range reads, Head/Delete, tiering `CopyTo<T>/MoveTo<T>` | assessed |
| Storage election | profiles + `Provider`/`Mode`/priority | compile-once plan; replicated = Local+Remote; facts project `StorageCaps` | assessed |
| Media entity ingest | `Photo.Upload(stream,...)` · `Photo.Store(bytes,...)` | SHA-256 dedup; tenancy/access inherited | verified (upload→derivative) |
| **Media recipe pipelines** | `MediaRecipe.New().AutoOrient().Resize(320).Name("s").Primary().EncodeAs("jpeg",q).Build()` under `[MediaRecipe("card")]` | staged grammar (orient→frame→rotate→shape→size→overlay→metadata→encode); startup validation; versioned fingerprints rotate derivatives; **config replaces code on name collision**; `GET /media/recipes[/{name}?as=appsettings]` inspection | verified |
| Direct pipeline | `source.AsMedia().Apply(recipe).WriteToAsync(dest)` | lazy until terminal; `ProbeAsync`/`ToBytesAsync`/`MaterializeAsync` | verified (dual terminals identical) |
| Serving + diagnostics | `GET /media/{id}/{seed}?w=&q=` | ETag/cache headers; `X-Koan-Media-*`; ad-hoc allowlist; request bounds config | verified |
| Derivative persistence | framework-owned `MediaDerivation` records | best-effort; source may decline | assessed |

## Intelligence — AI and vectors

| Capability | Expression | Depth | Evidence |
|---|---|---|---|
| Chat/embed/stream facade | `await Client.Chat("...")` · `Client.Embed(text)` · `await foreach (var d in Client.Stream(...))` | zero-config local election (Ollama conventional endpoint); call-scoped `Client.Scope(chat:"ollama")` | blind-run |
| Verb-gated operations | OCR/image-gen/transcription/rerank/moderation… | unsupported verb fails correctively, never faked | assessed |
| Prompt objects + catalog | `Prompt.Create(p => p.System(...).Instruct("... {orderId}"))` · `PromptEntry.Save()`/`PromptCatalog.Load(name)` | inert contracts; persisted versions | assessed |
| Runtime source control | `IAiSourceControl.InspectAsync(candidate)` → `Apply/Enable/Disable` | inspect-before-apply; health reset semantics | assessed |
| AI HTTP projection | `Sylin.Koan.AI.Web` → `/ai/chat[/stream]`, `/ai/embeddings`, `/ai/capabilities`… | no auth/quotas added by design | not assessed |
| Vector spaces on sources | `koan.Data.Source("Semantic").Vector<Media>(s => s.Dimensions(1536).Metric(Cosine))` | immutable plans; `Top/Where/AtLeast/Text/SemanticWeight`; execution diagnostics | blind-run (search); clauses per-adapter |
| Vector stores | SqliteVec · Qdrant · Weaviate ✅ · PgVector/Redis/Atlas/Mongo ⚠️ | no silent fallback ever | partial |
| Entity embeddings | `[Embedding]` + `EntityAi` via `Sylin.Koan.Data.AI` | save-time indexing | blind-run |
| RAG / structured output | `Sylin.Koan.AI.Orchestration` | answer-from-own-entities, branch, parse-to-type, stream | **not assessed** |
| Bounded entity agents | `Sylin.Koan.AI.Agents` (`WithSearch<T>()`) | tools generated from Entity shape | **not assessed** |
| Model artifact lifecycle | `Sylin.Koan.AI.Models` | acquire/convert/deploy/version | **not assessed** |
| Review queues | `Sylin.Koan.AI.Review` | human approve/reject/edit over AI output | **not assessed** |
| Evals/drift | `Sylin.Koan.AI.Eval` | gate on metrics | **not assessed** |
| Compute fabric | `Koan.AI.Compute` · `.Training` | — | shipped? |

## Agent surfaces — MCP

| Capability | Expression | Depth | Evidence |
|---|---|---|---|
| Entity MCP projection | `[McpEntity(Name="Todo")] public sealed class Todo : Entity<Todo>;` | advertisement=enforcement; forbidden ops absent per caller | blind-run |
| Business tools + hints | `[McpTool]` · `[McpReadOnly]`/`[McpDestructive]`/`[McpIdempotent]` · `[McpDescription]`/`[McpIgnore]` | hints, not enforcement | assessed |
| Self-description resources | `koan://self` · `koan://entities` · `koan://facts` | caller-scoped discovery | blind-run (facts) |
| Transports | STDIO default; Streamable HTTP deliberate | access/input/correlation preserved on failure | assessed |
| Explorer/operations consoles | `Koan.Mcp.Explorer` · `Koan.Mcp.Operations` | — | shipped? |

## Trusted records — canon

| Capability | Expression | Depth | Evidence |
|---|---|---|---|
| Canonized entity | `class Customer : CanonEntity<Customer>` + `[AggregationKey]` on Email | served at `/api/canon/customer`; `/api/canon/models` explains | not assessed |
| Pipeline phases | `ICanonPipelineContributor<T>` with `Phase => Validation` | phase→order→type ordering; park/fail semantics; 202/422 mapping | not assessed |
| Runtime control | `entity.Canonize()` · `ICanonRuntime.RebuildViews<T>()` | persistence/audit sinks replaceable independently | not assessed |

## Proof & operations — testing

| Capability | Expression | Depth | Evidence |
|---|---|---|---|
| Entity conformance suites | `EntityConformanceSpecs<Todo>` | skip ≠ certification | assessed |
| Container fixtures | `Koan.Testing.Containers/Hosting` | — | shipped? |
| Observability | `Koan.Observability` (OTel extracted, ARCH-0088) | — | shipped? |

## Unmapped families needing triage

`Koan.Classification(+Contracts)` · `Koan.Data.SearchEngine` · `Koan.ServiceMesh.Abstractions` ·
`Koan.ZenGarden(+Contracts)` · the messaging-core source family (no reference index, no map row) ·
`Koan.Identity.Credentials/Mfa/Passwords` ·
`Koan.Web.Backup` · `Koan.Orchestration.Cli.Core` — source ships, no reference index, no capability-map
row, no claim. Triage before anyone teaches them.

---

## Field harvest — production consumers

### gposingway/bundlingways-emporium.v2 (external consumer, pre-1.0 lineage; FFXIV catalog + CMS)

Patterns date to mid-2026 against older packages — treat as ergonomic evidence, not a supported
baseline; re-verify any pattern against the stable feed before teaching it.

Idiomatic highlights worth teaching: seven pinned hero-ladder recipes with version fingerprints;
recipe promotion from ad-hoc `?w=&h=` mutators; SPA consuming strictly named derivatives via typed
recipe unions; ingest-side fluent pipeline (`AsMedia(limits).ResizeFit.PreserveFormat.WriteToAsync`);
brand mascot recipe pinned to a stable slug id for LCP preload.

Friction signals → shaping candidates (ranked):

1. **Derivation lifecycle is app-owned** — write-through + orphan sweep hand-rolled (~7-file prewarm subsystem); blocked partly by missing `Storage.Create(string, Stream)` overload (obsolete byte[] API held alive with `#pragma CS0618`). → candidate capability: framework-owned warm/render API + sweep.
2. **Prewarm via HTTP self-call drained to force write-through**; the app's SSRF egress guard fought loopback until disabled (commit marked temporary). → in-process warm API deletes both problems.
3. **`Upload`/`Store` don't persist the entity row** — three call sites remember `.Upsert()`; one forgotten call = invisible blob. → ergonomics fix: persist-on-upload default or corrective warning fact.
4. **Non-idempotent keyed store writes** — `IOException "already exists"` handling rebuilt client-side. → get-or-create semantics.
5. **Unpinnable-slot story**: gallery/lightbox still emit raw `?w=&fit=&format=&q=` because prewarm can't cover free slots. → either documented pattern or first-class ad-hoc pinning.
6. **Windows-hostile keys**: Local sanitizer rejects `:` → app convention `__`. → document portable-key rule at provider level.
7. **Lineage setters `protected internal`** force duplicated stamp shims per entity subclass.
8. **SVG is a pipeline cliff** — sniff-and-bypass with custom validator/rasterizer. → safe-SVG story or explicit rejection correction.

Their engineering register (MEDIA-0004/0006/0007/0008) tracks several of these upstream already — cross-link rather than duplicate.

---

## Shaping plan

Ladder per pillar: **cold-run the floor → climb to the delight rungs → grow the nodes tree → fix code only where runs expose gaps.**

1. **AI (Leo priority)**: Phase 0 audit done via table above → Phase 1 cold-runs (`ai/semantic-search.md`, `embedding/portable.md`) → Phase 2 rungs: Orchestration RAG → Agents → Models → Review/Eval → then grow `docs/capabilities/ai/` leaves per ARCH-0134.
2. **Media**: convert harvest frictions 3–8 into register entries/triage; decide ownership of prewarm/warm API (framework vs library guidance).
3. **Agents domain coverage**: `AI.Agents` rung doubles as the missing agents-domain validation; `records/canon.md` still needs its cold-run.
4. **Triage the unmapped families** before anyone teaches them; either give them reference indexes + map rows or mark them explicitly internal.
5. **Long tail**: remaining `not-yet-tested` nodes per domain, prioritized by route traffic.
