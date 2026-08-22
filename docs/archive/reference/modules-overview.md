---
type: REF
domain: platform
title: "Koan Modules Reference"
audience: [developers, architects, ai-agents]
status: deprecated
last_updated: 2026-08-14
framework_version: v1.0.0
validation:
  status: passed
  date_last_tested: 2026-07-17
  scope: redirect to the generated product surface
---

# Koan modules reference

The hand-maintained module catalog is retired. Use the generated
[Koan product surface](../../reference/product-surface.md), which derives installable package facts from evaluated .NET projects
and joins them to explicit maturity and evidence claims.

The Markdown projection is checked in for readers. Generate the machine-readable JSON only when a
tool needs it:

```bash
dotnet run --project tools/Koan.Packaging -- product-surface \
  --output artifacts/product-surface.json
```

Refresh the checked-in projection with `product-surface --markdown docs/reference/product-surface.md`.
