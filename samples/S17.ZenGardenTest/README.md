# S17.ZenGardenTest

Console harness that proves `reference = intent` with Zen Garden auto-resolution.

By only referencing these adapters in the project file:

- `Koan.Data.Connector.Mongo`
- `Koan.AI.Connector.Ollama`
- `Koan.ZenGarden`

this sample boots with `services.AddKoan()` and resolves MongoDB + Ollama through Zen Garden automatically.

## What it shows

- live offering/storage catalog snapshot
- resolved intent diagnostics for `zen-garden://mongodb` and `zen-garden://ollama`
- Mongo write/read probe through `Entity<T>`
- Ollama chat probe through `Engine.Chat(...)`
- subscription events for offering/storage changes
- non-blocking capability wish request for Ollama models
- incremental capability progress events (`Requested`, `PartiallyFulfilled`, `Fulfilled`)

## Environment

- `KOAN_ZENGARDEN_ENDPOINT`
  - Optional explicit Moss endpoint override.
- `KOAN_TESTS_ZENGARDEN_ENDPOINT`
  - Alternate endpoint override.
- `GARDEN_STONE`
  - Optional selector used by Zen Garden discovery.
- `KOAN_ZENGARDEN_WATCH_SECONDS`
  - Event watch duration in seconds (default `5`).
- `KOAN_OLLAMA_WISH_CAPS`
  - Capability wish list for Ollama (csv or `|`, default `llama3.2,nomic-embed-text`).

## Run

```powershell
dotnet run --project samples\S17.ZenGardenTest\S17.ZenGardenTest.csproj
```

or run launcher:

```powershell
samples\S17.ZenGardenTest\start.bat
```
