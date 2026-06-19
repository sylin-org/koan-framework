# AN-leak · Relationship-expansion visibility bypass 〔SECURITY〕

> **Source**: docs/assessment/09-agent-native-projection.md §10 · 07-AN · **Tier**: T3 (read-path
> security semantics — frontier only) · **Depends on**: — (do first; unblocks AN7)
> Self-contained session prompt — paste this entire file into a fresh session.
> Update [PROGRESS.md](../PROGRESS.md): set the `AN-leak` row `in-progress` when you start;
> `done`/`blocked` when you finish.

---

## Session preamble

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

## Task

**The bug (confirmed, high severity).** The keyed get-by-id read applies WEB-0068 row-visibility
predicates and returns NotFound for a hidden row
(`src/Koan.Web/Endpoints/EntityEndpointService.cs:252-260`). But **relationship expansion** —
`?with=all` over REST and `with:"all"` over MCP — then calls `entity.GetRelatives(...)` with **no
predicate re-application** (`EntityEndpointService.cs:262-268`, and the collection path's
`EnrichRelationships` at `:137-140`). The traversal loaders fetch related rows via
`Data<TChild,TKey>.All()` / `Data<TParent,TKey>.Get()` directly and filter by foreign key
in-memory — raw, app-authority, no hook pipeline (`src/Koan.Data.Core/Model/Entity.cs:806-819`
parents, `:863-920` children). **Result:** a caller reads a *visible* parent, expands, and receives
related rows a direct query of that related type would hide — on REST and MCP. MCP amplifies it: the
agent cannot distinguish "forbidden to see" from "doesn't exist." Same class as the get-by-id bypass
the WEB-0068 comment at `EntityEndpointService.cs:237-240` describes, on a surface the 2026-06-14
read-path sweep missed.

**THE SUBTLE PART — do not break this.** `Entity<T,K>.GetChildren()/GetParents()/GetRelatives()`
are *also* called by application/service code, where they correctly run with **app authority** (no
request predicates — there is no HTTP principal, and `Koan.Data.Core` has no access to
`Koan.Web`'s `QueryOptions.Predicates`). **The fix must NOT make the domain-level traversal API
apply request predicates.** Same method, two callers: app-authority when service code calls it,
grant-bounded **only** when the agent/HTTP read path resolves an expansion. The clamp lives at the
**endpoint layer** (`Koan.Web`), where the request context and the child type's hooks exist — never
in `Koan.Data.Core`.

**ANCHORS** (read before planning):
- `src/Koan.Web/Endpoints/EntityEndpointService.cs` — `:237-260` (the keyed-read predicate gate to
  MIRROR: `BuildOptions` → `PassesRequestPredicates`), `:262-268` (GetById `with=all` → GetRelatives,
  the leak), `:137-140` + `EnrichRelationships`/`GetRelatives` callsite on the collection path
  (the SECOND leak — fix both), `PassesRequestPredicates`, `BuildOptions`.
- `src/Koan.Data.Core/Model/Entity.cs:733-761` (`GetRelatives`), `:806-819`
  (`LoadParentEntity` → `Data<>.Get`), `:863-920` (`LoadChildrenByProperty` /
  `LoadChildEntitiesByProperty` → `Data<>.All` + in-memory FK filter).
- `src/Koan.Data.Core/Relationships/RelationshipMetadataService.cs:49-56` (child relationships keyed
  by `(ReferenceProperty, ChildType)` — same-target-different-field IS supported; needed for T2).
- `src/Koan.Web/Endpoints/DefaultEntityHookPipeline.cs` + the `IRequestOptionsHook<T>` seam (how the
  child type's predicates are produced — you need the CHILD type's hooks, not the root's).
- The MCP read path: `src/Koan.Mcp/Execution/RequestTranslator.cs` (`with` → `With` on the request,
  ~`:104` collection / `:139` get-by-id) + `src/Koan.Mcp/CodeMode/Sdk/EntityDomain.cs` (the SDK
  `collection({with})`/`getById(id,{with})` proxy). MCP rides the SAME `IEntityEndpointService`, so
  fixing the endpoint fixes MCP — verify, do not duplicate the gate per transport (the AN3 lesson).
- `docs/decisions/WEB-0068-query-options-predicates.md` (the governance model + the keyed-read
  amendment). The existing test `Relationship_expansion_path_is_also_gated` gates only the ROOT.

**DECIDED:**
1. On the **agent/HTTP-facing read path**, an expanded/traversed related entity MUST be subject to
   **its own type's** visibility predicates — exactly as a direct `query`/`get-by-id` of that type
   would be. An edge "inherits its resolved query's projection," not the root's. This holds for
   BOTH directions (a parent the caller may not see is omitted, not returned) and BOTH expansion
   entry points (GetById `:262-268` AND the collection `EnrichRelationships`).
2. **Walled means silent** (09 §9.4): a related entity hidden by predicate must produce **no count,
   no field/relationship name, no existence signal** — the same NotFound-equivalence the root keyed
   read already uses. A fully-walled relationship is *absent* from the graph, not present-and-empty
   in a way that leaks the relationship's existence where it would otherwise be hidden. (A visible
   relationship that simply has zero visible children is fine as an empty set.)
3. The domain-level `Entity<T,K>.GetChildren()/GetParents()/GetRelatives()` API stays **app-authority
   and unchanged** — the fix is scoped to the `Koan.Web` endpoint read path. No request predicates
   leak into `Koan.Data.Core`.

**DEFAULT (design call — decide after reading, justify your choice in one paragraph):** the cleanest
mechanism for governed expansion. Two candidates:
- **(a) Re-query through the projector** — resolve each relationship as a governed query against the
  child type via `IEntityEndpointService<TChild,TKey>` (or the same `BuildOptions`+predicate path)
  filtered by the foreign key = the parent id. This is the AN7 "edge = a parameterized query through
  the one projector" model, fixes the leak AND the `All()`+in-memory perf bug, and is the
  forward-looking choice. Generic dispatch over arbitrary child types is the cost.
- **(b) Resolve-and-filter** — keep `GetRelatives` but, at the endpoint, run each related type's
  hook pipeline to obtain ITS predicates and filter the returned rows before emitting. Smaller blast
  radius; still loads via `All()` (perf bug remains); a stopgap unless paired with the perf fix.
  Prefer (a) unless reading the code shows (a) is disproportionately risky this session — and per the
  no-stopgaps rule, if you ship (b), say explicitly why and file the perf follow-up.

**TESTS — these ARE the spec (write them FIRST; they must FAIL pre-fix, PASS post-fix; mutation-check
by reverting the fix). Through `KoanIntegrationHost` (ARCH-0079) with a visibility `IRequestOptionsHook`
registered on the child type; cover BOTH the REST endpoint and the MCP `EndpointToolExecutor` path:**
- **T1 — lateral-movement tunnel.** Parent `Maker` (readable) `[ParentOf Work]`; `Work` walled by a
  predicate for this caller. Read `Maker` with `with=all`. **Assert:** the response carries **no**
  `Work` rows the predicate excludes; a fully-walled relationship discloses **no count, no field
  name, no existence**; a direct `Work` query under the same grant agrees.
- **T2 — divergent edges, same target, asymmetric disclosure (the hardest case).** `Maker` has
  `[Parent(typeof(Maker))]`-style relationships to `Work` via **two different fields** (`authorId`
  open, `reviewerId` walled). Anonymous reads `Maker` with `with=all`. **Assert:** `works-authored`
  present (filtered to visible rows); `works-reviewed` discloses **nothing** (not the edge, not a
  count, not the field name). Hand a grant opening reviewer visibility → `works-reviewed` appears,
  sized to that grant.
- **T-parent — parent omission.** A parent the caller may not see is omitted from the expanded
  graph, not returned.
- **T-app-authority (regression — proves the fix is scoped).** Service code calling
  `maker.GetChildren<Work>()` **directly** (no HTTP request, no endpoint) still returns **all**
  children, unaffected by request predicates. This is the guard that the domain API was not broken.

**DOCS:** amend `docs/decisions/WEB-0068-query-options-predicates.md` (add the expansion/traversal
surface to the per-surface enforcement amendment) + a one-line note in the mcp + web guides if they
describe `with=all`. Update 09 §10 / the AN-leak PROGRESS row to `done` with the commit. If the
domain API gains a predicate-aware overload, note it; otherwise no public-surface change.

**BOUNDARY:** `src/Koan.Web` (the fix) + possibly `src/Koan.Data.Core` (a predicate-aware traversal
overload, if chosen) + tests. `src/Koan.Mcp` is **read-only** here — it consumes the endpoint fix; no
governance logic belongs in the transport (AN3). Do not touch Jobs trees.
