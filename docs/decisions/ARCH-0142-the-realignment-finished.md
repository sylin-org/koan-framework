---
id: ARCH-0142
slug: the-realignment-finished
domain: Architecture
status: Accepted
date: 2026-11-02
title: A table exists before the host says started, a page and an API share one answer, and what a scope wrote is not told until it stands
related:
  - DATA-0166
  - DATA-0167
  - SEC-0036
  - ARCH-0092
  - ARCH-0141
---

# ARCH-0142: A table exists before the host says started, a page and an API share one answer, and what a scope wrote is not told until it stands

## Context

DATA-0166, SEC-0036, DATA-0167 and ARCH-0141 gave Koan a held transaction, role changes that join it, commands, and `[Tool]` on a controller action. A consumer moved onto them and
met what was still poorly shaped: Koan's own workers provisioned tables just after the host reported started, a form post read a JSON blob, `string == Reason` compiled and was
silently false, and so on. This record closes that list. Each decision is small, and they share one test: a small surface, with the complexity at a seam.

## Decision

1. **Every entity on a default route that holds has its table before the host reports started.** `KoanDataCoreModule.Start` runs before any hosted service. `TablesAtBoot` asks the default route's provider, once, whether it holds, and if so asks each discovered root's
   repository for `EnsureReady`, so a store that holds nothing has no repository made for it. Nothing changes the schema under a running application, so a table is never first made inside a hold (DATA-0166's first-use note) and
   a new connection's first use of an FTS5 table never meets a burst of schema changes, which it cannot survive: SQLite retries a statement once, and answers error 17 if the schema has moved again.
   - An entity that names its own source or adapter stays at its first use, so a store nothing elected is not touched; so does one under a Database axis, which has no finite list of tables, and a partition's table.
   - A tenant-scoped entity gets its table with no tenant in scope: a shared-row dimension is a column of one table. `RepositoryFacade.EnsureReady` binds no segmentation, because it carries no rows, and a row with no tenant is still refused.
   - Roots are made in one order, by full name, so two entities declaring one index name settle the same way on every run. The later one is refused at start and at its first use, and the warning says why and what to do. An entity the host never uses can still be the one that claims the name; the correction is to give one of them another name.
   - A store that refuses says so once, at start, and answers at first use as it would have, so a boot does not depend on it. An entity declared wrongly says so at its first use, as it did.
   - `SecIdentityModule`'s own list of tables is gone.
2. **A string compared with a reason compares its text, whatever its kind, and no string is no reason.** `string == Reason` used to convert the string and compare kinds. `Reason? r = cond ? Fixed : null` no longer throws. Plain strings stay valid reasons.
3. **One mapping answers a browser with a page and a client with a problem.** `this.Answer`, a `[Tool]` action's refusal and its binding failure go through one `Negotiated`: a request whose `Accept` names HTML at least as highly as JSON reads a page, and everything else, `*/*` included, reads
   `application/problem+json`. MVC's own negotiation cannot say this, because it ignores a browser's `Accept` while it holds `*/*`. An application registers `IProblemView` to answer with its own page; with none, a plain page says the same thing.
   `Reason.Gated(word, text, next, heading)` carries what a person reads above the sentence, which is the problem's `title`, so a consumer's map of headings can go.
4. **`CommandExtensions.TriesWaiting` says how many tries wait for their hold.** At zero, a command sent now runs behind every try sent so far.
5. **What a scope wrote is not told until it stands.** `EntityContext.UncommittedScope` names the scope a flow's writes wait on: an open hold, an open deferring scope, or an atomic commit while it dispatches, when `CurrentTransaction` is clear so that an operation does not track itself. `Raise` and `Send` there, a lifecycle hook during dispatch included, read their source inside the scope, as JSON copies, publish nothing, and publish after the commit through `AfterCommit`, under the context they were raised in, as a tracked save does (NK-047). A rollback takes it away, and the acceptance says `AwaitsCommit`. `AfterCommit` now also runs for a scope that tracked nothing.
   `CachedRepository` asks the same question: it retires an entry after that commit rather than seeding it before, and a read in such a scope goes to the store.
6. **The audit chain reads its head from the store, in the transaction it writes in.** There is no head in memory to correct after a rollback, so a verification reports no gap nobody made. An append joins the open hold or is a short hold of its own, so no flow holds a lock while waiting for a store a hold has; a route that cannot hold keeps the lock. In a deferring scope, its atomic commit's lifecycle hooks included, it follows the commit, because a hold of its own there would wait for the flow's own transaction. A second process continues the chain.
7. **Middleware an application adds in `Program` runs for a route.** `KoanWebStartupFilter` mapped its endpoints before `next(app)`, and an endpoint ends the pipeline, so such middleware ran only for a request no route answered. Endpoints now map after `next(app)`: the application's middleware runs after routing, authentication, the web context, authorization and the limiter, and before the endpoint. A tool call goes through it too.
   - `WebApplication` adds authentication of its own around the application's pipeline, and that would put back a principal a web context replaced. `PlacedAuthentication` answers a placed result for the scheme of the result it replaced, whatever type the identity has. A policy is evaluated twice on `WebApplication`, which is the cost.
   - `UseCors()` runs after routing and before authentication, so a default policy answers a preflight and a refusal alike, and a browser reads a 401 as "sign in" and not as a CORS failure. With no default policy it does nothing. A named policy still belongs to the application: `app.UseCors("name")` in `Program` runs after authorization, so it does not reach a refusal.
8. **A principal a web context places is the request's authentication result,** so ASP.NET's policy evaluator denies it 403 and not 401. A rejected credential stays rejected (WEB-0083).
9. **One rule for a tool's name, whichever door made it:** lower-case words joined by underscores, at most 64 characters. Claude's Messages API states `^[a-zA-Z0-9_-]{1,128}$` for a tool's name, and refuses the dot MCP allows. `article.get-by-id` is `article_get_by_id`, `koan.code.execute` is `koan_code_execute`, `analytics.ask` is `analytics_ask`, and an `[McpTool]` with no name is its method's name in those words. A name outside the rule stops the host at start, with the correction.
10. **A result's metadata and a list are the specification's shape.** `CallToolResult` says `_meta`. `tools/list` omits the field that was always null: nothing pages it, and the specification's `nextCursor` is optional.

## What this overturns

- DATA-0166's and DATA-0167's first-use notes, which said first use belonged at boot and left that to each application. DATA-0167's note that a stop can wait for a host's first lexical measurement no longer applies on a route that holds.
- SEC-0036's `SecIdentityModule` tables at boot, which the seam absorbs.
- ARCH-0141's `Answer` answering JSON only, and its record that Koan's dotted names stay for now. `meta` and `next` from its Later list.
- ARCH-0092 §E's demotion of `AllowMutations`. It stays, on the code: it filters a tool out of the registry and refuses nothing a route allows, so it is exposure and not access, and `[Access]` is what authorizes. Replacing it would make a read-only entity an `EntityToolset<T>` subclass with one `[ToolHidden]` per verb. Only the SDK generator's second copy of that filter is gone.
- The rule "observing never provisions" is unchanged, and the specs that read a file's absence at start as evidence of it now observe under `NoDdl`.
- `Koan.Communication`'s stated non-goal of coupling to persistence, in this much: a publication follows its scope. It is still no outbox.

## Considered and not built

Each scored for what it is worth to someone building on Koan, against the surface it costs.

| Item | Score | Why not |
|---|---|---|
| A retry on SQLITE_SCHEMA | 3 | Measured below: one schema change at a time never fails, and only sustained DDL from another connection does. An ordinary read fails there too, so a retry would have to wrap every statement. Koan no longer makes that DDL at run time. |
| A failed `Unknown` commit retiring cache entries | 3 | A native commit that fails with an unknown outcome leaves an entry stale until its TTL. It needs a second callback list on the coordinator for a disk-level failure. |
| One transaction around a table's DDL | 4 | It would make a partition's first use one schema change. Worth building when partitions and lexical search meet in a consumer. |
| `[Pagination]` and overrides reaching a tool (ARCH-0092's drift) | 4 | `[Pagination]` narrows a bound the global safety bound still holds; `AllowRawQuery` is refused on MCP on purpose (WEB-0074); overrides are tune-only by decision (ARCH-0092 §H). A policy lookup from the MCP edge costs a dependency on action discovery for a narrower page. |
| Entity operations through their real route | 3 | An `[McpEntity]` need not have a route at all, and each operation would have to declare its query fields. |
| Transport-free `EntityEndpointResult` | 3 | `IActionResult` is the hook surface's public short-circuit; `ResponseTranslator` reading a status back is tested and correct, and the gate's own refusal is already `AuthorizeDecision`. |
| `KoanBackgroundServiceBase.Check` reporting what a service does | 2 | Its one subclass in this repository is `HealthProbeScheduler`, for which "running" is true. |
| Keeping a terminal `app.Run` in `Program` from answering Koan's routes | 4 | It runs where the application's pipeline runs, ahead of the endpoint, so `app.Run` in `Program` answers every route, controllers included, where it used to answer only what no route matched. Plain ASP.NET does the same to a `MapGet` route (measured: 404 from the terminal). Going around it would mean a pipeline of Koan's own. The correction is `app.MapFallback`, which answers only what no route answers (`ApplicationMiddlewareSpec`). |

## Coalescence

- **Closest pattern:** `SecIdentityModule.TableExists` for the seam; `RoleCollection.Published` for work after a commit; `RouteToolInvoker`'s `Authenticated` feature for the placed result; `RouteTools.Words` for the name.
- **Owners:** `Koan.Data.Core` owns the seam, because it owns routes and the manifest of roots. `Koan.Web` owns the answer and the pipeline. `Koan.Communication` owns what it publishes, and asks the coordinator only for `AfterCommit`. `Koan.Mcp` owns the name.
- **Deleted:** `SecIdentityModule.TableExists`; the audit chain's in-memory head and its seeding; `TransactionOptions.DefaultTimeout` and `LongRunningTransactionWarning`, which nothing read; `TrackVectorSave`, `TrackVectorDelete` and their operations, which nothing called; DuckDB's declaration of `write.mutationOutcomes`, which it does not implement; `ToolsListResponse.Next`; `DescriptorMapper`'s kebab case.

## Ergonomics

An application writes nothing for items 1, 5 to 10. A new table costs nothing at boot on a store where it exists: `PRAGMA table_info` per entity. On a fresh store it costs what first use cost, at a moment nobody is waiting. New public concepts: `IProblemView`, `Reason.Heading`, `CommandExtensions.TriesWaiting`, `EventAcceptance.AwaitsCommit`, `TransportAcceptance.AwaitsCommit`, `EntityContext.UncommittedScope`.

## Compatibility

- A host with SQLite elected as its default creates its database and tables at start, where a host that never touched an entity used to leave no file. Under `NoDdl` it creates nothing and logs which entities have no table.
- Every entity tool name, `koan.code.*`, `koan.cache.*`, `koan.jobs.*` and `analytics.*` is renamed by the rule. An `[McpTool(Name = ...)]` outside it stops the host. A client that read `meta` reads `_meta`.
- A browser reading a refused `this.Answer` reads a page. A client sending `Accept: text/html` first reads it too.
- Middleware added in `Program` now runs for a route, after authorization. An application that relied on it seeing only unmatched requests must check `context.GetEndpoint()`. **A terminal `app.Run` in `Program` now answers every Koan route, controllers included: use `app.MapFallback`.** A default CORS policy answers before authorization whether or not `Program` calls `UseCors`; a named policy in `Program` does not reach a refusal.
- `Raise` and `Send` in a scope accept nothing until commit; `WaitForSettlement` on such an acceptance refuses and says so. A process that ends between a commit and its publication loses the publication. A read inside an open deferring scope goes to the store and not the shared cache.
- `EnsureReady` under a closed tenancy posture now makes a tenant-scoped table where it refused.
- `AuditEvent.Sequence` gains an index. The chain's rows are unchanged, and a chain written before this continues.
- `string == Reason` now compares text where it compared kind and text.

## Evidence

- `SqliteBootProvisioningSpec` (4) and `SchemaSettledSpec`, a host whose job worker starts with it: an untouched entity, its FTS5 table and a non-string key exist at start; `schema_version` does not move after start; a file missing a table has it back; `NoDdl` does not stop a boot.
- **What SQLite does,** measured through Koan's verbs on a new connection each round for five seconds, with DDL on another connection: one `CREATE TABLE` every 30 ms, all 273 rounds of search, rebuild, status, save and query answered. Eight in a burst every 30 ms, 2 to 4 of about 223 failed, a save through its trigger included. A tight loop, most failed, and an ordinary query failed with "database schema has changed". The reproducer's tight loop stands outside what Koan does now.
- `ReasonSpec` (4), `ProblemNegotiationSpec` (7), `ToolRouteSpec`, `ApplicationMiddlewareSpec` (5: on `WebApplication` and a generic host, a tool call through it, a placed principal surviving, an application's view), `ScopedPublicationSpec` (3), `CacheHoldSpec`'s atomic case, `AuditChainHoldSpec` (4), `AuthEndToEndSpec`'s 403, `ToolNameWireSpec` (4), `ToolsThatCannotStartSpec`'s name cases, `DuckDbCapabilitySpec`'s outcome case.
- **Measured by removing them,** one at a time: with no tables at start, the boot spec's three cases and the job worker's fail; with `==` and the conversion as they were, `ReasonSpec` fails; with no page, `ProblemNegotiationSpec` fails; with `TriesWaiting` answering zero, its case fails; with either coordinator publishing in a scope, `ScopedPublicationSpec` fails; with no placed result, the 403 fails; with the head in memory, a rollback reports a gap and an append outside a hold holds the hold to its limit; with a deferring scope not deferred, its case fails; with endpoints mapped before the application's pipeline, four of `ApplicationMiddlewareSpec`'s five fail; with no `PlacedAuthentication`, the placed principal fails; with a dot in a name and the check off, `ToolNameWireSpec` fails; with `meta`, its case fails; with DuckDB's declaration back, its case fails; with the cache reading only a hold, the atomic case fails. With a placed result reading the scheme after the user changed, the placed and narrowed cases with another identity type fail; with the chain testing only `CurrentTransaction`, the hook's case fails; with either coordinator doing the same, the hook's rollback case fails in the suite that composes Communication with SQLite; with segmentation bound for a table, `TablesAtBootSpec` fails; with either coordinator capturing at commit, its tenant case fails; with roots made in the other order, both index-name specs fail; with no `UseCors`, the refusal's case fails.
- **The consumer:** Tangent on these commits, its merge check: build, 471 specs, its rules. With a table made at start on a fresh file of 116 tables, a boot took 417 ms, and 218 ms on the same file again, in a process already warm.

## Later

- `KoanEnv`'s snapshot stays process-wide, from ARCH-0141's list.
