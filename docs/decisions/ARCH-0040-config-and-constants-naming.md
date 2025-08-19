---
id: ARCH-0040
slug: ARCH-0040-config-and-constants-naming
domain: ARCH
status: Accepted
date: 2025-08-18
---

# ADR: Naming simplification for configuration helper and per-assembly constants

## Context
SoraConfig provided resilient configuration access. Call sites were verbose (Sora.Core.Configuration.SoraConfig.Read). Per-assembly constants were named with long prefixes (e.g., WebSwaggerConstants), which made usage noisy and inconsistent.

## Decision
1) Rename SoraConfig to a static class named Configuration in the Sora.Core namespace. Call sites now use Sora.Core.Configuration.Read/ReadFirst, keeping names short, explicit, and discoverable.
2) Standardize per-assembly constants type name to Constants, relying on namespaces for clarity (e.g., Sora.Web.Swagger.Infrastructure.Constants). No functional behavior changed.

## Consequences
- Simpler call sites and consistent ergonomics across modules.
- Namespaces disambiguate Constants where many assemblies expose one; using-aliases remain an option when needed.
- No shims introduced; all call sites updated.

## Alternatives considered
- using static Sora.Core.Configuration.SoraConfig at call sites. Rejected: scattered file-level imports hide intent; rename is clearer.
- Adding IConfiguration extensions as sugar. Deferred: possible future addition for even shorter ergonomics (cfg.Read(...)).

## Migration notes
- Fully-qualified calls updated from Sora.Core.Configuration.SoraConfig.Read to Sora.Core.Configuration.Read (and similar for ReadFirst and typed overloads).
- Constants classes renamed in Web.Swagger, Web.Transformers, Messaging.Core.
