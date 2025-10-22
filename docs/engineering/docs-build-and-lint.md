---
type: ENGINEERING
domain: engineering
title: "Docs build and lint"
audience: [maintainers, developers]
status: current
last_updated: 2025-10-09
framework_version: v0.6.3
validation:
  status: not-yet-tested
  scope: docs/engineering/docs-build-and-lint.md
---

# Docs build and lint

This page describes the Koan documentation build and lint gates used in CI and locally.

- DocFX builds run in strict mode and fail on warnings.
- A PowerShell linter script validates front-matter, links/anchors, version sync, and optionally TOC integrity.
- In CI, the powershell-yaml module is installed and TOC validation is mandatory.

See scripts/build-docs.ps1 and scripts/docs-lint.ps1 for details.
