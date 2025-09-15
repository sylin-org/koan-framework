Developer: ## Role and Objective
You are contributing to **Koan**, a greenfield framework for modern, modular applications that emphasizes **simplicity** and **flexibility**, informed by lessons learned from the Zen framework.

Begin with a concise checklist (3-7 bullets) of your planned steps before making substantive documentation or code edits.

# Instructions

## Core Engineering Principles (Mandatory)

Premium DX, with semantically meaningful choices and sane-defaults. Terse but expressive interfaces/methods. Context-aware abstractions are preferred.
Default assumptions apply unless a documented ADR grants an exception:
This is a greenfield framework. Break and change as necessary, concerns for back-compat are minimum at the time. Interrupt and ask if you think answers will provide better direction.


## Data Access (Priority Guidance)

- Use first-class static model methods for prominent data access in samples, documentation, and code suggestions:
  - Examples: `MyModel.All(ct)`, `MyModel.Query(...)`, `MyModel.AllStream(...)`, `MyModel.QueryStream(...)`, `MyModel.FirstPage(...)`, `MyModel.Page(...)`
- Use generic facades like `Data<TEntity, TKey>` only when a first-class static is unavailable.
- For large result sets, prefer streaming (`AllStream`, `QueryStream`) or explicit paging (`FirstPage`, `Page`); avoid non-paged `All`/`Query` over large data.

**References:**

- `/docs/guides/data/all-query-streaming-and-pager.md`
- `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`

### 0. Prime Directives

- Prioritize separation of concerns, DRY (Don't Repeat Yourself), and ease of use.
- Prefer concise method names (e.g., `Save()`, `Get()`, `Find()`). Avoid unnecessary repetition.

### 1. Controllers, Not Inline Endpoints

- Expose HTTP routes via MVC controllers using attribute routing.
- Do not declare endpoints inline (avoid `MapGet`/`MapPost`/etc. in startup or module initialization).
- Reference: `/docs/decisions/WEB-0035-entitycontroller-transformers.md` for payload shaping.

### 2. No Stubs or Empty Artifacts

- Remove empty classes, placeholder files, and commented-out scaffolds.
- Do not stub code "for later"; retain only code that delivers current value.

### 3. No Magic Values; Centralize Constants

- Elevate magic literals to a unified Constants class scoped to the project (e.g., `Infrastructure/Constants`).
- Use typed Options for tunables and constants for stable identifiers (headers, routes, keys).
- Do not scatter literals across the codebase.
- Reference: `/docs/decisions/ARCH-0040-config-and-constants-naming.md`.

### 4. Project Root Hygiene

- For projects with over four classes, only the `.csproj` should reside at root; organize sources into folders (e.g., Controllers, Hosting, Extensions).
- Avoid placing multiple `.cs` source files at the project root in larger projects.

### 5. Class/File Layout and Co-location

- One public/top-level class per file. Nest satellite helpers within main types rather than using separate files.
- Interfaces and attributes may share a file only if they address the same concern; name such files accordingly (e.g., `AuthorizationHooks.cs`).
- Place unrelated types in separate files with descriptive, concern-reflective filenames.

## Working Conventions

- Document all changes; update `/docs` or ADRs as needed to reflect current implementations and decisions.
- Remove backward-compatibility shims unless explicitly required. Favor clean, cohesive designs.
- Submit improvement proposals for unclear areas. Make enhancements small and reviewable.
- Prioritize predictable defaults, sound folder structure, and consistent naming to optimize developer experience.
- Choose simple and intuitive constructs.

## Quick Checklist

- Add or modify HTTP routes only within controllers.
- Remove empty or placeholder files before committing.
- Replace literals with constants or options and centralize them in the Constants class.
- When a project exceeds four classes, organize files into folders, leaving only the `.csproj` at root.
- Maintain one public class per file, nest satellite helpers, and co-locate interfaces/attributes only when addressing the same concern.
- For any exceptions, document the rationale and scope in `/docs/decisions` as an ADR.
- Refrain from custom methods when similar ones are available in Koan.Core libraries.

**Also see:**

- Engineering: `/docs/engineering/index.md`
- Architecture: `/docs/architecture/principles.md`

## Doc Requests: Translate "document..." into Edits

- Select the appropriate target per routing map. Emphasize instruction-first content (not tutorials).
  - For modules/projects: follow `ARCH-0042`. Create `README.md` (information, setup, safe snippets) and `TECHNICAL.md` (reference, architecture) at project root.
  - For architectural policies: add ADRs to `docs/decisions` and update `toc.yml`.
  - For engineering guidance: edit/extend under `docs/engineering/`.
  - For reference, how-to, API docs: update/add under `docs/reference/` (e.g., `reference/data-access.md`).
  - For web conventions: use `docs/api/web-http-api.md`, etc.
  - For adapters: update `docs/reference/_data/adapters.yml`. Allow the build process to update generated docs; do not edit generated artifacts manually.
  - For guides: create under `docs/guides/<area>/` and update local TOCs.

**Include developer samples:**

- Provide code snippets (e.g., C# model statics, HTTP controller examples) and URLs to samples for context. Ensure samples are brief and production-safe.

**Content patterns:**

- Start documentation with a concise contract block (inputs/outputs, error modes, criteria).
- List 3–5 edge cases (e.g., null, large data, permissions, concurrency).
- Consolidate literals, cross-link relevant ADRs and canonical docs.

## Process Checklist

1. Select documentation targets as indicated by the routing map.
2. Register new ADRs in `docs/decisions/toc.yml`.
3. Update adapters YAML; rely on the build process to update generated docs.
4. Revise any affected TOCs.
5. Run the strict docs build and resolve any link issues.
6. Use a clear, conventional commit message (e.g., `docs(ref): web pagination headers with examples`).

After each documentation or code change, validate that updates are correctly linked, formatted, and reflect the intended change. If validation fails (e.g., link errors, build failures), self-correct and rerun the checklist before finalizing.

## Posture Guardrails (Strict)

- Avoid tutorials, quickstarts, or course-like flows. Keep documentation focused and instructional (per ARCH-0041).
- Use first-class model statics in all code samples.
- Do not manually modify generated documentation (files under `docs/reference/_generated/**`).

## Handy Anchors

- When asked, ensure all documentation remains accurate and up-to-date. Reflect new decisions promptly.
- Refer to core documentation entry points:

  - Engineering front door: `/docs/engineering/index.md` (high-signal rules and links)
  - Architecture principles: `/docs/architecture/principles.md` (curated ADR digest)
  - Documentation root: `/docs` and `/docs/toc.yml`

- Data Access: `/docs/guides/data/all-query-streaming-and-pager.md`, `/docs/guides/data/working-with-entity-data.md`, `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
- Web API: `/docs/api/web-http-api.md`, `/docs/api/openapi-generation.md`, `/docs/decisions/WEB-0035-entitycontroller-transformers.md`
- Messaging: `docs/reference/messaging.md`, decisions in `docs/decisions/MESS-*.md`
- Adapter Matrix: `docs/reference/_data/adapters.yml` → `docs/reference/adapter-matrix.md` (generated)
