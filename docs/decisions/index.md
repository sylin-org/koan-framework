# Architectural & Technical Decisions

This index aggregates accepted and proposed decision records. Each ADR is:

- Stable once marked Accepted
- Superseded only via an explicit Supersedes / Superseded-By chain
- Focused on one concern (single-responsibility guidance)

Refer to `toc.yml` for categorized navigation. Use the template (`AAAA-0000`) for new ADRs.

## Recent Archival / Streamlining Decisions

| ID | Title | Intent |
|----|-------|--------|
| ARCH-0062 | S8 legacy snapshot removal and sample streamline | Removed obsolete duplicate S8 snapshot; clarified single active sample path. |

## Authoring Principles

1. Decision first – lead with the outcome, then rationale.
2. Cite related ADR IDs (avoid re-stating prior rationale).
3. Capture consequences (positive, negative, neutral) explicitly.
4. Prefer removal over indefinite deprecation when safe (reduces cognitive load).
5. Reference = Intent: inclusion in the solution implies support & maintenance.

## Change Workflow (Summary)

1. Draft ADR using template.
2. Add file under `docs/decisions` with next sequential domain prefix & number.
3. Register in `docs/decisions/toc.yml`.
4. Reference from related ADRs if superseding.
5. Validate doc build (link integrity, anchors).

## Tags Recommendation

When removing sizable code surfaces, prefer a lightweight git tag (e.g., `archive/<area>-<date>`) instead of leaving dead code in-tree.

---
This index is intentionally terse. For pillar-specific entry points see root documentation TOC.
