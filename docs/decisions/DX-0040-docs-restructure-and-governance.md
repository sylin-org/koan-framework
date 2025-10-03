# DX-0040: Documentation restructure, tone reset, and governance

Status: Accepted

## Context

The documentation set split between `/docs` and `/documentation` has drifted. Legacy guides carry inconsistent tone, metadata gaps, and overlapping coverage. Proposals live outside the ADR system, DocMind case study material exists only as agent chunks, and agents struggle to ingest monolithic files. Prior conversations ratified a holistic clean-up that must:

- Collapse all canonical documentation into `/docs/` with predictable navigation.
- Normalize contract + edge-case scaffolding, tone, and metadata.
- Archive or merge ad-hoc proposals into long-term records.
- Rehome DocMind material as a concise case study while keeping deep references available.
- Chunk oversized references so agents can reason over them.
- Keep lint/manual validation triggered explicitly instead of automated pre-commit hooks.

## Decision

1. **Canonical location** – `/docs/` becomes the single source for product documentation. Legacy `/documentation/**` content is migrated, rewritten, or archived under `/docs/archive/**`. Remaining files under `/documentation` are removed once migrated.
2. **Standard framing** – Every instructional doc starts with a "Contract" block (inputs/outputs, error modes, success criteria) plus an "Edge cases" callout. Tone stays instructive, concise, and first-principles aligned.
3. **DocMind consolidation** – Replace the raw S13 DocMind chunk set with a curated case study in `/docs/case-studies/s13-docmind/`. Retain the original research packs under `/docs/archive/chunks/` for provenance.
4. **Proposal realignment** – Collapse freeform proposals into ADRs or archival summaries. Proposals that already informed ADRs link forward; open ideas move to `/docs/archive/proposals/` with current status notes.
5. **Chunking and agent support** – Break any file over ~800 lines or multi-topic references into targeted leaf docs. Generated or tooling-specific chunks live only in `/docs/archive/chunks/`.
6. **Validation posture** – `scripts/build-docs.ps1` remains the authoritative check. No automatic lint gate is added; contributors run it manually after batches of edits.

## Consequences

- Contributors have a single entry point with harmonized scaffolding and navigation.
- Obsolete `/documentation/**` paths disappear, reducing duplication and broken links.
- DocMind insights surface as a digestible case study while preserving research detail for deep dives.
- Proposals gain traceability inside the ADR catalog or the archive directory with explicit status.
- Agents and chunk-based tooling can target smaller files, improving response quality.
- Manual doc builds remain required before merging, keeping verification explicit.

## Follow-ups

- Execute migrations: move/merge docs, update `docs/toc.yml`, and delete vacated legacy files.
- Produce curated DocMind case study and archive residual research artifacts.
- Review each proposal file, either fold into an ADR or annotate in the archive with disposition.
- Sweep the repository for stale links pointing at `/documentation/**` and update them.
- Re-run the strict doc build and fix any warnings introduced by the move.
