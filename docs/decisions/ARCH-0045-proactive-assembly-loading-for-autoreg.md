# ADR: Proactive Sora.* Assembly Loading for Zero-Config Adapter Auto-Registration

## Status
Accepted

## Context
Sora Framework's core engineering principle is zero-config, safe auto-registration of all referenced adapters (e.g., Sqlite, Mongo, JSON) without explicit registration. In minimal test setups, adapter assemblies may not be loaded into the AppDomain before DI is built, causing auto-registration to fail. This breaks the zero-config promise and leads to inconsistent developer experience between tests and real applications.

## Decision
Sora.Core's AppBootstrapper will proactively load all referenced Sora.* assemblies from the base directory before running `InitializeModules`. This guarantees that all ISoraAutoRegistrar and ISoraInitializer implementations are discovered and invoked, regardless of runtime context or usage pattern.

## Consequences
- Zero-config adapter auto-registration is guaranteed in all contexts (apps, tests, samples).
- No need for explicit adapter registration or forced type loading in tests.
- Consistent developer experience and reliable dependency discovery.
- Slight increase in startup time due to additional assembly loads, but outweighed by DX and reliability benefits.

## Implementation
- Update AppBootstrapper to scan the base directory for Sora.*.dll files and load any not already present in the AppDomain before invoking InitializeModules.
- Document this behavior in engineering and data access guides.

## Edge Cases
- Assemblies with side effects on load must remain safe and idempotent.
- If an adapter assembly is present but not referenced, it will still be loaded, but will not affect DI unless its registrar is present.

## References
- /docs/engineering/index.md
- /docs/guides/data/all-query-streaming-and-pager.md
- /docs/decisions/DATA-0061-data-access-pagination-and-streaming.md
- /docs/decisions/ARCH-0040-config-and-constants-naming.md
