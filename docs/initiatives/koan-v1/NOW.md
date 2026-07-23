---
type: HANDOFF
domain: koan-v1
status: passed
last_updated: 2026-07-22
framework_version: v0.20.0
---

# Koan v1 — current handoff

## Current state

[R14](work-items/R14-public-documentation-product-surface.md) is complete. The current non-ADR public
documentation is one greenfield, capability-led product surface for developers, coding agents,
operators, maintainers, and architects.

- All 40 promoted product claims have exactly one canonical capability home.
- All current public documents are reachable; there are zero current-document orphans.
- Fifteen legacy capability cards are concise superseded pointers.
- Generated product truth is the sole maturity authority; 22 duplicate package statements were
  removed and a permanent lint rule guards the boundary.
- S3 and Backup remain explicitly shelved.
- ADRs were not changed.
- The branch is `work/public-docs-greenfield`; changes are local and uncommitted.

## Validation

- `pwsh scripts/public-docs-lint.ps1`
- `pwsh scripts/skills-lint.ps1 -Strict`
- `dotnet run --project tools/Koan.Packaging -- product-surface --check`
- focused docs link/frontmatter lint
- `git diff --check`
- changed-diff privacy inspection

These checks cover the requested public project documentation surface. DocFX/API generation,
dependency restoration, and broad framework builds are deliberately outside this documentation
epic.

## Next action

There is no active implementation item in this epic. Review the local diff, then commit or publish
only when explicitly requested.

## Guardrails

- Never disclose private downstream identity, artifacts, paths, or distinctive workflows.
- Keep generated product surface as the sole maturity authority.
- Do not create a second documentation inventory, human/agent manual pair, or per-page work-card
  tree.
- Keep changes local and uncommitted until explicitly requested.
