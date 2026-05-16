# ARCH-0083 — Operational workbooks: structure and conventions

**Status**: Accepted
**Date**: 2026-05-16
**Deciders**: Enterprise Architect
**Scope**: Documentation taxonomy across the repository

---

## Context

After ARCH-0082 landed the two-tier versioning system, an operational guide grew up under [docs/guides/versioning-workbook.md](../guides/versioning-workbook.md). It was useful but conflated three audiences:

- **First-time learners** (want to understand the concepts)
- **Operators mid-task** (want exact commands)
- **Future maintainers** (want to know *why* the system is shaped this way)

Conflating those audiences in one doc means each gets a degraded experience. ADRs already handle the third audience. Tutorials/guides serve the first. The middle audience — operators with a goal, half-asleep at 2am, or LLMs assisting them — needed its own format.

## Decision

Introduce **workbooks** as a first-class documentation category, with a standard shape and a dedicated home at `docs/workbooks/`.

### What a workbook is

A workbook is a **task-oriented runbook**. The reader has a goal ("I want to release a hotfix," "I want to add a package to the kernel," "the workflow failed and I need to fix it"). The workbook gives them the exact commands, in order, with brief explanations of what each does — no rationale digression, no conceptual teaching.

The voice is imperative. The reader is not learning; they're executing.

### How workbooks differ from neighbors

| Doc type | Lives in | Answers | When read |
|---|---|---|---|
| ADR | `docs/decisions/` | "Why did we choose this?" | Onboarding, design review, justifying a future change |
| Guide / tutorial | `docs/guides/` | "How do I learn this concept?" | First time encountering the topic |
| Reference | `docs/reference/` | "What's the exact API surface?" | Looking up a specific symbol |
| **Workbook** | **`docs/workbooks/`** | **"What do I run, step by step, right now?"** | **Mid-task, or recovering from failure** |

Overlap between categories is allowed when it serves the reader. The workbook for "publishing to NuGet" may reference the versioning workbook and the ARCH-0082 ADR — but it won't repeat their content.

### Required structure

Every workbook MUST contain these four sections, in this order:

1. **Front matter** — When to use this, scope, prerequisites
2. **Mental model (30 seconds)** — The framing the rest depends on
3. **Happy path** — The one canonical command-by-command recipe
4. **Failure → recovery** — Exact commands for known failure modes (not "investigate and fix")

Every workbook SHOULD contain these when they apply:

5. **Scenarios table** — `if X, look here` lookup
6. **Anti-patterns** — What NOT to do, with the lesson behind each
7. **References** — Links to ADRs, scripts, related workbooks

### What workbooks must NOT do

- Explain rationale at length (that's the ADR's job — cross-link instead)
- Teach concepts from first principles (that's the guide's job)
- Be exhaustive about every option (that's the reference's job)
- Use "investigate", "consider", "you might want to" — workbooks give commands, not invitations to think

### Naming and location

- Files live at `docs/workbooks/<topic>.md`, lowercase, hyphenated
- Filenames are nouns or noun phrases describing the topic, not verbs:
  - ✅ `versioning.md`, `nuget-publishing.md`, `adding-a-pillar.md`
  - ❌ `how-to-version.md`, `release-process.md`
- The workbook's title (H1) restates the topic in plain English

### Scaffolding

- `docs/workbooks/README.md` — defines the standard (refers here), lists active workbooks
- `docs/workbooks/_template.md` — empty workbook starting point with all required + recommended sections

### Cross-references from code

Operational scripts (`scripts/`, GitHub Actions workflows) referencing operational concerns should link to the relevant workbook in their header comment. Example:

```yaml
# Required reading before re-running this workflow manually:
#   docs/workbooks/nuget-publishing.md (Failure → recovery section)
```

This is what makes the workbook discoverable by future operators (human or LLM).

## Consequences

### Positive

- **Clear separation of audiences.** A workbook reader gets commands; an ADR reader gets reasoning; a guide reader gets concepts. Each is shorter and clearer for the audience it serves.
- **Discoverability.** `docs/workbooks/` is one place. The README indexes everything. Workbooks are linked from the scripts/workflows they describe.
- **Failure recovery becomes a first-class concern.** The "Failure → recovery" section is required. Operators don't have to invent the recovery from scratch — past lessons land here.
- **LLM-friendly.** Future Claude/Cursor sessions can be pointed at a single workbook for any operational task and execute reliably.

### Negative / acceptable trade-offs

- **One more place to look.** Mitigated by the index in `docs/workbooks/README.md` and cross-references from scripts.
- **Drift risk.** A workbook claiming "run command X" while the script's actual flag set has changed is worse than no workbook. Mitigation: workbooks are reviewed alongside the script changes that affect them.

## Non-goals

- Migrating every existing guide to the workbook format. Only docs whose primary use case is "operator mid-task" should become workbooks.
- Enforcing a rigid template via tooling. The standard is a convention; reviewers enforce it during PR review.

## Initial workbooks

These ship with this ADR:

- **`docs/workbooks/versioning.md`** — operating the ARCH-0082 versioning system day-to-day
- **`docs/workbooks/nuget-publishing.md`** — operating the release-on-main publish flow

The existing `docs/guides/versioning-workbook.md` becomes a redirect stub pointing at the new location.

## Notes for reviewers

- The fact that the existing workbook was named "versioning-workbook" and lived under `guides/` was the original tell that this category needed its own home. ARCH-0083 just makes that home official.
- Future doc categories (e.g., security advisories, performance tuning playbooks) can follow the same precedent — define a category, set the structure in an ADR, index in a README.
