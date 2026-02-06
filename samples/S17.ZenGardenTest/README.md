# S17.ZenGardenTest

Manual smoke harness for the greenfield `Koan.ZenGarden` tools-domain adapter.

## What it exercises

- Catalog listing for offerings and seed banks.
- Offering subscription (with optional capability requirements).
- Storage subscription for seed-bank availability.
- Live stream consumption over `tools/stream`.

## Environment variables

- `KOAN_ZENGARDEN_ENDPOINT`
  - Optional explicit Moss endpoint override.
  - If omitted, the sample uses `GARDEN_STONE` (if set) or UDP discovery.
- `GARDEN_STONE`
  - Optional endpoint/selector used by Zen Garden clients (for example `stone-01` or `http://stone-01:7185`).
- `KOAN_ZENGARDEN_OFFERING`
  - Preferred offering to watch.
  - Default: `mongodb`
- `KOAN_ZENGARDEN_STORAGE`
  - Preferred seed-bank to watch.
  - Default: `default`
- `KOAN_ZENGARDEN_CAPABILITIES`
  - Optional CSV requirements, e.g. `llama3.2,nomic-embed-text`.
  - Type prefixes are optional and mainly useful for disambiguation.
  - If omitted, requirements are derived from the selected offering capabilities.
- `KOAN_ZENGARDEN_WATCH_SECONDS`
  - How long to keep streaming live events after initial events.
  - Default: `10`

## Run

```powershell
dotnet run --project samples\S17.ZenGardenTest\S17.ZenGardenTest.csproj
```
