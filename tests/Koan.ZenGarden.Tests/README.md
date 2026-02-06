# Koan.ZenGarden.Tests

Integration tests for the greenfield `Koan.ZenGarden` tools-domain adapter.

## What it exercises

- Catalog reachability for offerings and seed banks.
- Normalized tool shape (`tool_fqid`, `tool_type`).
- Initial subscription events for:
  - offering availability
  - offering capability requirement satisfaction
  - storage (seed-bank) availability

## Environment

- `KOAN_TESTS_ZENGARDEN_ENDPOINT`
  - Optional explicit Moss endpoint override.
  - If omitted, tests use `GARDEN_STONE` (if set) or UDP discovery.
- `GARDEN_STONE`
  - Optional selector/endpoint used by Zen Garden endpoint resolution.
- `KOAN_TESTS_ZENGARDEN_OFFERING`
  - Preferred offering for targeted checks.
  - Default: `mongodb`
- `KOAN_TESTS_ZENGARDEN_REQUIRED`
  - If `1/true/yes/on`, endpoint unavailability fails tests.
  - If unset/false, tests soft-skip when endpoint is unreachable.

## Run

```powershell
dotnet test tests\Koan.ZenGarden.Tests\Koan.ZenGarden.Tests.csproj
```
