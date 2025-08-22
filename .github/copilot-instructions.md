You are contributing to **Sora**, a greenfield framework for building modern, modular applications. Sora emphasizes **simplicity** and **flexibility**, drawing inspiration from the legacy Zen framework.

#### Entry points for agents and developers

- Engineering front door: `/docs/engineering/index.md` (high-signal rules and quick links)
- Architecture principles: `/docs/architecture/principles.md` (curated ADR digest)
- Full docs root: `/docs` and `/docs/toc.yml`

Keep documentation updated with new decisions to ensure accuracy and consistency.

## Data access (priority guidance)

- Prefer first-class, static model methods for all top-level data access in samples, docs, and code suggestions:
  - Examples: `MyModel.All(ct)`, `MyModel.Query(...)`, `MyModel.AllStream(...)`, `MyModel.QueryStream(...)`, `MyModel.FirstPage(...)`, `MyModel.Page(...)`.
- Treat generic facades like `Data<TEntity, TKey>` as second-class helpers; only use them when a first-class model static is not available for the scenario.
- All/Query without paging must materialize the complete result set. Use streaming (`AllStream`/`QueryStream`) or explicit paging (`FirstPage`/`Page`) for large sets.

References:
- `/docs/guides/data/all-query-streaming-and-pager.md`
- `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`

## Core engineering concerns (mandatory)

These are default assumptions for all code. Follow them unless a documented ADR explicitly allows an exception.

0. Prime directives: Separation of Concern. DRY. Easy.

Avoid repetition; if all methods are async, names should be shorter and concise; Save(), Get(), Find().

1. Controllers, not inline endpoints

- Do: expose HTTP routes via MVC controllers (attribute-routed). Keep routing in controllers for discoverability and testability.
- Don’t: declare endpoints inline (no MapGet/MapPost/etc. in startup or module initialization).

Reference: `/docs/decisions/WEB-0035-entitycontroller-transformers.md` for payload shaping.

2. No stubs or empty artifacts

- Do: remove empty classes, placeholder files, and commented-out scaffolds.
- Don’t: add stubs “for later.” This is greenfield—only keep code that delivers value now.

3. No magic values; centralize constants

- Do: hoist magic strings/numbers into a project-scoped Constants class (for example, Infrastructure/Constants or Infrastructure/<Area>Constants).
- Prefer typed Options for tunables; use constants for stable names (headers, routes, keys, defaults) and policy literals.
- Don’t: scatter literals (headers, routes, paging sizes, media types) across the codebase.

Reference: `/docs/decisions/ARCH-0040-config-and-constants-naming.md`.

4. Project root hygiene

- Do: if a project has more than 4 classes, keep only the .csproj at the project root. Place all source files into semantically meaningful folders (e.g., Controllers, Hosting, Options, Extensions, Infrastructure, Filtering, Hooks, Attributes, etc.).
- Don’t: leave multiple .cs files at the root of busy projects.

5. Class/file layout and co-location

- Do: keep one public/top-level class per file. If a helper is a true satellite of a main type, prefer nesting it inside the main class rather than creating a separate file.
- Do: allow interfaces and attributes to share the same file only when they address the exact same concern; name the file after the concern (e.g., AuthorizationHooks.cs).
- Do: Separate concerns into different files (e.g., controllers, services, models).
- Don’t: co-locate unrelated types in the same file. Ensure filenames reflect their primary concern.

## Working conventions

Documentation first

- Refer to /docs for implementation guidance. When making decisions, update documentation (and/or ADRs) to keep it authoritative.

Clean content

- No backward-compat shims unless explicitly required. Favour a polished, cohesive design over incremental legacy support.

Feedback & collaboration

- Propose improvements and point out unclear areas. Favor small, reviewable changes that improve clarity and usability.

Developer experience

- Minimize friction. Prefer predictable defaults, clear folder structures, and consistent naming so developers can onboard quickly.

Clarity & design

- Prioritize simple, intuitive constructs that are easy to adopt and extend.

## How to apply (quick checklist)

- Add or modify HTTP routes in controllers only.
- Before committing, remove any empty/placeholder files.
- Replace literals with constants or options; if constant, add to a central Constants class for the project.
- If a project grows beyond 4 classes, move .cs files into folders; keep only the .csproj at the root.
- Keep one public class per file; nest satellites; co-locate interfaces/attributes only when they share a single concern and name the file accordingly.
- If an exception is needed, add a short ADR or decision note in /docs/decisions with rationale and scope.
- Do not implement bespoke methods if a similar one is already present in a core library (Sora.Core, Sora.Dat.Core, etc.)

See also:
- Engineering: `/docs/engineering/index.md`
- Architecture: `/docs/architecture/principles.md`

## Doc requests: translate “document …” into concrete edits

When a user asks to “document X”, choose the right target(s) and produce instruction-first content (no tutorials). Use this routing guide and checklist.

Routing map (what to create/edit)
- Architecture decision (ADR): when the ask is a policy, tradeoff, or framework-wide behavior.
  - Create `docs/decisions/<ID>-<slug>.md` with front-matter (id, slug, domain, status, date, title).
  - Sections: Context → Decision → Scope → Consequences → Implementation notes → Follow-ups → References.
  - Add to `docs/decisions/toc.yml` in the proper domain group.
- Engineering guidance: rules and guardrails for developers.
  - Edit `docs/engineering/index.md` or add a focused page under `docs/engineering/` and link it from the index.
- Reference (canonical how-to + API-centric): default for features, modules, adapters.
  - Data/Web/Messaging/Vector: prefer updating/creating under `docs/reference/` (e.g., `reference/messaging.md`, `reference/data-access.md`).
  - Web HTTP conventions: `docs/api/web-http-api.md`, `docs/api/openapi-generation.md`, `docs/api/well-known-endpoints.md`.
  - Adapters: update capabilities/guardrails in `docs/reference/_data/adapters.yml` (single source). The matrix is generated to `docs/reference/_generated/adapter-matrix.md` at build time—don’t hand edit generated files.
- Guides (concise, instructional, API-anchored): place under `docs/guides/<area>/` only when it maps 1:1 to stable APIs (no narratives/quickstarts).
  - Update the local `toc.yml`.

Always include developer samples (when applicable)
- Add an Examples section with minimal, runnable snippets:
  - C# first-class model statics for data access: `Item.All(ct)`, `Item.Query(...)`, `Item.FirstPage(...)`.
  - HTTP examples for controllers/headers with realistic payloads.
  - Link to sample apps in `samples/` when deeper context helps (keep links minimal and stable), e.g., `samples/S2.Api/`.
- Keep examples short and production-safe (headers, paging, error handling cues). No multi-part tutorials.

Content patterns to apply
- Lead with a short “contract” block: Inputs/Outputs, options, error modes, success criteria.
- List 3–5 edge cases (null/empty, large/slow, auth/permission, concurrency/timeouts).
- Hoist literals into constants/options and link relevant ADRs.
- Cross-link canonical pages (Engineering front door, Architecture principles, Decisions).

Process checklist (green-before-done)
1) Pick targets using the Routing map; create/edit files accordingly.
2) If adding an ADR, register it in `docs/decisions/toc.yml` under the correct domain.
3) If touching adapters, update `docs/reference/_data/adapters.yml`; let the build generate the matrix.
4) Update any affected TOCs (`docs/toc.yml`, per-folder `toc.yml`).
5) Run strict docs build (Task: docs:build (clean)) and fix broken links.
6) Commit with a conventional message, e.g., `docs(ref): web pagination headers with examples` or `docs(adr): ARCH-00xx <title>`.

Posture guardrails (enforced)
- No tutorials, quickstarts, or course-style flows. Keep docs instructional and reference-focused as per ADR ARCH-0041.
- Prefer first-class model statics over generic facades in samples.
- Don’t hand-edit `docs/reference/_generated/**`.

Handy anchors
- Data access patterns: `/docs/guides/data/all-query-streaming-and-pager.md`, `/docs/guides/data/working-with-entity-data.md`, `/docs/decisions/DATA-0061-data-access-pagination-and-streaming.md`.
- Web API conventions: `/docs/api/web-http-api.md`, `/docs/api/openapi-generation.md`, `/docs/decisions/WEB-0035-entitycontroller-transformers.md`.
- Messaging basics: `docs/reference/messaging.md` (create if missing) and decisions under `docs/decisions/MESS-*.md`.
- Adapter matrix source: `docs/reference/_data/adapters.yml` → generated matrix include at `docs/reference/adapter-matrix.md`.
