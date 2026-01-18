# Group A — High Value / Low Effort

**Do these first.** They unlock visible DX improvements with minimal risk.

- **A1. OpenAPI 3.1 as default** via `Microsoft.AspNetCore.OpenApi` + deprecate `WithOpenApi` usage. citeturn6search0turn6search1
- **A2. SSE streaming wrappers** (typed + string) for Minimal APIs and MVC. Default SSE for Koan MCP and AI streaming. citeturn1search0turn1search2
- **A3. JSON strategy**: Keep **Newtonsoft.Json** default; add **STJ strict** option where shapes are closed. citeturn3search0turn0search4
- **A4. CLI modernization**: one‑shot `dotnet tool exec`, `dnx` shim, tab‑completions. citeturn9view0
- **A5. Container/AOT lanes**: blessed publish profiles + docs for Windows, Docker/K8s, GHA, Azure DevOps. citeturn2search3turn2search12turn2search5
- **A6. JSON Patch (STJ)**: add optional STJ-based JsonPatch to avoid Newtonsoft coupling in Minimal APIs. citeturn4search3

> Koan principles this group leans on: **Reference = Intent, Deterministic configuration, Progressive complexity**, and **Escape hatches**. fileciteturn0file16
