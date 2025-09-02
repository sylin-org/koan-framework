# 📝 Sora Copilot Instructions (Agent-Only)

## Role

Contribute to **Sora**, a greenfield framework for modern, modular apps.

- Break/change as needed.
- No backward-compat unless explicitly required.

---

## Response Format

- Start with a **checklist of 3–7 planned steps**.
- Then provide docs/code edits.

---

## Core Principles

- Separation of concerns. DRY. Easy.
- Terse async method names: `Save()`, `Get()`, `Find()`.
- Use context-aware abstractions with sane defaults.
- ADRs override defaults.

---

## Data Access

- Use first-class static methods:

  - `MyModel.All(ct)`
  - `MyModel.Query(...)`
  - `MyModel.AllStream(...)`
  - `MyModel.QueryStream(...)`
  - `MyModel.FirstPage(...)`
  - `MyModel.Page(...)`

- Use `Data<TEntity, TKey>` only if no static available.
- **Never** use `All`/`Query` unpaged for large sets. Must stream or page.

Refs:

- `/docs/guides/data/all-query-streaming-and-pager.md`
- `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`

---

## Controllers

- Routes only in MVC controllers with attributes.
- No `MapGet`/`MapPost` in startup/init.

Ref: `/docs/decisions/WEB-0035-entitycontroller-transformers.md`

---

## Artifacts

- Remove empty/stub classes/files.
- No commented scaffolds.

---

## Constants

- Centralize literals in `Infrastructure/Constants`.
- Use Options for tunables; constants for stable IDs (headers, routes, keys).
- Never scatter literals.

Ref: `/docs/decisions/ARCH-0040-config-and-constants-naming.md`

---

## Project Structure

- If >4 classes: keep only `.csproj` at root. Organize files in folders.
- One public/top-level class per file.
- Nest satellites inside main type.
- Interfaces + attributes may share file only if same concern (e.g. `AuthorizationHooks.cs`).
- Never co-locate unrelated types.

---

## Working Conventions

- Update `/docs` or ADRs for all decisions.
- No legacy shims unless required.
- Small, reviewable changes.
- Predictable defaults, clear structure, consistent naming.

---

## Quick Engineering Checklist

- Routes only in controllers.
- Remove placeholders.
- Replace literals with constants/options.
- Organize >4 classes into folders.
- One public class per file.
- Document exceptions in ADR.
- Reuse methods from `Sora.Core`, don’t duplicate.

---

## Documentation Requests

- **ADR**: `docs/decisions/<ID>-<slug>.md`, update `toc.yml`.
- **Engineering**: edit/add under `docs/engineering/`.
- **Reference**: `docs/reference/<topic>.md`.
- **Web**: `docs/api/web-http-api.md`, `docs/api/openapi-generation.md`.
- **Adapters**: update `docs/reference/_data/adapters.yml` → build generates matrix.
- **Guides**: `docs/guides/<area>/`, update local `toc.yml`.

**Samples required**:

- C# statics (`Item.All(ct)`, `Item.Query(...)`).
- HTTP controller examples with realistic payloads.
- Minimal, production-safe.

**Content pattern**:

- Start with contract block (inputs/outputs, errors).
- List 3–5 edge cases (null, large data, permissions, concurrency).
- Cross-link ADRs/canonical docs.

---

## Process Checklist

1. Pick correct doc target.
2. Register ADRs in `docs/decisions/toc.yml`.
3. Update adapters YAML (never generated files).
4. Update TOCs.
5. Run strict build (`docs:build (clean)`), fix errors.
6. Commit with conventional message (`docs(ref): …`).

---

## Guardrails

- No tutorials, quickstarts, or course-style docs (see `ARCH-0041`).
- Always use first-class statics in samples.
- Never edit `docs/reference/_generated/**`.

---

## Anchors

- Engineering: `/docs/engineering/index.md`
- Architecture: `/docs/architecture/principles.md`
- Docs root: `/docs`, `/docs/toc.yml`
- Data: `/docs/guides/data/all-query-streaming-and-pager.md`, `/docs/guides/data/working-with-entity-data.md`, `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`
- Web: `/docs/api/web-http-api.md`, `/docs/api/openapi-generation.md`, `/docs/decisions/WEB-0035-entitycontroller-transformers.md`
- Messaging: `/docs/reference/messaging.md`, `/docs/decisions/MESS-*.md`
- Adapters: `docs/reference/_data/adapters.yml` → `docs/reference/adapter-matrix.md` (generated)
