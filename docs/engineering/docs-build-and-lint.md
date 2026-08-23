---
type: ENGINEERING
domain: engineering
title: "Docs build and lint"
audience: [maintainers, developers]
status: current
last_updated: 2025-10-09
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/engineering/docs-build-and-lint.md
---

# Docs build and lint

This page describes the Koan documentation build and lint gates. **Both are run deliberately, not by
CI** -- no workflow invokes them today, so a documentation defect reaches `main` unless someone runs
them first.

```powershell
pwsh -File scripts/docs-lint.ps1              # docs/ -- the default root
pwsh -File scripts/docs-lint.ps1 -Roots src   # package READMEs and technical companions
```

`docs-lint.ps1` exits non-zero on any Error, so it composes into a shell chain or a future workflow
without further wiring. Warnings do not fail unless `-FailOnWarning` is passed.

What it checks:

- **FrontMatter** -- required keys and their allowed values.
- **Links** -- every markdown link resolves, and heading anchors exist in markdown targets.
- **Paths** -- repository paths cited in backticks resolve. The Links check cannot see these, which is
  how three dead references survived a full documentation pass; records that describe the tree as it
  was (decisions, initiatives, archives, ledgers, implementation plans, templates) are exempt.
- **Redirects** -- no link reaches the legacy `/documentation` tree.
- **Terminology** -- discouraged terms from `docs/_term-map.json`.
- **Toc** -- optional with `-ValidateToc`; requires the powershell-yaml module.

DocFX builds run in strict mode and fail on warnings; see `scripts/build-docs.ps1`.
