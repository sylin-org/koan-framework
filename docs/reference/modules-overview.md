---
type: REF
domain: platform
title: "Koan Modules Reference"
audience: [developers, architects, ai-agents]
status: deprecated
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  status: passed
  date_last_tested: 2026-07-17
  scope: redirect to the generated product surface
---

# Koan modules reference

The hand-maintained module catalog is retired. Use the generated
[Koan product surface](product-surface.md), which derives installable package facts from evaluated .NET projects
and joins them to explicit maturity and evidence claims.

For machine consumption, use [`product-surface.json`](product-surface.json) or regenerate both views with:

```bash
dotnet run --project tools/Koan.Packaging -- product-surface \
  --output docs/reference/product-surface.json \
  --markdown docs/reference/product-surface.md
```
