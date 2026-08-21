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
