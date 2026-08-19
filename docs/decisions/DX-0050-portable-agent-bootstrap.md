---
id: DX-0050
title: Portable agent bootstrap
status: Accepted
date: 2026-08-19
area: DX / AX
related: [DX-0037, DX-0049, ARCH-0118]
---

# DX-0050 — Portable agent bootstrap

## Outcome

Every scaffolded Koan application and every graduated sample carries one portable, harness-neutral
`AGENTS.md`. It routes an agent to authority; it never restates authority.

Three properties define it, and each is mechanically enforced:

| Property | Meaning | Enforcement |
|---|---|---|
| Capability-agnostic | The file names no capability and no package identifier | `skills-verify` rejects any `Sylin.Koan.*` identifier in an `AGENTS.md` |
| Subordinate | Where a harness supports the Koan skills, the skills win | The file says so in its own text |
| Bounded | Every outward link resolves, under the rule its kind requires | `skills-verify` resolves pinned links at the tag and the capability map against the tree |

The file answers "how do I add something this application does not have yet?" with a link to the
capability map, not with a list. Adding a capability to Koan therefore edits the map and no bootstrap.

## Context

Koan's coding skills are real and verified, and they reach exactly one harness. `dotnet new koan-web`
writes `.claude/settings.json`, which registers the marketplace and enables the `koan` plugin
(DX-0049). A developer who scaffolds a Koan application and opens any other agent gets nothing: no
grammar, no evidence pointers, no way to reach the capability shelf that carries exact package
identifiers.

The gap is sharpest on the question that decides whether the framework feels agentic at all — *"now I
want to add AI to this solution; how should I do it?"* Answering it requires three facts a general
model does not have: that a reference is the intent and registration must not be written, that
package identifiers are exact and not derivable from product names, and where the owning page lives.
Without those, an agent writes plausible ASP.NET Core ceremony that Koan explicitly rejects.

Graduated samples make the inconsistency concrete. The `koan` skill's stack cards route a developer
*to a sample* as the working version of each composition, yet no sample carried any agent affordance.
Cloning the artifact the skill calls canonical produced a worse experience than scaffolding.

Meanwhile the framework already emits more authoritative self-description than any bootstrap could
carry: `koan.lock.json` at build time, startup election reporting, `/.well-known/Koan/facts` and
`koan://facts` at runtime. The skill's own orientation script inferred composition by pattern-matching
`.cs` files while that evidence sat unread.

## Decision

### The bootstrap is a router, not a catalog

`AGENTS.md` contains no capability name, no package identifier, and no inventory. Any of those would
create a second catalog that goes stale whenever a package ships, contradicting single ownership of a
fact. The complete capability-to-package authority remains the generated product surface, reached
through the retrieval map; the capability shelf remains the skill's own progressive-disclosure layer.

A structure check enforces the property rather than relying on review discipline. A bootstrap that
names a package fails the gate.

### It points at the application's own evidence first

Generic framework guidance is the fallback, not the opening move. The bootstrap's first substantive
section names what *this* application composes — the build-time lockfile, startup reporting, the facts
and health endpoints — so an agent reaches ground truth in one hop instead of inferring composition
from source.

This is the ordering that matters: the portable file is an on-ramp whose job is to hand the agent off
to Koan's own self-description as quickly as possible.

### Two rules carry most of the value

The bootstrap states the reference-is-intent law and the exact-identifier law. Together they convert
an open-ended request into a bounded one: add the reference, do not write registration, and copy the
identifier from the map rather than constructing it.

### It defers to the skills

Where a harness supports them, `koan`, `koan-explain`, and `koan-upgrade` remain the richer surface
and the bootstrap says so explicitly. Without that sentence the surfaces compete, and an agent holding
both resolves the conflict by recency. The bootstrap is the portable floor beneath DX-0049, not a
rival to it.

### The capability map is public, and is the primary route

The capability map moves from `.agents/skills/koan/references/capabilities.md` to
[docs/reference/capability-map.md](../reference/capability-map.md). It already stated every capability
as an outcome with the exact package and a recipe; filing it as plugin internals meant only one
harness could reach the one document that answers "add AI to this." The skill now references it
instead of owning it — the same single-owner rule, one less indirection.

The resulting flow is two fetches and no local inspection: match the stated need to a row, open that
row's recipe, work from it. Subtracting what an application already composes is worth doing only for
the genuinely open "what *could* I add?", never for a stated need.

### A vague ask routes to prose, not to a table

A lookup table is the wrong first move for "I want to add AI." That sentence could mean semantic
search, question answering over documents, vision, an agent that acts, or human review of model
output — five different projects, with different runtimes and different costs. Matching it to a row
and naming a package picks one of five and is usually wrong.

So the map routes by the *shape* of the request. A named piece ("add Mongo") goes straight to its row.
A vague outcome routes away from the table entirely.

**Amended by DX-0051.** This decision first sent a vague outcome to a hand-written scenario page.
That was wrong for reasons that generalize: the page duplicated what an index could carry, could not
observe the application in front of it, and would never be written for combinations. DX-0051 replaces
it with a **generated recipe index** the agent reasons over directly. The routing rule here stands;
only its destination changed.

Publishing mechanics are the last mile of a recipe, never its organizing principle. A capability
described only as an identifier plus a companion list still leaves the developer to guess which of
five things they are buying.

### A row is an outcome, and the relation is many-to-many

Keying the map by package would be a lie in both directions. One outcome routinely needs several
packages — authentication is inert without a provider, media needs somewhere to put bytes, vector
search needs an index — and one package routinely serves several outcomes, so the same identifier
appears in more than one row.

A three-column map hid the first half of that, and hid it in the way that hurts most: an agent matched
an outcome, installed the one named package, and produced a capability that composes and does nothing.
Every capability table therefore carries an **Also needs** column, and `—` is a positive statement
that the row stands alone rather than an absence of information.

The column records only verified requirements. Cache, for example, ships a built-in process-memory
floor, so its adapters are upgrades and its cell is `—`; asserting a requirement there would be worse
than silence.

### Two outward links, pinned differently, and the difference is the point

A recipe describes how a specific version behaves, so recipe links stay pinned to the release tag and
are verified to resolve there. The capability map is the opposite kind of thing: a frozen map hides
every capability shipped since that tag, which is precisely what the question needs to see. It is a
channel, like the plugin marketplace, and it tracks the release branch.

A channel link therefore cannot be verified against an immutable revision. It is verified against the
local tree instead — the map must exist here and must ship — and pinning it is a failure, not a
virtue. `llms.txt` remains as the broader documentation index, pinned, and continues to own the
resolution rule for its own repository-relative paths.

### Scaffolding announces what it produced

A file nobody is told about is a file nobody reads. Both templates display instructions after
creation naming the run command, the skills, and the bootstrap. The post-action is the only text a
developer is guaranteed to see.

### Samples point; they do not copy

Graduated samples receive a pointer line in the README each already has, not a copied bootstrap. Ten
copies would be ten things to rot, and samples inside this repository already inherit the root
`AGENTS.md`. The pointer exists for the case that motivates it: a sample lifted out of the repository,
where inheritance is lost.

The root `AGENTS.md` is the portable entry point for the repository itself. It does not restate the
contributor law that `CLAUDE.md` owns; it points at it, harness-neutrally.

### The gate owns the invariant

`skills-verify` gains a fifth check, `bootstrap`, in the per-PR structure run: the root and both
templates carry a bootstrap, every graduated sample points at one, every link resolves, pinned links
resolve at the tag, and no bootstrap names a package. A delight pass that is not gated has a
six-month half-life.

## Consequences

- A Koan application is legible to any agent, not only to one vendor's.
- "Add AI to this solution" becomes actionable without the plugin, in any harness.
- Shipping a capability edits no bootstrap; the retrieval map and product surface absorb it.
- Samples and scaffolds converge on one agent experience.
- Maintainers gain one more structure check, and lose the option of writing a convenient package name
  into a bootstrap.
- The bootstrap can only be as good as the map it points at; drift in the capability map now degrades
  every harness at once, which is the correct place to concentrate that risk.
- Promoting the map exposed a blind spot in the existing shelf-parity check: it requires a row only for
  packages that carry a product claim, so five shipped, documented, developer-facing AI capabilities —
  retrieval-augmented chains, Entity-tooled agents, model lifecycle, human review, and evaluation —
  were absent from the one document that answers "add AI to this." They are listed now, each marked
  *not assessed*. Whether *unassessed but shipped* should be **owed** a row remains a product-policy
  question this decision does not settle: absent a rule, "not assessed" silently means "undiscoverable."

## Alternatives considered

- **List capabilities in `AGENTS.md`.** Rejected: a second inventory that rots on every release and
  duplicates the generated product surface.
- **Copy the skills into the scaffold.** Rejected for the reason DX-0049 already gives — copied
  guidance freezes while the framework moves. Reference, not copy, is the same law the csproj obeys.
- **Emit per-harness configuration for each vendor.** Rejected: an unbounded matrix that ages badly.
  `AGENTS.md` is the convergent convention; `.claude/settings.json` remains as the one harness where a
  richer surface actually exists.
- **Copy `AGENTS.md` into every sample.** Rejected: ten owners for one fact.
- **Document the bootstrap without gating it.** Rejected: every prior ungated skill invariant in this
  repository drifted, which is the finding the DX-0049 amendment already records.

## Prior art and evidence

- [AGENTS.md](https://agents.md) — the convergent, harness-neutral instruction file adopted across
  coding agents.
- [llms.txt](https://llmstxt.org) — the retrieval-map convention Koan's root file already follows.
- [Agent Skills specification](https://agentskills.io/specification) — the portable skill contract
  DX-0049 builds on, and which the bootstrap defers to where supported.
