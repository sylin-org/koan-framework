# Handoff prompt — `koan-create-adapter` skill, proven by building four researched adapters

Copy everything below the line into a fresh agent session.

---

You are working in the Koan framework repository at `F:\Replica\NAS\Files\repo\github\sylin-org\koan-framework` (branch `dev`). Koan is an opinionated .NET 10 meta-framework: **a package reference is the intent**; pillar capabilities own semantic policy and runtime chokepoints; adapters own provider realization. Koan's stated ambition (the Adapter Forge brief) is that *the framework extends itself through agents* — this task makes that real.

## Mission, in two sentences

Create the portable skill **`koan-create-adapter`** — an agent-executable workflow for authoring conformant Koan adapters of any family — and **prove it by actually authoring four new adapters with it** (one each: relational, document, vector, AI runtime), each implementing and passing its family's behavioral test-suite oracle. The adapters must be preserved, building, and tested in the tree at the end; whether they are later promoted into the official connector fleet with product claims is a maintainer decision and is explicitly *not* your concern.

## Read first (non-negotiable, in order)

1. `AGENTS.md`, then `CLAUDE.md` (contributor law — especially *Module authoring*, and the Core/Pillar/Adapter ownership split).
2. `docs/MEMORY.md` (working conventions and hard-won lessons).
3. `docs/architecture/data-adapter-development-primer.md` — status **current**; the blueprint your data seam is distilled from. Note its route table ("Build a new adapter: §1 → §4 → Steps 1–4 in §5 → §§6–9 → Steps 5–10 in §5"), its acceptance catalog, and its conformance obligations ("Must/required" = conformance; earned obligations per capability).
4. `docs/architecture/adapter-forge.md` — **draft**; the strategic thesis only. Do not cite its CLI vision (`koan adapter new`) as existing.
5. `docs/decisions/ARCH-0127-connector-fleet-strategy.md` (how a connector enters; obligations) and ARCH-0120 (maturity/claims — **merging grants nothing**; a new adapter ships as **not assessed** with truthful claims).
6. `docs/reference/connector-matrix.md` — the covered set you must not duplicate (generated file; read it, don't edit it by hand).
7. `.agents/skills/` — `koan`, `koan-explain`, `koan-upgrade` — and `docs/guides/agent-skills.md`: the structure, frontmatter, and portability conventions your skill must match.

## Phase 1 — the skill

Create `.agents/skills/koan-create-adapter/` following the existing skills' structure and `docs/guides/agent-skills.md` conventions exactly.

- `SKILL.md`: the entry workflow — pick the seam → research the target → place the package per module-authoring law → implement → **pass the family's conformance oracle** → obligations sweep (capability-map entry, recipe ingredients, truthful maturity) → report. Keep the description tight enough for reliable trigger.
- `references/data.md`: the data-adapter playbook, distilled from the primer — the authoring sequence as concrete steps, the acceptance catalog as mandatory checkpoints, and the failure modes (corrective rejection of unsupported paths, mixed-space guard, AOT constraints).
- Design for additive seams: `references/` gains one playbook per family as they are proven (Phase 3). The skill must say plainly which seams have playbooks and which fall back to "derive from an assessed exemplar connector in that family" (Phase 3 does exactly that).
- The gates are the skill's core value: behavioral conformance, truthful maturity claims, docs obligations, NativeAOT-clean (`docs/guides/nativeaot-howto.md` — no `dynamic`, no IL emit, Newtonsoft only), and the standing **repo-wide `MakeGenericType` sweep** on any changed generic contract. Encode them as hard, non-skippable steps.

## Phase 2 — dogfood: relational

Do not skip to generic research — Phase 2 validates the skill on the seam that has a current blueprint.

1. **Research and pick** a relational database that is (a) free/open-source, (b) **absent from the connector matrix**, (c) drivable from .NET (existing ADO.NET provider or sane wire protocol), (d) runnable for tests (local process or container — the repo has `src/Koan.Testing.Containers`), and (e) preferably AOT-viable (P/Invoke or managed protocol, no IL-emitting client). Candidates to evaluate (non-binding): Firebird, libSQL, H2-style embeddables — record your selection rationale and why the others lost.
2. **Author it through the skill you just wrote** — follow `references/data.md` step by step as if you were an outside agent; every place the playbook is ambiguous or wrong, fix the playbook. This dogfooding loop is the point of the phase.
3. **The oracle**: locate the shared Data-family behavioral conformance suite that existing relational connectors pass (the same specs that gate SQLite/Postgres — search the connector projects and `tests/` for the shared AdapterSurface/conformance specs). Your adapter must fulfill that oracle: full pass, or an explicit, reasoned skip list per spec (some providers genuinely cannot lower a filter — the primer defines correctives; "skip because hard" is not a reason).
4. Place per module-authoring law (`src/Connectors/Data/<Name>` pattern), claim **not assessed**, and complete the obligations sweep.

## Phase 3 — seam expansion: document, vector, AI

For each remaining family, in this order:

1. **Derive the seam playbook** the honest way: read the primer's principles + one *assessed* exemplar connector in that family (e.g., Mongo for document; SqliteVec or Qdrant for vector; Ollama or ONNX for AI) and write `references/<seam>.md` — the family's contracts, chokepoints, conformance oracle location, and family-specific failure modes.
2. **Research and pick** a free, not-yet-covered target by the Phase 2 criteria. Candidates to evaluate (non-binding): document → CouchDB or RavenDB Community; vector → Chroma or LanceDB; AI runtime → llama.cpp (`llama-server`). Note the matrix already covers Couchbase (≠ CouchDB) and PgVector/Qdrant/Weaviate/Milvus/Redis — pick genuinely new ground.
3. **Implement and test through the skill**, fulfilling that family's oracle with the same pass-or-reasoned-skip discipline. Where a family's shared conformance suite is thinner than Data's, say so in the report and construct the strongest available behavioral proof (the recipes' "Prove it" patterns are the floor, not the ceiling).

## Rules of engagement

- Work in **phases with everything green at each phase boundary**; if the session must end early, the tree keeps every completed phase, building and tested, plus a notes file describing exactly where the next session resumes. Nothing half-merged: preserve implementations.
- Stage **exactly your own files** when committing; the tree carries unrelated in-flight files (`continuation.md`, `agent-prompts/`, lock-file churn, `samples/recipes/`) that you must not touch. Re-check `git status` immediately before staging.
- Every adapter ships **not assessed**; no product claim anywhere. Updating `docs/reference/capability-map.md` / connector obligations entries with truthful not-assessed rows is in scope; editing the generated `connector-matrix.md` by hand is not (note it for regeneration instead).
- NativeAOT-clean throughout; `MakeGenericType` sweep on changed contracts; Newtonsoft is the canonical serializer.
- Do not modify the existing skills (`koan`, `koan-explain`, `koan-upgrade`) — new files only.

## Prove it / report

- Skill: matches `docs/guides/agent-skills.md` conventions; discoverable alongside the family; add a pointer row per `docs/MEMORY.md` conventions.
- Each adapter: family oracle result (pass counts, skip list with reasons), solution build 0 errors, its own test project green, AOT publish attempted or explicitly reported as blocked (with the blocker — native deps are per-RID claims per the AOT guide).
- Report: phase-by-phase — what shipped, oracle numbers, playbook fixes discovered by dogfooding, selection rationale for each target, and any deviation from this brief with the reason.
