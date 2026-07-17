---
name: explore
description: Run a mandatory pre-implementation exploration workflow before writing production code in Koan (.NET/C#). Use when a task requires code changes and Codex must first map concerns/layers, read relevant files and docs, check existing constants and types, identify the closest existing pattern, plan exact code placement, and confirm architectural guardrails.
---

# Explore

Before implementing anything for a task, complete the following steps in order.
Do not write production code until all steps are done.

## Global constraints (must hold)

- Entity-first data access: prefer `Entity<T>` statics; avoid repositories and `Data<TEntity,TKey>` unless no static exists.
- Controllers-only HTTP: no inline endpoints (`MapGet`/`MapPost`/etc) unless an ADR explicitly allows it.
- No magic literals: stable identifiers go in `Infrastructure/Constants`; tunables are typed `*Options`.
- Docs posture: instruction-first (no tutorials); update ADRs and TOCs when behavior/policy changes.
- Per-project docs: `README.md` + `TECHNICAL.md` at project roots for reusable modules.
- Large data paths: use capability-qualified streaming or explicit paging. InMemory, JSON, and Redis
  reject `AllStream`/`QueryStream`; see DATA-0107.

## Step 1: Understand the task

Restate the task in your own words. Identify:
- What concern this touches: data access, web API, messaging, hosting, adapters, tooling, docs
- Which layer is involved: controllers, core logic, options/config, storage, adapters
- Expected output: new feature, refactor, bug fix, doc update

For application-facing capability work, state before internal design:

- the user's business sentence;
- the smallest honest C# expression and complete user action surface—references, code, decorations,
  configuration, context, and runtime prerequisites—of it;
- the guarantee that expression creates and the corrective failure when it cannot be met; and
- every additional public concept and why the business or guarantee requires it.

Use `AddKoan()` / `Entity<T>` / `EntityController<T>` as the golden business-to-code comparison. Exact
compactness is not always possible, but framework assembly machinery must not leak into the common path.
Minimize distinct concepts and cognitive branches, not physical lines or tokens; hidden prerequisites count.

## Step 2: Map the project and read core docs

Open and read the 3-5 most relevant documentation files and entry points.
Start with:

- docs/engineering/index.md
- docs/architecture/principles.md
- docs/toc.yml
- README.md
- samples/CATALOG.md

If the task touches specific areas, add the relevant ADR or guide, for example:

- docs/decisions/DATA-0107-provider-bounded-entity-streams.md
- docs/decisions/WEB-0035-entitycontroller-transformers.md
- docs/guides/data/entity-access-and-streaming.md
- docs/reference/web/http-api.md

For each file read, state in one sentence what it establishes and whether it is relevant.

## Step 3: Read existing code

Open and read the 3-5 most relevant existing source files.
Use searches like:

```bash
# Find existing controllers and HTTP patterns
rg "class .*Controller" src/ samples/ -g"*.cs"
rg "\[ApiController\]|\[Route\(" src/ samples/ -g"*.cs"

# Find data access patterns (prefer model statics)
rg "\.All\(|\.Query\(|\.AllStream\(|\.QueryStream\(|\.FirstPage\(|\.Page\(" src/ samples/ -g"*.cs"

# Find options and constants
rg "class .*Options|record .*Options" src/ samples/ -g"*.cs"
rg "class Constants|static class Constants" src/ samples/ -g"*.cs"

# Find similar domain types
rg "class|record|interface" src/ samples/ -g"*.cs"
```

For each file read, state in one sentence what it does and whether it is relevant.

## Step 4: Check for existing constants, options, and shared types

Run these searches explicitly and report results:

```bash
# Constants that might already exist
rg "\bconst\b" src/ samples/ -g"*.cs"
rg "\bConstants\b" src/ samples/ -g"*.cs"

# Options types that might already exist
rg "\bOptions\b" src/ samples/ -g"*.cs"

# Shared DTOs or contracts
rg "record|class .*Dto|class .*Request|class .*Response" src/ samples/ -g"*.cs"
```

For each required piece of functionality, state clearly:
- `Already exists`
- `Needs to be created`

## Step 5: Interrogate the closest pattern and coalescence boundary

Find the most similar existing feature in the codebase.
Examples:
- New HTTP endpoint: read an existing `*Controller.cs` in `src/` or `samples/`
- New data access: read a model using `All`, `Query`, or paging statics
- New options/config: read a feature using `*Options`
- New adapter: read the closest adapter in `src/` or `samples/`

The closest pattern is evidence, not authority. Compare it with sibling mechanisms and state:

- `Closest pattern: [specific file]`
- current decision owner, consumers, state lifetime, and hot-path cost;
- repeated mechanics elsewhere;
- chosen specificity: framework law, capability family, pillar, adapter, or application;
- disposition: `keep`, `absorb`, `rebuild`, or `delete`;
- the one target owner and why the next wider/narrower owner is wrong;
- human readability, IntelliSense discovery, and coding-model legibility; and
- superseded paths that the slice must delete.

Do not preserve a pattern merely because it already exists. Do not lift it merely because names look
similar. Shared machinery is permitted only where meaning and lifecycle are identical.

## Step 6: Plan where new code will live

For every new file, type, method, or constant, state location and justification:

| New code | Location | Justification |
|----------|----------|---------------|
| (type/method/const) | (exact path) | (why here and not elsewhere) |

Apply placement rules:
- HTTP routes only in controllers with attribute routing
- Core logic in `src/` feature folders, not in controllers
- Options in `*Options` types near the feature or hosting config
- Constants centralized in a project-scoped `Constants` class
- One public/top-level class per file
- If the project exceeds four classes, keep sources in folders (only `.csproj` at root)

## Step 7: Check for potential violations

Before proceeding, confirm:

- [ ] No inline endpoints (`MapGet`/`MapPost` etc); all routes are controllers
- [ ] If inline endpoints are observed in the area, note the exception and cite the ADR or existing pattern
- [ ] No empty placeholder classes or commented-out scaffolds
- [ ] No magic literals; constants are centralized and typed options are used
- [ ] First-class model statics are used for data access when available
- [ ] Large data access uses a capability-qualified stream (`AllStream`, `QueryStream`) or explicit paging; InMemory/JSON/Redis do not currently stream
- [ ] Docs/ADRs/TOCs will be updated when behavior or policy changes

## Step 8: Present the plan

Summarize findings in this exact format:

**Task:** (one sentence)
**Application intent:** (the user's business sentence)
**Public expression:** (smallest honest C# plus the complete references/decorations/config/context/runtime surface)
**Guarantee/correction:** (what becomes guaranteed and the safe corrective failure)
**Complete intent surface:** (all required user actions; explicitly say when none exist beyond the expression)
**Public concepts:** (each visible concept mapped to its business decision or guarantee)
**Docs read:** (list with one-sentence relevance notes)
**Code read:** (list with one-sentence relevance notes)
**Reusing:** (list what already exists)
**Creating new:** (table from Step 6)
**Coalescence:** (closest pattern, specificity, keep/absorb/rebuild/delete, one target owner, deletions)
**Ergonomics:** (human readability, IntelliSense discovery, coding-model legibility, cognitive branches)
**Constraints satisfied:** (bullet list of the global constraints above, mark any exception)
**Risks:** (anything you're unsure about)

The active work card or ADR must contain the application intent, complete expression, guarantee,
coalescence decision, and ergonomics evidence before production edits. Then stop and wait for approval
before implementing unless the user has already granted explicit standing authorization for this scope.
