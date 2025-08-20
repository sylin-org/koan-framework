---
id: ARCH-0010
slug: ARCH-0010-meta-packages
domain: ARCH
status: Accepted
date: 2025-08-16
---

# 0010: Introduce meta packages (Sora, Sora.App)

Context
- We want a one-NuGet out-of-the-box experience without coupling core to any storage implementation.
- JSON adapter is ideal for dev/demo; web support should be optional for non-web apps.

Decision
- Ship two meta packages (NuGet IDs published as Sylin.Sora and Sylin.Sora.App):
  - Sora (NuGet: Sylin.Sora): depends on Sylin.Sora.Core, Sylin.Sora.Data.Core, Sylin.Sora.Data.Abstractions, Sylin.Sora.Data.Json
  - Sora.App (NuGet: Sylin.Sora.App): depends on Sylin.Sora and Sylin.Sora.Web
- Keep assemblies modular; discovery continues to self-register when referenced.
- JSON adapter retains lowest provider priority; explicit registrations always override.

Consequences
- Beginner DX improves (single-package install).
- No architectural coupling of core to a specific provider.
- Versioning handled via meta dependencies.

See also
- 0009: Unify on IEntity<TKey>
