---
id: DX-0049
title: Three-skill public agent surface
status: Accepted
date: 2026-08-15
area: DX / AX
supersedes: [DX-0048]
related: [ARCH-0105, ARCH-0118, ARCH-0121, ARCH-0123]
---

# DX-0049 — Three-skill public agent surface

## Outcome

Koan exposes exactly three public coding skills, named for durable user intent:

| Skill | User intent | Trust boundary |
|---|---|---|
| `koan` | Build, extend, fix, test, research, or ship a Koan application | May inspect and make user-scoped changes, then verify the outcome |
| `koan-explain` | Explain, review, or orient without changing anything | Strictly read-only |
| `koan-upgrade` | Move an older Koan application to current Koan | May perform an explicitly requested framework migration while preserving application contracts |

Build, extend, fix, test, research, and ship are internal recipes behind `koan`, not additional public
skills. Explanation stays separate because read-only is a promise. Upgrade stays separate because old
code is evidence about what must be preserved, never a template for a new application.

## Context

Capability-shaped skills made developers learn the framework's filing system before they could ask
for an outcome. They also competed for activation and repeated API guidance. A request such as “use
Mongo, add authentication, and expose the same operations to an agent” is one application journey,
not three unrelated skill invocations.

Koan's most distinctive experience is composability: a developer describes a useful application,
combines a few semantic pieces, and retains an obvious path to add the next capability. The public
skill surface should amplify that experience rather than narrate framework development history.

## Decision

### One delightful front door

`koan` begins with the developer's business sentence and proposes the smallest coherent stack. Its
preview uses three concepts:

- **Now** — pieces required by the requested outcome;
- **Later** — natural additions that remain easy to compose;
- **Preserved** — contracts the work will not disturb.

The skill then builds one working vertical slice. It asks at most one question, only when the answer
changes data, security, a public contract, or deployment topology.

The common path speaks Koan's application language: reference means intent, one `AddKoan()` composes
the referenced pieces, `Entity<T>` owns recognizable data operations, and `EntityController<T>` adds
the conventional HTTP surface. Provider, source, and context switches remain explicit at their
boundary without leaking infrastructure ceremony into domain code.

The resulting answer must feel specifically Koan, not like generic .NET advice with a Koan package
name attached. It should reveal how the same small pieces reach persistence, Web, identity and
tenancy, Jobs, communication, cache, storage, media, AI, vectors, MCP, Canon, testing, and operations.
It also knows the available database adapters, but recommends only those earned by the application.

### Progressive disclosure

The surface has three layers:

1. **Catalog metadata** selects one of the three user-facing skills.
2. **Selected skill** supplies the durable workflow and trust boundary.
3. **Task recipe** supplies only the capability knowledge needed for the current outcome.

One fact has one owner. Loaded skills teach choices and procedure; they do not reproduce release
ledgers, package inventories, development chronology, internal validation commands, or architecture
project management. Compatibility evidence may exist for maintainers, but it is not application
guidance and is not loaded into a developer's task.

Capability and provider rows link to canonical online Koan recipes. The link target may carry the
published source revision needed for correctness; the surrounding skill prose remains timeless.

### Current, greenfield guidance

Ordinary application work is greenfield in posture even when it extends an existing application.
The skill uses the current stable public Koan contract and describes the smallest present-day
expression. It does not make a developer reason about how that expression evolved.

When an exact external standard, vendor API, or security practice matters, `koan` researches the
current primary source and cites the result. Research is earned by the task; it is not a ritual for
ordinary Koan composition.

### Stable routing and trust

Selection follows these rules:

1. An explicit skill invocation wins.
2. Work that must remain read-only selects `koan-explain`.
3. A Koan framework migration selects `koan-upgrade`.
4. Other Koan implementation work selects `koan`.

A database, cache, storage, AI, or vector-provider change is application evolution and remains with
`koan`. It is not a framework upgrade.

Skill selection never expands authorization. Destructive operations, credentials, publishing,
external messages, and production changes retain their normal approval requirements.

If a request starts with explanation and continues into repair, `koan-explain` completes the
read-only explanation and explicitly hands off to `koan` before mutation.

### Upgrade without contamination

`koan-upgrade` inventories observable source behavior, public contracts, data boundaries, and
operational assumptions. It maps each obsolete framework expression to a verified current
expression, proves the preserved behavior, and keeps rollback possible.

Old documentation and copied skills may help identify what the source application expected. They do
not define the target. Unknown replacements stop for evidence rather than being guessed or blended
into ordinary guidance.

### Lifecycle and deprecation

The former capability-specific names and copies derived from them are superseded by the three public
skills. Loader entrypoints are removed rather than retained as executable aliases. Git history, the
lifecycle manifest, and the external-listing ledger preserve the inventory and successor mapping.

Third-party listings should be delisted or replaced with a link to the official three-skill set. A
listing is not treated as removed until its owner or platform confirms the change.

### Evaluation

Acceptance centers on developer and agent outcomes:

- the correct skill activates from natural language;
- `koan` produces a small, distinctly Koan stack and a clear growth path;
- capability additions compose with existing operations instead of creating parallel architecture;
- the result proves behavior, selected composition, and a useful failure;
- `koan-explain` remains read-only;
- provider cutovers do not leak into `koan-upgrade`;
- migration preserves named contracts and stops on unknown replacements; and
- release or development-process narration does not crowd the loaded skill context.

Static structure checks protect the exact roster, metadata, links, recipe reachability, semantic
breadth, and absence of legacy loader entrypoints. Forward evaluation must still judge real model
responses; a corpus validator alone does not prove delight.

## Consequences

- Developers can remember the entire public surface.
- A fresh developer can move from a business sentence to a useful vertical slice without learning
  package taxonomy first.
- One application can grow through small pieces while keeping its domain language and operations.
- Read-only and migration work retain explicit, auditable boundaries.
- Maintainers must keep recipes concise and evaluate task success, not reward added context by volume.
- Exact compatibility and retirement records remain maintenance artifacts rather than loaded
  application instructions.

## Amendment — 2026-08-16

The three-skill surface, routing, trust boundaries, and progressive disclosure stand unchanged. The
validation machinery around them does not.

Every gate this decision called for measured **absence** — whether a word appeared, whether a
manifest matched the filesystem, whether an immutable tag was still immutable. None could answer
whether the guidance was true, and all of them went green over a skill that named no package
identifiers, omitted four shipped capabilities, and recommended a shelved connector as a peer of
assessed ones.

Retired: the lifecycle manifest and its schema, the corpus validator, the packaging step (whose
output nothing consumed), and the semantic-breadth word checks. `evals/koan/rubric.md` and
`cases.jsonl` remain as documents to run, not artifacts to validate.

Retained, in one script: routing metadata, link resolution, capability-shelf parity against the
shipped product, and — new — verification that every package identifier restores and every taught
construct compiles against the **published** packages. That last check found two real defects on its
first execution, including a canonical bootstrap snippet that did not compile.

The principle: a gate that has never failed for a good reason is not protecting anything. Prefer one
oracle that can be wrong to many checks that cannot.

The lifecycle manifest named above is retired; git history preserves the legacy inventory and
successor mapping. The delisting obligation in "Lifecycle and deprecation" is unaffected and its
record is [legacy-skill-listings.json](../reference/agents/legacy-skill-listings.json) — around forty
verified third-party listings across eight platforms remain to be delisted or redirected, and none is
treated as removed until its owner confirms.

Product maturity is unchanged by this amendment. The claim ledger, the maturity vocabulary, and the
release train remain exactly as the product defines them; the skill surface reads them and reports
what they say. Where a package carries no claim, the capability shelf marks it *not assessed* rather
than presenting it as an ordinary choice — the shelf reflects product truth, it does not set it.

## Alternatives considered

- **Keep one public skill per capability.** Rejected because package taxonomy is not user intent and
  overlapping activations worsen as Koan grows.
- **Publish one skill per internal recipe.** Rejected because internal organization should be free to
  evolve without changing the public vocabulary.
- **Publish one monolithic skill.** Rejected because read-only explanation and framework migration
  require materially different trust boundaries.
- **Publish documentation only.** Rejected because an invocable, evaluated workflow can turn the
  documentation into a coherent application outcome.

## Prior art and evidence

- [OpenAI, “Build skills”](https://learn.chatgpt.com/docs/build-skills) describes focused repository
  skills and progressive disclosure.
- [Agent Skills specification](https://agentskills.io/specification) defines the portable directory
  and metadata contract used for the canonical source.
- [Laravel Boost](https://laravel.com/docs/13.x/boost) separates always-loaded guidance from
  task-specific skills and retrieves relevant framework knowledge on demand.
- [Supabase Agent Skills](https://supabase.com/blog/supabase-agent-skills) demonstrates a small public
  surface with security invariants kept close to action.
- [SWE-Skills-Bench, arXiv:2603.15401](https://arxiv.org/abs/2603.15401) motivates paired evaluation:
  additional skill context is valuable only when it improves task success.
