# Testing Guide

## Unit tests
- Prefer fast unit tests for core packages. Avoid hitting network or external services.

## Adapter tests
- Create `<Provider>.Tests` project alongside the adapter.
- Include CRUD tests and instruction execution tests.
- Use temporary files or in-memory modes when possible (e.g., SQLite file DB).

## Samples
- Keep runnable samples minimal. Provide a start script and HTTP request file if web-based.
 - Console samples that use Sora adapters should register a minimal IConfiguration in DI so adapter option configurators can resolve. In tests, prefer overriding options via `services.PostConfigure<...Options>(o => ...)` to avoid depending on environment config.

## CI tips
- `dotnet build` then `dotnet test` on solution.
- Keep logs concise; fail fast.

## Integration tests for health
- Use `WebApplicationFactory<Program>` to bootstrap sample apps.
- Override configuration to simulate failures:
	- JSON: `Sora:Data:Sources:Default:json:DirectoryPath` → invalid path
	- SQLite: `Sora:Data:Sources:Default:sqlite:ConnectionString` → invalid file path
- Assert `/health/live` returns 200 and `/health/ready` returns 503 in failure scenarios.
- To avoid cross-test contamination due to cached per-entity configs, call `Sora.Data.Core.TestHooks.ResetDataConfigs()` at the start of each test.
