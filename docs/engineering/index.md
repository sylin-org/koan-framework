---
type: GUIDE
domain: engineering
title: "Koan Engineering Guardrails"
audience: [developers, maintainers, ai-agents]
status: current
last_updated: 2025-09-29
framework_version: v0.2.18+
validation:
  date_last_tested: 2025-09-29
  status: verified
  scope: docs/engineering/index.md
---

# Koan Engineering Front Door

## Contract
- **Scope**: Day-to-day guardrails for contributors working inside the Koan repository.
- **Inputs**: Existing modules, ADRs, scripts (`apply-version.ps1`, `scripts/validate-packages.ps1`), and framework conventions.
- **Outputs**: Code and docs that comply with controller-first web APIs, entity-first data patterns, packaging standards, and documentation posture.
- **Failure modes**: Inline endpoints, repository abstractions over entities, scattered magic literals, missing README/TECHNICAL companions, or drifting NuGet metadata.
- **Success criteria**: Features land with controllers, entity statics, centralized constants/options, validated packaging metadata, and updated companion docs.

## Quick Links
- [Packaging policy](packaging.md)
- [Architecture principles](../architecture/principles.md)
- [Documentation posture (ARCH-0041)](../decisions/ARCH-0041-docs-posture-instructions-over-tutorials.md)
- [Script-owned versioning (BUILD-0072)](../decisions/BUILD-0072-script-owned-versioning.md)

## Prime Guardrails

1. **Entity-first data access**  
   Use `Entity<T>` static methods such as `Todo.All(ct)`, `Todo.Query(...)`, `Todo.FirstPage(...)`, or streaming APIs before considering `Data<TEntity, TKey>`. Repository patterns and bespoke data services are rejected unless the entity surface is insufficient and an ADR exists.

2. **Controller-surfaced HTTP APIs**  
   All routes live in MVC controllers with attribute routing. Avoid `MapGet/MapPost` shortcuts. Reuse `EntityController<T>` for CRUD and trim overrides to behavior changes.

3. **No magic literals**  
   Promote stable identifiers to `Infrastructure/Constants.cs`; prefer typed Options for tunables. Reference: [ARCH-0040](../decisions/ARCH-0040-config-and-constants-naming.md).

4. **Project hygiene**  
   Keep only the `.csproj`, `README.md`, and `TECHNICAL.md` at project roots. Organize files into concern-based folders and ensure namespaces mirror directory layout.

5. **Companion documentation**  
   Every shipping module maintains `README.md` (quick orientation) and `TECHNICAL.md` (in-depth contract) as mandated by [ARCH-0042](../decisions/ARCH-0042-per-project-companion-docs.md).

## Packaging Checklist
- Update `version.json`; run `apply-version.ps1` instead of editing `<Version>` nodes.
- Ensure `<Description>`, `<PackageTags>`, and `<GenerateDocumentationFile>true</GenerateDocumentationFile>` are set.
- Write or update per-project `README.md` with controller/entity examples.
- Execute `scripts/validate-packages.ps1` locally and wire it into CI jobs touching packaging.
- Dotnet tools set `<PackAsTool>true</PackAsTool>` and document install commands; analyzers ship assets under `analyzers/dotnet/cs`.

See the [NuGet packaging policy](packaging.md) for detailed expectations and follow-ups.

## Documentation Guardrails
- Author instruction-first content; avoid tutorials and quickstarts (per ARCH-0041).
- Register new ADRs in `docs/decisions/toc.yml` and cross-link from relevant guides.
- When adding new modules, update the Reference section and ensure samples align with guardrails.

## Change Workflow
1. **Trace existing surfaces**: Search the repo/samples before introducing new helpers.
2. **Document decisions**: Add ADRs for structural changes; update `docs/engineering/**` and module-level companion docs.
3. **Validate**: Run unit or integration tests applicable to touched components plus the docs build (`scripts/build-docs.ps1 -Strict`).
4. **Package lint**: Execute `scripts/validate-packages.ps1` to confirm metadata compliance before PRs.

## Edge Cases & Escalation
- **Large data operations**: Prefer `Entity.AllStream(...)` or explicit paging; flag data guides when new patterns arise.
- **Provider capabilities**: Guard fallbacks when `Data<TEntity, TKey>.QueryCaps` lacks LINQ pushdown.
- **Environment detection**: Use `KoanEnv` for environment checks; avoid raw `IHostEnvironment` or env vars.
- **Configuration overrides**: Introduce typed Options with validation; do not branch on raw configuration keys inside business logic.

Escalate policy questions in `docs/decisions/` with a new ADR; update this front door when guardrails change.
