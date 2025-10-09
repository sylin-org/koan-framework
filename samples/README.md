# Samples Directory

This folder hosts scenario-driven examples grouped by family using the `SXX.Name` convention.

Organization Standard

- Family root: `SXX.Name/` (e.g., `S16.PantryPal`, `S13.DocMind`, `S12.MedTrials`)
- Subprojects inside family root:
  - `API/` — Web API app
  - `Web/` — UI-first web app (optional)
  - `MCP/` — Model Context Protocol service/host (optional)
  - `AppHost/` — Aspire/orchestrator host (optional)
  - `Core/` — Shared contracts/models/services (optional)
  - `Tools/` — CLI or worker tools (optional)
  - `Infra/` — Infrastructure/adapters demos (optional)
  - `Docs/` — Family-specific docs (optional)

Current Migration Targets (non-destructive plan)

- S16.PantryPal
  - `S16.PantryPal` → `S16.PantryPal/API`
  - `S16.PantryPal.McpHost` → `S16.PantryPal/MCP`
- S12.MedTrials
  - `S12.MedTrials` → `S12.MedTrials/API`
  - `S12.MedTrials.Core` → `S12.MedTrials/Core`
  - `S12.MedTrials.McpService` → `S12.MedTrials/MCP`
- S13.DocMind
  - `S13.DocMind` → `S13.DocMind/API`
  - `S13.DocMind.Tools` → `S13.DocMind/Tools`
- S2
  - `S2.Api` → `S2/API`
  - `S2.Client` → `S2/Client`
  - `S2.Compose` → `S2/compose`

Notes

- Each subproject retains its `start.bat` for a one-command run experience on Windows.
- Don’t add empty folders; only create subprojects that exist.
- Guides remain under `samples/guides/`.

See `docs/engineering/samples-organization.md` for the full standard and guidelines.
