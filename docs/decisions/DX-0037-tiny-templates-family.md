---
id: DX-0037
slug: DX-0037-tiny-templates-family
domain: DX

title: Tiny* template family (TinyApp, TinyDockerApp, TinyWorker)
status: Accepted
date: 2025-08-17
---

# 0037: Tiny\* template family (TinyApp, TinyDockerApp, TinyWorker)

Context

- We want a frictionless "dotnet new" experience that showcases Koan quickly.
- Provide three web flavors and one worker flavor, with minimal deps.

Decision

- Ship templates in the `Sylin.Koan.Templates` pack with short names:
  - TinyApp (Koan-tiny-app): SQLite storage; web + swagger
  - TinyDockerApp (Koan-tiny-docker): Alpine.js SPA + API + MongoDB via compose; UI and API in same container
  - TinyWorker (Koan-tiny-worker): BackgroundService, no web
- TinyApi (Koan-tiny-api) was retired on 2025-09-29; subsequent template packs exclude it.
- Default ports follow sample scheme + 1000: 6055, 6155, 6255.
- API serves wwwroot where applicable.

Consequences

- New users can run locally instantly; container scenario covered by TinyDockerApp.
- Maintenance: version pinning in templates must track releases.

See also

- 0014: Samples port allocation
- 0010: Meta packages
