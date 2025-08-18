# Configuration helper and extensions

Sora ships a tiny, resilient configuration helper and a set of `IConfiguration` extensions to keep call sites terse and consistent.

## TL;DR
- Prefer `cfg.Read(key, default)` when `IConfiguration` is in scope.
- Use `Sora.Core.Configuration.Read(key, default)` when it isn’t.
- Fallbacks: `cfg.ReadFirst(keyA, keyB, ...)` or with default overloads for string/bool/int.

## Behavior
- Canonical keys use `:`. The helper probes env var shapes (`:`, `__`, `_`, with uppercase variants) and `IConfiguration` flattening.
- Precedence: Environment > IConfiguration > default.
- Typed reads with robust conversions (bool on/off/1/0/yes/no, numbers, enums, timespan).

## Examples
- `var enabled = cfg.Read(Sora.Web.Swagger.Infrastructure.Constants.Configuration.Enabled, false);`
- `var magic = cfg.Read(Sora.Core.Infrastructure.Constants.Configuration.Sora.AllowMagicInProduction, false);`
- `var env = cfg.ReadFirst(Sora.Core.Infrastructure.Constants.Configuration.Env.AspNetCoreEnvironment, Sora.Core.Infrastructure.Constants.Configuration.Env.DotnetEnvironment);`

## Notes
- Keep constants per assembly in a `Constants` class; rely on namespaces for readability.
- Avoid `cfg["..."]` and direct `Environment.GetEnvironmentVariable` where possible.
