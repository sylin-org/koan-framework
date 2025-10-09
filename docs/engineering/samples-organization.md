## Samples Organization Standard

Contract

- Inputs: Existing sample folders under `samples/` (e.g., `S12.MedTrials`, `S12.MedTrials.Core`, `S12.MedTrials.McpService`, `S13.DocMind`, `S13.DocMind.Tools`, `S16.PantryPal`, `S16.PantryPal.McpHost`).
- Outputs: A clear, predictable folder layout for “sample families”: `SXX.Name/<Subproject>` where subprojects are API, Web, MCP, AppHost, Core, Tools, Infra, Docs.
- Error modes: Name collisions when moving, partial migrations, broken relative paths. Mitigation: dry-run, atomic moves per family, README updates, and git-clean check.
- Success criteria: All related samples are grouped under a single family root; top-level `samples/` is easy to scan; each subproject builds and its start script remains discoverable.

Rationale

The `samples/` folder currently has multiple siblings that belong to the same scenario (e.g., `S16.PantryPal` and `S16.PantryPal.McpHost`). Grouping these into a “family root” improves legibility, discoverability, and lowers the cognitive load for users browsing examples. This follows Koan’s principles: premium DX, predictable defaults, and clean organization.

Numbering and Naming

- Prefix `SXX.` is retained for scenario grouping and ordering (e.g., S0 = minimal, S1–S4 = fundamentals, S10+ = integrated scenarios/case studies).
- Family root naming: `SXX.QualifiedName` (e.g., `S16.PantryPal`, `S13.DocMind`, `S12.MedTrials`).
- Subproject folders are PascalCase nouns that reflect the surface or role:
  - API: HTTP APIs (typically EntityController-based web APIs)
  - Web: UI-first web apps
  - MCP: Model Context Protocol services/hosts
  - AppHost: Aspire or orchestrator host(s)
  - Core: shared domain, contracts, models, services
  - Tools: CLI or background tools
  - Infra: infrastructure, provisioning, or adapter demos
  - Docs: family-specific documentation (if needed)

Standard Layout (per family)

SXX.Name/
- API/               # Web API app (controllers, Models, Services, start.bat)
- Web/               # UI-first app (optional)
- MCP/               # MCP host/service (optional)
- AppHost/           # Aspire AppHost (optional)
- Core/              # Shared domain/contracts (optional)
- Tools/             # Utilities/CLI (optional)
- Infra/             # Infra/adapters/showcases (optional)
- Docs/              # Family docs and local READMEs (optional)
- README.md          # Family overview: purpose, components, how to run

Required Conventions (DX)

- Each subproject contains its own `start.bat` aligned with our Windows-first dev flow. Keep `Program.cs` minimal (AddKoan pattern). Avoid inline endpoints; use controllers.
- Root `README.md` explains the scenario, lists subprojects, and provides simple run instructions (link to subproject `README` when present).
- No stubs or empty projects. If a subproject is not used, don’t create an empty folder.
- Use standard Koan structure inside projects: `Models/`, `Services/`, `Controllers/`, `Contracts/`, `Infrastructure/`, `Initialization/`.
- Centralize constants and avoid magic values per Core Engineering Principles.

Streaming and Data Access in Samples

- In code samples and snippets, prefer entity statics: `MyModel.All(ct)`, `MyModel.Query(...)`, `MyModel.AllStream(...)`, `MyModel.FirstPage(...)`, etc. See DATA-0061.
- For larger sets, demonstrate `AllStream`/`QueryStream` or explicit paging. Avoid `All()` for large data paths.

Migration Guidance

1) Create the family root folder (e.g., `S16.PantryPal/`).
2) Move each related sibling into a subfolder with the correct role:
   - `S16.PantryPal` → `S16.PantryPal/API`
   - `S16.PantryPal.McpHost` → `S16.PantryPal/MCP`
   - `S12.MedTrials` → `S12.MedTrials/API`
   - `S12.MedTrials.Core` → `S12.MedTrials/Core`
   - `S12.MedTrials.McpService` → `S12.MedTrials/MCP`
   - `S13.DocMind` → `S13.DocMind/API`
   - `S13.DocMind.Tools` → `S13.DocMind/Tools`
3) Ensure each moved subproject still builds and runs via its `start.bat`.
4) Add a family-level `README.md` that describes components and how they relate.
5) Update docs links that referenced old paths. Prefer relative links from `docs/` to `samples/` families.

Edge Cases

- Name collisions: If a family already has `API/` or `Core/`, consolidate intentionally. Resolve duplicate files with maintainers.
- Cross-family reuse: If two families share a utility project, keep it under each family’s `Core/` (duplicated) OR promote to `samples/_shared/` with a small note in each family README.
- Guides and standalone demos (`samples/guides/`, one-offs like `S0.ConsoleJsonRepo`) remain at top level.
- Case studies spanning multiple families (e.g., docs integrations) should link to families, not duplicate code.

Worked Examples (Current Repo)

- S16.PantryPal
  - Before: `S16.PantryPal`, `S16.PantryPal.McpHost`
  - After: `S16.PantryPal/API`, `S16.PantryPal/MCP`
- S12.MedTrials
  - Before: `S12.MedTrials`, `S12.MedTrials.Core`, `S12.MedTrials.McpService`
  - After: `S12.MedTrials/API`, `S12.MedTrials/Core`, `S12.MedTrials/MCP`
- S13.DocMind
  - Before: `S13.DocMind`, `S13.DocMind.Tools`
  - After: `S13.DocMind/API`, `S13.DocMind/Tools`

Validation Checklist (Quality Gates)

- Build: Each subproject builds without errors after move.
- Start scripts: `start.bat` runs from subproject folder; family README documents how to run.
- Links: Docs and READMEs updated; no broken links in strict docs build.
- Requirements coverage: Related sample siblings are co-located under a family root; naming matches this policy.

Automation

A helper script `scripts/reorg-samples.ps1` is provided to perform a dry-run plan or apply the moves for known families. Extend it incrementally as new families are identified.

References

- Engineering: `/docs/engineering/index.md`
- Architecture: `/docs/architecture/principles.md`
- Data Access: `/docs/guides/data/all-query-streaming-and-pager.md`, `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
- Web API: `/docs/api/web-http-api.md`, `/docs/decisions/WEB-0035-entitycontroller-transformers.md`
