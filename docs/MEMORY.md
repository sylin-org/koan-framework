---
type: GUIDE
domain: framework
title: "Koan durable working memory"
audience: [maintainers, architects, ai-agents]
status: current
last_updated: 2026-08-20
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-20
  status: reviewed
  scope: pointer index and durable learnings; owns no state of its own
---

# Durable working memory

Several people and several agents work this tree in parallel, so anything that must outlive one
session belongs here, in the repository, rather than in a single assistant's private store. An
assistant's own memory is a cache; this file is the source.

It **points**. State lives once and decisions live once, in the documents named below — restating
them here would create a second copy to drift. What this file adds is the part that is written
nowhere else: how to work in this tree, and what earlier sessions learned the hard way.

Sensitive or session-scoped notes stay out of git — see [local/README.md](../local/README.md).

## Where current state lives

| Question | Authority |
|---|---|
| What may I change, and by what law? | [CLAUDE.md](../CLAUDE.md) |
| Which agent surface am I in? | [AGENTS.md](../AGENTS.md) |
| What does the framework offer today, and how proven is it? | [docs/reference/product-surface.md](reference/product-surface.md) (generated) |
| Why is it this way? | [docs/decisions/](decisions/) — ADRs are dated records; a later one supersedes or amends, never edits |
| What is deliberately deferred? | [docs/initiatives/koan-v1/POST-CYCLE-TODO.md](initiatives/koan-v1/POST-CYCLE-TODO.md) |
| How does a release happen? | [docs/engineering/nuget-publishing.md](engineering/nuget-publishing.md) |
| What does an application look like? | [samples/README.md](../samples/README.md) |
| Before changing production code | [.codex/skills/explore/SKILL.md](../.codex/skills/explore/SKILL.md) |

## How to work in this tree

- **Verify empirically; do not reason-and-assert.** Probe the real store, read the startup facts,
  run the thing. Several confident claims in this repo's history were wrong in a way one command
  would have caught.
- **`git ls-files` is the authority for what exists.** Ignored `bin`/`obj` left in `src/` make
  retired packages look live. `ls` has produced false conclusions here more than once.
- **Root fix, not spot fix.** Do not drop a capability to the in-memory floor to make a suite green;
  repair the owner. Where two implementations converge, collapse them rather than adding a third.
- **Reference = Intent.** The canonical bootstrap is a bare `AddKoan()`. A sample that needs an
  argument to compose is reporting a framework gap, not configuring itself.
- **Never hand-edit a package version.** Versions come from NBGV; releasing fast-forwards `main`.
- **Fix the seam.** When a feature cannot ride an existing contributor pipeline, the pipeline is what
  needs work — bespoke per-feature logic is how axes drift apart.

## Durable learnings

- **A guard is not trusted until it has failed against the thing it exists to catch — and running that
  trial is how you find the cells you are missing.** Three defects, three trials. Two were caught by the
  obvious cell. The third — `Assembly.GetName()` on a satellite resource assembly in invariant mode — was
  caught by *nothing*: SQLite ships no satellites, and the SQL Server build carries culture data because
  SqlClient demands it, which makes its satellites nameable. Both cells went green with the bug present.
  Reasoning about coverage would have concluded the lane was complete; breaking it on purpose is what
  showed the hole, and a cell now exists that puts satellites and invariant mode in one process.
  (2026-08-21)
- **The cheap proxy for an expensive check is worth exactly what it catches, and that is measurable.**
  PMC-050 first proposed an ILC compile instead of a publish-and-run, on cost. Reintroducing the
  `MetadataToken` defect settled it: the publish **succeeded** and the binary exited 1 on the first entity
  it mapped. The proxy would have gone green on the exact regression that invalidated an accepted ADR for
  five weeks — worse than no check, because it would have reported a certified single binary that dies on
  startup. Before accepting a proxy, run the real failure through it. The resolution is layering, not
  choosing: the grep became ratchet leg F for the constructs already known to break a binary, and the
  publish-and-run stayed the authority. A cheap check earns its place as a complement to the real one and
  never as a replacement for it. (2026-08-21)
- **A defect that fires only on a first run needs the guard to manufacture a first run.** The
  reference-manifest bug appears only when the RID-specific intermediate directory does not yet exist, so
  it reproduces on a fresh CI checkout and hides on any developer machine that has published once. The
  same check would have been honest in CI and quietly meaningless locally. `aot-verify.ps1` deletes the
  sample's `obj` before publishing, so both mean the same thing. Ask what state a check silently depends
  on. (2026-08-21)
- **Put the expensive proof on a schedule, not on a milestone.** ARCH-0093's AOT proof was a manual
  certification activity, and that is precisely why five weeks passed with a false claim standing in an
  accepted ADR. `skills-verify.yml` had already established the pattern here — cheap structural check
  per-PR, the real thing on a cron — and the same shape fits any capability whose proof is slow: the
  guard must not require a human to decide to run it. (2026-08-21)

- **A proof nothing re-runs is a claim with a shelf life.** ARCH-0093 certified the NativeAOT single binary on
  2026-07-17. The mapping compiler began using `MemberInfo.MetadataToken` on 2026-08-06, which ILC does not
  keep, and from that day every AOT-published Koan application failed on the first entity it mapped. Five weeks
  passed with the claim standing in an accepted ADR, and it was found only because an unrelated question sent
  someone to re-measure. The defect was not subtle; nothing was watching. When a capability is proven by a
  manual procedure, the procedure is the capability — schedule it or expect it to expire. (2026-08-21)
- **Do not cite a precedent without `git ls-files`.** A handoff prompt named
  `samples/guides/g1c2.GardenCoopEmbedded` as the worked SQLite precedent, on the strength of a PROGRESS.md
  entry describing it in detail. It does not exist in the tree. The prompt's own constraints told the reader to
  treat `git ls-files` as the authority, which is the check its author had skipped. Secondary documents describe
  the tree on the day they were written. (2026-08-21)

- **A certified capability decays silently; re-measure it before building on it.** ARCH-0093 certified a
  SQLite NativeAOT binary on 2026-07-17. Three weeks later a mapping compiler landed that ordered properties by
  `MemberInfo.MetadataToken` — which does not exist under ILC — and every AOT publish died on the first entity
  mapped. Nobody re-published, so the ADR still read as proof and a guide still read as blocked, both wrong in
  opposite directions. The question asked (do the *servers* publish?) could not be answered without first
  discovering the *floor* had stopped publishing. Where a capability has no suite, its certification is a
  timestamp, not a fact. (2026-08-21)
- **"The provider doesn't support it" is a hypothesis about someone else's code; check your own first.** The
  SQL Server AOT binary failed with `CultureNotFoundException`, which reads as a `Microsoft.Data.SqlClient`
  limitation. It was Koan's: `AppBootstrapper` called `Assembly.GetName()` on every assembly, which materializes
  the culture, and SqlClient merely happens to ship eleven satellite resource assemblies that an invariant
  process cannot name. The genuine SqlClient constraint was a *different* error found after fixing ours — an
  explicit `NotSupportedException` refusing invariant mode, which is driver policy and not about AOT at all.
  Two failures wearing one provider's name, and only one belonged to it. (2026-08-21)
- **A defect that only fires on the first run hides in a tree where everything has been built once.** The
  reference-manifest writer never created its RID-specific intermediate directory, so the first `-r <rid>`
  publish of any Koan application failed — and the second succeeded, on the directory the failure had left
  behind. It survived because the samples had all been published before. A new project is the only thing that
  tests a first run; reach for one when a build step looks unconditionally correct. (2026-08-21)
- **Make the probe name who answered.** The AOT relational probe prints the elected adapter
  (`adapter=NpgsqlRepository`2`) before it writes, and the row is then confirmed in the container from outside
  the application. Without both, a connector that failed election and fell back to another store would produce a
  perfectly green write-then-read — the proof would be of the fallback, not of the thing under test. An
  end-to-end assertion that reads back what it just wrote is self-consistent by construction. (2026-08-21)

- **Do not write a capability claim into a durable document ahead of its evidence — including your own.**
  Removing Dapper removed the blocker ARCH-0093 named, and the consequence "NativeAOT stops being SQLite's
  private property" went straight into DATA-0120. It is not established: whether a server adapter publishes
  under NativeAOT depends on `Npgsql`, `Microsoft.Data.SqlClient` and `MySqlConnector`, none of which had been
  looked at. The claim was demoted to a hypothesis and carried as PMC-049 the same day it was written. Four
  register entries closed this session had been misfiled by earlier confident notes, two of them mine; an ADR
  is a worse place to leave one than a register. (2026-08-21)
- **A centralized seam pays for itself the first time a provider disagrees.** Moving three adapters onto one
  parameter binder surfaced that Npgsql refuses to bind a CLR enum without an explicit type while SqlClient and
  MySqlConnector convert silently. Before the collapse that would have been three separate discoveries, each
  found by whichever store's suite happened to exercise an enum filter. After it, one fix in one method served
  every adapter — which is the concrete form of "complexity centralized at one owner". (2026-08-21)

- **Ask what a dependency is actually being used for before designing around it.** Dapper was treated as a
  constraint that split the relational adapters in two — the AOT floor without it, the servers with it — and it
  shaped an ADR. Reading the call sites showed every one of them was untyped or scalar, each immediately casting
  the row to a dictionary: the compiled materializer that is Dapper's entire reason for existing, and the exact
  thing NativeAOT forbids, was never called. The dependency was costing an architectural split and providing
  nothing. The check took one grep of the call shapes. (2026-08-21)
- **When a shared surface has no consumers, suspect the seam's altitude, not the idea.** ARCH-0093 built an
  AOT-clean command surface and a Dapper twin of it; the twin was retired for having no consumer, and the
  original then sat unused too. The instinct was right and the level was wrong: helper wrappers around
  `connection.QueryAsync` own mechanism, and nothing reaches for a seam that owns no semantics. The surface
  found its consumers the moment something above it — the repository execution path — actually needed one.
  (2026-08-21)

- **Read for the constraint before designing the collapse.** DATA-0120 opened proposing one relational core over
  four adapters. Reading found that SQLite uses raw ADO and references Dapper nowhere, because Dapper emits IL
  at runtime and NativeAOT forbids it — ARCH-0093's shipped single-binary proof depends on that. A Dapper-based
  core would have excluded SQLite; a Dapper-free one would have re-litigated a decided question. The constraint
  was findable in ten minutes and would have invalidated weeks of work. (2026-08-21)
- **"Obviously a decision" is as unreliable as "obviously duplication".** Of the members an inherited measurement
  labelled genuine divergences, `Order` and `Query` both turned out to be grammar — `Query` differs only in
  `OFFSET…FETCH` against `LIMIT…OFFSET`. Meanwhile `Upsert`, which looks like boilerplate, is three idiomatic
  strategies with different transactional semantics, and `Count` hides an overflow bound in `COUNT_BIG`. The
  label has to come from reading the member, in both directions. (2026-08-21)

- **"Flaky" is a diagnosis, and it is usually the wrong one.** A Jobs spec failed in batch runs and passed
  alone, on one store and then another, and was filed as a test-isolation problem. It was a spec depending on an
  order nothing specified: two ledger rows seeded at the same instant, tied on the claim window's entire sort,
  and the conveyor's behaviour differs by which it reaches first. Load changed the arbitrary choice, which is
  what produced the intermittency. An unrelated change then made the order deterministic and the failure
  reproducible on three stores — which is how it finally became diagnosable. Before filing intermittency as
  isolation, look for a tie the test is silently resolving. (2026-08-21)
- **Making an order deterministic will break whatever was relying on it being arbitrary.** Appending an identity
  tiebreaker to paginated reads was correct and well covered by the data suites; the failure landed two layers
  away, in the jobs conveyor's claim window, where a spec's two rows were tied. Any change that turns an
  unspecified order into a specified one needs its blast radius measured where orders are *consumed*, not only
  where they are produced. (2026-08-21)

- **A spec can encode the defect as the expected behaviour, and then nothing will ever catch it.**
  `PageOrderOwnershipSpec` asserted that a paged query carrying a caller's sort reaches the adapter with
  *exactly one* sort spec. That is precisely the bug — no tiebreaker — written down as the contract and kept
  green. When a defect has survived a suite that looks like it covers the area, read what the spec asserts
  before concluding the area is covered. (2026-08-21)
- **Closing half a law leaves the other half looking closed.** DATA-0119 moved "the order a page is a window
  onto" to the framework and closed the case where the caller named no order. Naming a sort is not naming a
  total order, and the second case sat open for a cycle under a rule everyone believed was finished. When a
  guarantee is stated as "X must always hold", enumerate the ways X can fail to hold, not the one that
  prompted the work. (2026-08-21)

- **Similarity metrics cannot answer "is this duplication".** Two attempts at scoring the four relational
  repositories both misled: raw ratios put `GetMany` at 34% when the two bodies are the same code (the score
  was eaten by `plan.` versus `_plan.` and four connection type names), and folding those differences away made
  it worse, scoring `UpdateSet` at 3% through a distortion the normalizer introduced. A ratio understates
  duplication wherever a mechanical difference repeats and overstates it wherever two stores spell different
  logic alike. Where the question is whether two implementations are the same, read them. (2026-08-21)
- **The odd one out may be the only one that is right.** Reading `Order` across four adapters to plan a
  collapse: three were identical down to the comment, and MySQL differed. The tempting reading is that MySQL
  drifted. The truth is that MySQL appends the identity to every ORDER BY as a tiebreaker and is the only store
  whose paged reads are stable over a non-unique sort — a framework gap the other three inherit. A collapse
  driven by majority rule would have deleted the fix and kept the bug. Explain the outlier before flattening
  it. (2026-08-21)

- **Check the premise of a deferred entry before believing its subject.** A register entry recorded MySQL's
  filter corpus as unmeasurable because seeding threw. The corpus was not the subject: `JSON_UNQUOTE` renders a
  JSON null as the string `null`, which is what a stored generated column is built from and what every filter
  emits, so the adapter could not write an entity with a null nullable scalar at all. It had survived because
  MySQL's suite was the smallest of the relational set and nothing in it wrote a null. A test-plumbing entry
  was a data-loss-adjacent defect. (2026-08-21)
- **When two stores fail the same way, check whether they fail for the same reason.** SQL Server and MySQL both
  refused to index text, so I declined it on both for symmetry. MySQL has prefix indexes and a prefix key is
  exact there — the engine seeks the prefix and rechecks the full column — so the symmetry was mine, not the
  stores'. Symmetry across adapters is a hypothesis to test, not a design goal. (2026-08-21)
- **A spec that claims exactness has to be given values that could break it.** The first prefix-index test
  compared a 2000-character string against `"short"`; they differ at character zero, so it would have passed
  against a truncating index. Two values sharing 2000 characters and differing only past the key is the case
  the claim is about. Ask what input would make the assertion pass while the claim is false. (2026-08-21)
- **Reuse a refusal record only where its axes are real.** DATA-0119 says new refusals adopt `KoanMagic`, but
  that record carries a Risk, a Consent flag, and an environment boundary. A capability the store does not have
  is refused in Development too and no consent unlocks it, so adopting the shape would have meant filling two
  fields with fiction. What generalizes is the obligation — name the capability, the store, the remedy — not
  the type. (2026-08-21)

- **A guard that resolves nothing answers "absent" and runs every time.** SQL Server's new index guard asked
  `OBJECT_ID(N'dbo.Koan.Jobs.JobRecord')`, which it reads as a four-part identifier, resolves to nothing, and
  returns NULL — so `object_id = NULL` was never true, the guard never fired, and the second boot failed with
  "an index with that name already exists". Koan's default storage names are namespaced and therefore full of
  dots, so any identifier passed to a name-parsing function has to be bracketed first. A guard that fails open
  is invisible on the first run and only appears on the second — which is why it took a jobs suite, not a
  connector suite, to find it. (2026-08-21)
- **A wall-clock assertion measures the machine, not the code.** A jobs spec asserting a bulk save completes in
  under ten seconds failed cold at 14.6s and passed warm, at a commit with none of the work under test in it.
  It cost a full worktree bisect to clear. Where the claim is "one batched write", assert the count, not the
  clock. (2026-08-21)

- **Before adding a capability, ask what it does to the values it touches.** Building declared indexes was the
  obvious half of the work. The half that mattered was asking what an index does to a text property: SQL Server
  read a mapped string out of the document as `nvarchar(4000)`, indexed it happily, and then rejected the first
  insert whose key exceeded 1700 bytes. Shipping that would have traded "the index does nothing" for "the index
  breaks writes", which is strictly worse, and no existing spec covered it because every fixture writes short
  strings. A new capability's failure mode is rarely where the capability is; it is in what the capability now
  constrains. (2026-08-21)
- **Sharing a runtime is not evidence.** CockroachDB speaks the PostgreSQL wire and runs the same executor, and
  MongoDB and SQLite both build indexes from the same `[Index]` attribute — none of which says the planner on
  the other engine will choose what was built. Where a claim is about a store's behaviour, prove it against that
  store. The cheap form is one spec per engine asserting the index exists *and* appears in a plan. (2026-08-21)
- **An index whose expression differs by a character from the query's is dead weight.** Every store here only
  uses an expression or computed-column index when the read spells the value identically, so the index has to be
  built from the dialect's own `Read` rather than from a spelling invented at the index site. On Couchbase this
  forced the path grammar out of the generic document plan, because an index built for a container and a filter
  compiled for an entity have to agree and could not while the grammar lived behind a type parameter. (2026-08-21)
- **This repo builds every project into one shared path outside the working tree, so only one build may be in
  flight at a time — and a worktree build clobbers the main tree's binaries.** Two false regressions came from
  this in one session. First, load-bearing checks running in the foreground rebuilt the shared assemblies with a
  fix deliberately disabled while a background regression was testing them: nine SQLite failures that did not
  reproduce. Then a baseline worktree built at an older commit overwrote the same output, and the next
  `--no-build` run tested a mixture of two trees. Rules that follow: never run a build or test while another is
  in flight; after building any worktree, rebuild the main tree before trusting `--no-build`; and treat a
  cluster of unrelated failures in one suite as a build-provenance question before a code question. (2026-08-21)

- **A shared seam is only proven by adopting it, and adoption is where its lies surface.**
  `RelationalSchemaOrchestrator` was 746 lines, registered in DI, resolved by nobody, and looked complete.
  Moving four adapters onto it found: an entry point that compiled a *second* mapping by reflection, so any
  caller would have validated a table it neither reads nor writes; four of nine members that existed only as
  defaults feeding the others, one of which rendered a JSON path in a spelling no dialect uses; no way to
  express the persisted computed columns two adapters have always built; and a neutral nullability field no
  executor read, whose only effect was to invent drift on the one store that checks. None of that was visible
  from reading the seam. Write the first consumer before believing the abstraction. (2026-08-21)
- **When four implementations disagree, decide which kind of disagreement it is before unifying.** SQLite,
  PostgreSQL, SQL Server and MySQL spelled nullability three different ways, validated to four different
  depths, and built indexes on one store out of four. Only the second and third were *decisions* to move to
  one owner. Nullability is a store convention, and forcing one neutral answer would have been wrong for
  three of the four; column types cannot be compared by the framework at all, because a CLR type cannot see
  a character set and a store type cannot be mapped back. Move the decision; leave the vocabulary. (2026-08-21)
- **A validation that reports one severity for everything was written by someone who only had one case.**
  Whether a schema difference stops a boot is a per-column answer — identity and the structured document on
  any matching mode, a projected column never under Relaxed, everything under Strict — and four private
  validations had each answered it differently and partially. Findings that carry their own severity replaced
  four parallel string lists; the shape of the type is what made the rule statable at all. (2026-08-21)
- **A hardcoded health answer is not a health answer.** Three relational adapters returned
  `TableExists = true, State = "Healthy"` as literals after a readiness call, so the schema-validate
  instruction was structurally incapable of reporting ill health, and no test noticed because the value was
  never wrong in the cases anyone ran. (2026-08-21)
- **Before attributing a red test to your change, run it against an unmodified worktree.** A Jobs/PostgreSQL
  failure appeared in the middle of a large refactor and looked like fallout; it reproduced identically at
  `HEAD` with none of the changes present. `git worktree add --detach <short-path> HEAD` is the cheapest
  answer — keep the path short, because a scratchpad path can exceed Windows' filename limit mid-checkout
  and leave a half-written tree. (2026-08-21)
- **A test double that is more capable than production hides defects.** Canon's persistence double
  returned the object it was handed, so nothing could ever be lost in storage. Every Canon spec
  passed while the pillar's central promise — messy arrivals converge — was broken for every real
  adapter. A double must be honest about the property that makes storage storage: serialization.
  Making it so gave an existing spec teeth it had never had. (2026-08-20)
- **A seam that cannot represent the concept produces a lying implementation, however well built.** Sparse
  projection blanked fields because `TEntity` had no vocabulary for *absent*, so `default` had to mean it — and
  `0001-01-01` is not "absent", it is a date. Before asking who should own a decision, ask what the type can
  *say*: a seam missing a word will be filled with a plausible wrong one. The relational schema seam needed two
  additions for exactly this reason before any adapter could adopt it without losing validation. (2026-08-20)
- **Assert the decision, not a sample of its effects.** A spec that paged an unsorted corpus and checked the
  pages partitioned it passed with the guarantee removed — five rows come back from a small table in physical
  order regardless. Where the change is "this decision now has one owner", assert that the owner made it; an
  end-to-end sample can agree by luck and then be kept as false comfort. Check a new spec against a *disabled*
  fix before trusting it. (2026-08-20)
- **A capability question belongs to the provider, not to a static list.** Streaming refused an order key by
  its CLR type before any adapter was asked, which held every provider to what the weakest one manages — and
  the remedy it offered, "materialize the query", was the one thing streaming exists to avoid. The per-provider
  check already existed a few lines below. Prefer: attempt, let the provider decline, and name it in the
  refusal. Where behaviour merely *varies* by backend rather than being unavailable, explain it in the facts
  instead of forbidding it. (2026-08-20)
- **A spec that only compares answers cannot see whether the store did the work.** Every adapter ordered
  `-Sightings.LastChangedAt` correctly while none of them pushed it down: the framework's sorter finished
  the job over the whole materialized result. The surface suites were green throughout. Where pushdown
  matters, assert the adapter's receipt (`SortHandled`, `PaginationHandled`) alongside the ordering — and
  read a receipt before believing a claim about which layer answered. (2026-08-20)
- **A cross-adapter ordering corpus must be a total order.** Ties pin LINQ's stable sort against databases
  that promise nothing among equals, and fail on whichever store breaks them differently — a failure that
  is not a defect. (2026-08-20)
- **A suite that shells out must inherit its own build configuration.** A sample spec pinned
  `-c Release --no-build` while its build produced Debug, so it passed only on a machine where
  someone had previously built that sample by hand. (2026-08-20)
- **Readiness that cannot become green is worse than red.** A probe that reports unhealthy until
  something provisions, in a system where nothing provisions until traffic arrives, deadlocks under
  any orchestrator. Distinguish *not yet written to* from *broken*. (2026-08-20, ARCH-0128 wave)
- **Certification is deliberately manual.** `pr-gate` builds and projects but runs no tests, so red
  suites accumulate unseen between explicit ratchet runs. Run `scripts/green-ratchet.ps1` at a real
  boundary and read `artifacts/ratchet/test-manifest.json`, rather than trusting a green PR.
- **Sample suites are the only proof of the real process.** In-process specs cannot see stdout
  ownership, shutdown, discovery health, or ambient flow across an async hop. Every framework-level
  defect found by running a sample end-to-end was invisible to the in-memory suites.
- **A deferred entry records the tree on the day it was written.** Verify the premise before working
  one; the register's own contract says why.
