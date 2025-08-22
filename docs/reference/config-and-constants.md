# Config & Constants Reference

## Configuration helpers
- Use Sora.Core.Configuration helpers: Read(...), ReadFirst(...).
- Prefer canonical ":" keys; helpers adapt provider/env shapes.

## Constants policy
- One `Constants` class per assembly; rely on namespaces for clarity.
- Use constants for stable names (routes, headers, keys, defaults); use Options for tunables.

## Examples (keys)
- Sora:Web:RoutePrefix
- Sora:Data:DefaultPageSize
- Sora:Messaging:DefaultGroup
- Sora:AI:Provider

## References
- decisions/ARCH-0040-config-and-constants-naming.md
- engineering guardrails
