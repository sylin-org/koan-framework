# 08 — The agyo-tools Reorganization

> Status: **Proposal** (2026-06-14) · Supersedes the *removal* framing of the 06/07 prompt stash for the capabilities listed here.
>
> The removal prompts asked one question — *cut or keep?* — and the external-consumer gate kept catching a third answer: **"neither — it's a useful helper that shouldn't be in the core, but shouldn't be lost."** `agyo-tools` ("PowerToys for Koan") is the home for that third answer. This document re-classifies every removal target into **core-Koan / agyo-tools / delete**, with the layering and the migration path.

## The reframe

The framework's core identity is **data · web · cache · jobs · mcp · auth · storage**. A capability can be *good*, *consumed*, and *well-engineered* and still not belong in that core. Before `agyo-tools` existed, the only ways to express "not core" were **cut** (lose it) or **attic** (freeze it). That forced a false binary, and the consumer gate exposed it four times this session (Tagging, Recipe.Observability, Secrets, Scheduling were all "cuts" that turned out to be consumed).

`agyo-tools` is a **sibling repo that depends on Koan's published packages and is never referenced by Koan** (STACK-0001 layering law: names never flow down). It is the destination for opt-in, peripheral, app-building helpers — the ones an application *reaches for* but the framework doesn't *need*.

## Method

Each candidate was evaluated by an independent agent that read the capability's **actual code** (from the tree, `attic/`, or git history for the already-cut ones), assessed its Koan-dependency surface (extractability), capability value, and quality, and classified it. Downstream-consumption status was supplied as ground truth (the consumer is never named — persona separation). 11 capabilities, 0 found to be core.

## The verdict

| Capability | LOC | Quality | Verdict | One-line rationale |
|---|---|---|---|---|
| **Koan.Tagging** | ~290 | solid | **agyo-tools** | Distinct app helper, consumed, zero core coupling — clean lift. |
| **Koan.Secrets** (Abs+Core+Vault) | ~640 | solid | **agyo-tools** | Dormant-complete, consumed; core touches it only via a fail-soft reflection probe (stays as-is). |
| **Koan.Scheduling** | ~470 | rough | **agyo-tools** | Consumed lightweight in-proc scheduler; ~60% vaporware — finish or document as the minimal alt to Jobs. |
| **Koan.Rag** (Abs+impl) | ~8025 | solid | **agyo-tools** | Sophisticated opinionated RAG stack; zero consumers but high value; pure public-surface composition. |
| **Koan.Web.Connector.GraphQl** | ~1300 | rough | **agyo-tools** | GraphQL-over-entities (visibility-correct); HotChocolate CVE-treadmill belongs on *its* cadence, not Koan's. |
| **Koan.WebSockets** | ~200 | solid | **agyo-tools** | Bidirectional duplex streaming — genuinely distinct from SSE; thin shim over the .NET 10 BCL. |
| **PGVector connector** | ~1360 | solid* | **agyo-tools** | "Vectors in the Postgres you already run" — real draw; *needs a ~few-hour finish (stale compile gap). |
| **Koan.Recipe** (Abstractions / Observability) | ~230 | split | **split** | **Delete** the superseded bootstrap idiom (AppDomain-scan anti-pattern); **move** the observability bundle as a `KoanModule`. |
| **Koan.ServiceMesh + Translation** | ~2431 | split | **split** | **Delete** the experimental mesh (return-path is a future trust-fabric ADR, not a port); **move** Translation, decoupled. |
| **Strict-STJ binding** (Koan.Web.Json.Strict) | ~280 | dead | **delete** | Superseded canon (05 §6.1 Newtonsoft); reviving it even in agyo-tools reopens "two serializer worlds". |
| **Confirmed-dead bucket** (5 surfaces) | ~1700 | dead | **delete** | Cqrs→Jobs outbox · Inbox-Redis→Jobs idempotency · AI Pipelines→EntityAi · Media output-cache→storage derivations · ResilientStorageDecorator→Mode=Replicated. |

**Tally:** core-Koan **0** · agyo-tools **7** (+2 half-splits) · delete **2 buckets** (7 surfaces). Not one removal target was actually core — which validates the removal instinct — but **9 of them carry preservable capability**, which is exactly what the cut-or-keep binary couldn't express.

## Bucket A — moves to agyo-tools

Every item here ports against **Koan's public packages only** — verified, zero reach into Koan internals or `InternalsVisibleTo`. So each becomes a sibling project whose in-repo `ProjectReference`s turn into `Sylin.Koan.*` `PackageReference`s, and Koan keeps **no seam** unless noted.

1. **Tagging** — wholesale lift (7 src + 4-spec suite). Cleanups: a README cites a `TagScopeJsonConverter` that doesn't exist; `Tag.cs` cites a dangling `ADR-0018`. No seam.
2. **Secrets** (3 projects) — wholesale lift. **The one seam stays in Koan untouched:** `TryInvokeSecretsBootstrap` in `Koan.Data.Core/ServiceCollectionExtensions.cs` is a reflection-only, fail-soft probe (`Type.GetType(..., throwOnError:false)`); it resolves when the assembly is present downstream and no-ops when absent. `Koan.Data.Core.csproj` has **zero** Secrets references, so nothing compiles against it — clean.
3. **Scheduling** — lift the whole project. **De-bloats core on the way out:** `Koan.Web.csproj` hard-references Koan.Scheduling today, so *every* web app runs the 1-second poll loop with zero tasks — drop that reference and the dead `/.well-known/Koan/scheduling` endpoint. Move the `KoanServiceEvents.Scheduling` / `KoanServiceActions.Scheduling` constant groups out of `Koan.Core` (names never flow down). **Open decision below:** finish the promised cron/locks/windows, or ship it documented as the deliberately-minimal alternative to the durable Jobs ledger.
4. **Rag** (both projects, currently in `attic/`) — move out; swap ProjectRefs for `Sylin.Koan.*` NuGet. Drop the dangling `InternalsVisibleTo Koan.Rag.Tests` (author a real suite in agyo-tools — it ships untested, which is itself why it never belonged in Koan's integration-tests-as-canon core), drop the `KoanPackageKind` props, delete the committed `bin/obj` artifacts.
5. **GraphQl** (currently cut — recover from tag `attic/koan-web-graphql`) — move; retarget to public packages. The WEB-0068 hook pipeline it rides is already public, so no seam stays. Moving it takes the HotChocolate-13.9.16 CVE pin off Koan's release train.
6. **WebSockets** (currently cut — recover from `ffef0899~1`) — move all 9 src + 5 tests. The value over the bare BCL is `AcceptWebSocketStream` (upgrade + sub-protocol + wrap) plus the options/provenance binding.
7. **PGVector** (currently cut — recover from tag `attic/pgvector`) — move + **finish** (~hours): delete the now-cut `IVectorFilterTranslator<PGVectorWhere>` interface + `PGVectorWhere` record, keep the already-present static `Translate` + `FilterSupport Caps` (the exact migration Qdrant/Milvus/Weaviate already received), re-enable `IsPackable`, add the ARCH-0079 integration spec against a real pgvector container.
8. **Observability bundle** (the live half of the Recipe split) — extract as a **`KoanModule`**, not an `IKoanRecipe`. It wires `AddHealthChecks` + a resilient `Koan-observability` HttpClient (the advertised OTel was never implemented — optionally make it real now). **Do not** fold it into `Koan.Web`'s registrar (that forces health-checks-always-on into every web app — and that exact fold was tried as C5 this session and reverted).
9. **Translation** (the live half of the ServiceMesh split — recover from `f7d8a499~1`) — extract, **decoupled from the dead mesh**: strip the `[KoanService]`/`[KoanCapability]` attributes and the `ServiceExecutor<TranslationService>` indirection; rewrite the static `Translation` facade to call `TranslationService` directly (DI/`AppHost`, or a small `KoanModule`). Its only real dependency is `Koan.AI` (`Client.Chat`/`ChatOptions`), all public.

## Bucket B — confirmed delete (no agyo-tools value)

Each is a strictly-inferior predecessor of a capability Koan already ships as a live core pillar — they fail the "distinct helper" bar by definition.

- **Koan.Data.Cqrs** (already cut) — superseded by the Jobs durable-ledger outbox (JOBS-0005; DATA-0019 Superseded). Leave deleted.
- **Inbox-Redis connector** (already cut) — a server with no client (`HttpInboxStore` no longer exists); idempotent-inbox is now Jobs at-least-once + `[JobIdempotent]`. Leave deleted.
- **Koan.AI Pipelines** (still in tree, inert — internal ctors, no entry point, `ImagePipeline.ToImage` throws unconditionally) — superseded by `EntityAi.Embed/Chat/Ocr`. Delete on the C11 schedule; no call-site fallout.
- **Media.Web output-cache** (`[Obsolete]`, MEDIA-0008 already scheduled) — superseded by storage-backed derivations. Still wired (the `_legacyCache` probe in `MediaController` + a `TryAddSingleton`); removal strips those two call sites.
- **ResilientStorageDecorator** (already cut) — the *only* item that could technically port (public `IStorageProvider` seam), but **don't**: untested, superseded by `Mode=Replicated`, and needs an external circuit driver (`GardenAwareEndpointManager`) absent from any Sylin repo. Shipping it would re-introduce a competing outage-resilience path.

**Strict-STJ** also deletes, deliberately not routed to agyo-tools: it's superseded canon (05 §6.1), unconsumed, and its substance is ~15 lines of `JsonSerializerOptions` tightening any app re-derives in minutes. The real latent need — strict **bulk-import validation** — is a *new, differently-shaped* capability (schema/contract validation over inbound payloads) to design fresh in agyo-tools, not a revival of this Minimal-API binding hardener.

## Reconciliation with this session's cut campaign

The campaign was **mostly right** — the genuinely-dead got deleted correctly, and the consumer gate already protected the four consumed packages (they're still in the tree). The reorg's net change is: **resurrect four cut-but-valuable capabilities** from git instead of losing them, and **earmark four protected ones** for their real home.

| Capability | This session's state | Reorg action |
|---|---|---|
| GraphQl | cut (tag `attic/koan-web-graphql`) | **resurrect → agyo-tools** |
| WebSockets | cut (`ffef0899~1`) | **resurrect → agyo-tools** |
| PGVector | cut (tag `attic/pgvector`) | **resurrect → agyo-tools** + finish |
| Translation | cut with the mesh (`f7d8a499~1`) | **resurrect → agyo-tools** (decoupled) |
| Rag | parked (`attic/Koan.Rag`) | **move attic → agyo-tools** |
| Tagging | protected (in tree) | **earmark → agyo-tools** |
| Secrets | protected (in tree) | **earmark → agyo-tools** (seam stays) |
| Scheduling | protected (in tree) | **earmark → agyo-tools** + de-bloat `Koan.Web` |
| Recipe.Observability | reverted, in tree | **earmark → agyo-tools** (as KoanModule) |
| Cqrs, Inbox-Redis, ServiceMesh, Storage-decorator | cut | **stays deleted** |
| AI Pipelines, Media output-cache | in tree | **delete on schedule** |

Nothing valuable was lost — everything is recoverable from a tag, a commit ref, or `attic/`.

## agyo-tools structure

```
agyo-tools/
├── Agyo.sln
├── Directory.Build.props        # version.json (NBGV), <PackageId> convention, Koan public-package versions
├── docs/
│   ├── CHARTER.md               # "PowerToys for Koan": opt-in helpers, depends on Koan, never referenced by it
│   └── STACK-0001-*.md          # the shared layering-law copy (agyo sits above Koan)
├── src/
│   ├── Tagging/                 ├── Secrets.Abstractions/  ├── Secrets.Core/  ├── Secrets.Connector.Vault/
│   ├── Scheduling/              ├── Rag.Abstractions/      ├── Rag/
│   ├── Web.GraphQl/             ├── WebSockets/            ├── Data.Vector.PGVector/
│   ├── Observability/           └── Translation/
└── tests/                       # ARCH-0079-style integration specs per capability (Rag/PGVector/GraphQl notably ship untested today)
```

**Layering law (binding):** every project references `Sylin.Koan.*` **published packages**, never a Koan `ProjectReference`. Koan has zero knowledge of agyo-tools. This is the STACK-0001 direction; all 11 capabilities already satisfy it (that's *why* they're extractable).

## Transition safety (protect the consumer)

The downstream consumer references `Sylin.Koan.Tagging` / `Sylin.Koan.Secrets.*` / `Sylin.Koan.Scheduling` / `Sylin.Koan.Recipe.Observability` as **published NuGet packages**. The cutover must be non-breaking:

1. Stand up agyo-tools and get its packages **building + publishing green** *before* removing anything from Koan.
2. Keep Koan publishing the moved packages until agyo-tools republishes them and the consumer re-points. **Do not unpublish or delete from Koan's publish set until the consumer is confirmed re-pointed.**
3. Only then remove the projects from `Koan.sln` and sweep the doc ledgers (`modules-overview.md`, `module-ledger.md`, `capability-map.md`), per the rotation contract.

## Open decisions (need your call)

1. **Package identity.** Two paths, and it's genuinely yours because it touches the consumer you spent this session protecting:
   - **(A) Keep `Koan.*` namespaces + `Sylin.Koan.*` package IDs, sourced from agyo-tools** — *zero* downstream churn; the consumer never notices the repo move. Cost: the "agyo" identity is invisible in package names.
   - **(B) Rebrand to `Agyo.*` / `Sylin.Agyo.*`** — clean "these are tools, not core" identity. Cost: a breaking change for the consumer (package refs + `using` namespaces); needs a deprecation/forwarding window.
   - *Recommendation:* **(A) now, (B) later behind a forwarding shim** — protects the consumer first, lets identity follow.
2. **Scheduling:** finish the promised cron/locks/windows in agyo-tools, or ship it documented as the deliberately-minimal in-proc alternative to the durable Jobs ledger? (Shipping as-is perpetuates ~60% vaporware.)
3. **OTel in the Observability bundle:** implement it on the move (make the name honest), or carry the bundle as health-checks + resilient-HttpClient only?

## Suggested execution order

1. **Found agyo-tools** — sln, `Directory.Build.props` (NBGV + Koan public-package versions), CHARTER, the STACK-0001 copy, CI (build + the publish pipeline), the layering-law guard.
2. **Resurrect the cut-but-valuable** (lowest risk, nothing in Koan changes): GraphQl, WebSockets, PGVector, Translation from their refs; Rag from `attic/`. Retarget to NuGet, get them green. **Finish** PGVector; **decouple** Translation.
3. **Move the earmarked** (consumer-facing — do under transition safety): Tagging, Secrets, Scheduling, Observability. De-bloat `Koan.Web` (drop the Scheduling hard-ref), keep the Secrets seam.
4. **Re-point the consumer**, confirm green, *then* remove from `Koan.sln` + sweep ledgers.
5. **Execute the confirmed deletes** in Koan (AI Pipelines, Media output-cache on their schedules).
