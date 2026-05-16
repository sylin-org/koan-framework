# Exploration Phase

Before implementing anything for this task, complete the following steps in order. Do not write any production code until all steps are done.

## Step 1: Understand the task

Restate the task in your own words. Identify:
- What concern does this touch? (adapters, core, protocol, commands, platform, config)
- Which layer is involved? (transport, business logic, wire format, CLI)
- What's the expected output? (new feature, refactor, bug fix, extension)

## Step 2: Read existing code

Open and read the **3-5 most relevant existing files**. Use these searches to find them:

```bash
# Find types related to the task
rg "struct|enum|trait" src/ -l | head -20

# Find functions related to the task
rg "fn keyword_from_task" src/

# Find the closest existing implementation to what we're building
rg "similar_feature_keyword" src/ -l
```

For each file you read, state in one sentence what it does and whether it's relevant.

## Step 3: Check for existing constants & types

Run these searches explicitly and report the results:

```bash
# Constants that might already exist
rg "const " src/core/mod.rs
rg "const " src/adapters/
rg "const " src/main.rs
rg "const " src/config.rs

# Protocol types that might already exist
rg "struct|enum" src/protocol/mod.rs
rg "struct|enum" src/protocol/request.rs
rg "struct|enum" src/protocol/response.rs

# Core types that might already exist
rg "struct|enum" src/core/mod.rs
rg "struct|enum" src/core/registry.rs
```

Also check `.agentic/reference/utilities.md` for the full constants & types catalog.

State clearly: **"Already exists"** or **"Needs to be created"** for each piece of functionality the task requires.

## Step 4: Identify the closest pattern to follow

Find the most similar existing feature in the codebase. For example:
- New HTTP endpoint → read an existing handler in `src/adapters/http.rs`
- New CLI command → read an existing command in `src/commands/standalone.rs` or `src/commands/client.rs`
- New protocol type → read `src/protocol/mod.rs`
- New core operation → read `src/core/mod.rs`
- New admin command → read `src/admin.rs`
- Platform integration → read `src/platform/`

State: **"Following the pattern from [specific file]"**

## Step 5: Plan where new code will live

For every new file, type, function, or constant, state its location and justify it:

| New code | Location | Justification |
|----------|----------|---------------|
| (type/fn/const) | (exact path) | (why here and not elsewhere) |

Apply the layer placement rules:
- Wire format types? → `src/protocol/`
- Business logic? → `src/core/`
- Transport handling? → `src/adapters/`
- CLI command? → `src/commands/`
- OS integration? → `src/platform/`
- Formatting? → `src/format.rs`

## Step 6: Check for potential violations

Before proceeding, confirm:

- [ ] No mdns-sd imports outside `core/daemon.rs`
- [ ] No new constant duplicates one in `.agentic/reference/utilities.md`
- [ ] No new type duplicates one in `protocol/` or `core/`
- [ ] Constants are co-located with usage (not in a centralized module)
- [ ] New protocol types have serde round-trip tests planned

## Step 7: Present the plan

Summarize your findings in this format:

**Task:** (one sentence)
**Files read:** (list with one-sentence relevance notes)
**Reusing:** (list what already exists)
**Creating new:** (table from Step 5)
**Pattern:** (which existing file you're following)
**Risks:** (anything you're unsure about)

**Then stop and wait for approval before implementing.**
