# Koan documentation engagement plan

Purpose
- Make Koan’s guidance obvious, accurate, and runnable for developers and architects.
- Drive a populate-first, then migrate approach with clear milestones, quality gates, and ownership.

Guiding principles
- Engineering and Architecture front doors stay prominent and current.
- One source of truth for capability facts (adapter matrix YAML) rendered automatically.
- Examples-first: short, runnable snippets before deep dives.
- Strict builds: no warnings for new/changed docs; redirects for migrations.

Scope and milestones
1) Populate core references (2 weeks)
   - Data Access: examples for All/Query/Stream/Page; guardrails; instructions usage.
   - Web: transformers and payload shaping examples (WEB-0035); controller-only posture.
   - Messaging: idempotency, leasing, poison flows; batch semantics.
   - AI: streaming chat + embeddings; minimal RAG sample.
   - Config & Constants: Key Registry and first-win examples.

2) Solidify adapter layer (1 week)
   - Keep `reference/_data/adapters.yml` authoritative; auto-generate matrix table.
   - Add maintainer README for fields/values and update policy.
   - Ensure each adapter guide links back to reference and matrix.

3) Architecture and cross-cutting (1 week)
   - Observability reference: ActivitySource, tags, LoggerMessage guidance.
   - Security posture: AuthN/Z seams, secrets, DDL dev-only toggles, rate limiting.
   - Performance guide: streaming vs paging, batching, indexes/computed columns.

4) Migrate legacy content (1 week)
   - Move/retire historical pages; add redirects or landing notes.
   - Ensure TOC is lean and high-signal; remove stubs/duplicates.

Deliverables checklist (living)
- [ ] Data Access reference has runnable examples for: All, Query, AllStream, QueryStream, FirstPage, Page.
- [ ] Data Access shows Instructions usage (scalar/nonquery/query; ensure/clear) with safety notes.
- [ ] Web reference includes transformer sample and anti-pattern note (no inline endpoints).
- [ ] Messaging reference covers dedupe window, leasing/visibility, poison routing, batch send.
- [ ] AI reference includes chat stream + embed examples and minimal RAG path.
- [ ] Config & Constants includes a Key Registry table and Constants/Options patterns.
- [ ] Adapter matrix auto-generates from YAML in builds; maintainer README exists.
- [ ] Observability reference documents trace/log conventions and tag keys.
- [ ] Security reference covers secrets and policies; links to ADRs.
- [ ] Performance guide outlines guardrails and tuning tips per store class.
- [ ] Legacy pages reviewed; migrations/redirects added; Strict build clean.

Quality gates
- Build, Strict docs build (no new warnings), link checks clean.
- Each page: examples compile (when applicable), consistent headings and anchors, See also cross-links present.
- ADR links included when asserting policy decisions.

Execution model
- Weekly slice: ship a cohesive set (e.g., Data Access reference + examples + cross-links).
- Each slice ends with Strict build and a short release note in `docs/support/release-flow.md`.
- Ownership: Docs WG (dev + arch), with PR review from core maintainers.

Workboard (initial breakdown)
- Week 1
  - Expand `reference/data-access.md` examples (6 items), add Instructions section.
  - Add transformer example to `reference/web.md` and link WEB-0035.
  - Add Config Key Registry to `reference/config-and-constants.md`.
- Week 2
  - Messaging examples (idempotency, leasing, poison, batch) in `reference/messaging.md`.
  - AI chat/embed minimal paths in `reference/ai.md`.
- Week 3
  - Add `reference/observability.md` and wire cross-links.
  - Add `reference/security.md` (initial posture).
- Week 4
  - Performance guide `guides/performance.md`.
  - Migrate legacy pages with redirects and prune TOC.

Tracking and updates
- Keep this plan updated at the top with “What’s next” and mark completed items.
- When adapter capabilities change, update `adapters.yml` and re-run docs build (auto-table).
