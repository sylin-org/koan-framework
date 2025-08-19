# 0036: Sylin.* prefix for NuGet package IDs

Status: Accepted
Date: 2025-08-17

Context
- We own sylin.org and will publish packages under a verified "Sylin" NuGet publisher.
- Code namespaces remain `Sora.*` to avoid churn and preserve clarity.

Decision
- Publish all NuGet packages with IDs `Sylin.Sora.*`.
- Keep code namespaces `Sora.*`.
- Provide meta packages:
  - `Sylin.Sora` (core + data abstractions + JSON adapter)
  - `Sylin.Sora.App` (Sylin.Sora + Sora.Web)
- Future capability packs: `Sylin.Sora.Pack.*`.

Consequences
- Discoverable, consistent package IDs; minimal code changes.
- Docs/READMEs must reference `Sylin.Sora.*` IDs for installs.

See also
- 0010: Meta packages (Sora, Sora.App)
