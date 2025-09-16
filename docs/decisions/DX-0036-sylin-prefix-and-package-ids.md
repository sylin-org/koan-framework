---
id: DX-0036
slug: DX-0036-sylin-prefix-and-package-ids
domain: DX
status: Accepted
date: 2025-08-17
title: Sylin.* prefix for NuGet package IDs
---
 
# 0036: Sylin.* prefix for NuGet package IDs

Context
- We own sylin.org and will publish packages under a verified "Sylin" NuGet publisher.
- Code namespaces remain `Koan.*` to avoid churn and preserve clarity.

Decision
- Publish all NuGet packages with IDs `Sylin.Koan.*`.
- Keep code namespaces `Koan.*`.
- Provide meta packages:
  - `Sylin.Koan` (core + data abstractions + JSON adapter)
  - `Sylin.Koan.App` (Sylin.Koan + Koan.Web)
- Future capability packs: `Sylin.Koan.Pack.*`.

Consequences
- Discoverable, consistent package IDs; minimal code changes.
- Docs/READMEs must reference `Sylin.Koan.*` IDs for installs.

See also
- 0010: Meta packages (Koan, Koan.App)
