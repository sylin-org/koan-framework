# Contributor workflow (short)

This page describes how to make changes confidently and keep docs authoritative.

## 1) Before you code
- Read Engineering front door: engineering/index.md
- Skim Architecture Principles: architecture/principles.md
- Confirm data/web mandates:
  - Data: model statics; All/Query fully materialize; use streaming or explicit paging for large sets.
  - Web: attribute-routed controllers only; no inline MapGet/MapPost.

## 2) Implement
- Small PRs; focused scope.
- Lifetimes: Singleton for clients/factories; Transient for stateless helpers; Scoped only when required.
- Centralize constants in a per-assembly `Constants` class; use typed Options for tunables.
- Add XML docs or short markdown snippets for public APIs where helpful.

## 3) Tests and docs
- Prefer runnable samples and focused unit tests.
- If behavior changes or new policy is introduced, update docs:
  - Guides (how-to)
  - Architecture Principles (if high-signal policy)
  - Decisions (ADR) if a new decision is made or an old one is superseded

ADR notes
- File name format: PREFIX-####-short-title.md (e.g., DATA-0061-...)
- Put it in `docs/decisions/`; add a bullet to `docs/decisions/index.md`.
- If superseding, state: “Supersedes <ID>” near the top.

## 4) Build locally
- Build code and tests via VS Code task: build (dotnet)
- Docs site generation has been removed from this repository.

## 5) PR Checklist
- [ ] Routes are in controllers only
- [ ] No empty classes or commented scaffolds
- [ ] Constants centralized; Options validated on start
- [ ] Data access uses model statics; large sets use streaming or explicit paging
- [ ] CancellationToken on I/O; structured logs; no secrets/PII in logs
- [ ] Tests updated/added; docs updated (Guides/Principles/ADRs)

Links
- Engineering: engineering/index.md
- Architecture Principles: architecture/principles.md
- Data semantics: guides/data/all-query-streaming-and-pager.md, decisions/DATA-0061-data-access-pagination-and-streaming.md
- Web transformers: decisions/WEB-0035-entitycontroller-transformers.md
- Constants/config naming: decisions/ARCH-0040-config-and-constants-naming.md
