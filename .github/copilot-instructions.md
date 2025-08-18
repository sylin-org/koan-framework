You are contributing to **Sora**, a greenfield framework for building modern, modular applications. Sora emphasizes **simplicity** and **flexibility**, drawing inspiration from the legacy Zen framework.

#### Guidelines

* **Documentation First**
  Refer to `/docs` when looking for implementation guidance. Keep documentation updated with new decisions to ensure accuracy and consistency.

You are contributing to Sora, a greenfield framework for building modern, modular applications. Sora emphasizes simplicity and flexibility, drawing inspiration from the legacy Zen framework.

## Core engineering concerns (mandatory)

These are default assumptions for all code. Follow them unless a documented ADR explicitly allows an exception.

1) Controllers, not inline endpoints
- Do: expose HTTP routes via MVC controllers (attribute-routed). Keep routing in controllers for discoverability and testability.
- Don’t: declare endpoints inline (no MapGet/MapPost/etc. in startup or module initialization).

2) No stubs or empty artifacts
- Do: remove empty classes, placeholder files, and commented-out scaffolds.
- Don’t: add stubs “for later.” This is greenfield—only keep code that delivers value now.

3) No magic values; centralize constants
- Do: hoist magic strings/numbers into a project-scoped Constants class (for example, Infrastructure/Constants or Infrastructure/<Area>Constants).
- Prefer typed Options for tunables; use constants for stable names (headers, routes, keys, defaults) and policy literals.
- Don’t: scatter literals (headers, routes, paging sizes, media types) across the codebase.

4) Project root hygiene
- Do: if a project has more than 4 classes, keep only the .csproj at the project root. Place all source files into semantically meaningful folders (e.g., Controllers, Hosting, Options, Extensions, Infrastructure, Filtering, Hooks, Attributes, etc.).
- Don’t: leave multiple .cs files at the root of busy projects.

5) Class/file layout and co-location
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

If an exception is needed, add a short ADR or decision note in /docs/decisions with rationale and scope.