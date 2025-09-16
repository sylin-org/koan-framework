# Config & Constants Reference

## Configuration helpers
- Use Koan.Core.Configuration helpers: Read(...), ReadFirst(...).
- Prefer canonical ":" keys; helpers adapt provider/env shapes.

## Constants policy
- One `Constants` class per assembly; rely on namespaces for clarity.
- Use constants for stable names (routes, headers, keys, defaults); use Options for tunables.

## Examples (keys)
- Koan:Web:RoutePrefix
- Koan:Data:DefaultPageSize
- Koan:Messaging:DefaultGroup
- Koan:AI:Provider

## References
- decisions/ARCH-0040-config-and-constants-naming.md
 - engineering/developer-guidelines.md

### Snippet
```csharp
// Constants class per assembly
namespace MyApp.Web;
internal static class Constants
{
	public const string RoutePrefix = "/api";
}

// Reading configuration (first-win)
var prefix = KoanConfig.ReadFirst("Koan:Web:RoutePrefix") ?? Constants.RoutePrefix;
```

## Edge cases
- Missing keys: always provide sane defaults in `Constants`; do not assume configuration is present.
- Invalid types: configuration values may be strings; validate and convert with clear errors.
- Precedence: environment variables override appsettings; document your effective source when debugging.
- Secrets: never log values; redact in boot reports; prefer a secrets provider in production.
