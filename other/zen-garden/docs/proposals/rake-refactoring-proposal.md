# Rake CLI Refactoring Proposal

**Status:** Proposed
**Date:** 2026-01-22
**Author:** Claude (via conversation)

## Executive Summary

The Rake CLI client has grown to **4,820 lines in main.rs** with 28 command handlers implemented inline in a single match statement. This proposal applies the same SoC/KISS/YAGNI/DRY principles that successfully refactored Moss (from 3,976 to 54 lines) to create a maintainable, ergonomic CLI architecture.

**Goal:** Reduce main.rs from 4,820 lines to ~150 lines while improving developer ergonomics.

---

## Current State Analysis

### File Structure
```
src/rake/src/
├── main.rs               4,820 lines  ← PROBLEM: monolithic
├── command_manifest.rs   1,455 lines  (declarative, well-structured)
├── ui.rs                   740 lines  (good abstractions, underutilized)
├── discovery.rs            565 lines  (functional)
├── parser.rs               377 lines  (zen syntax, functional)
├── client.rs               159 lines  (endpoint resolution)
├── tending.rs              133 lines  (cache persistence)
├── stone_cache.rs          132 lines  (in-memory cache)
├── suggestions.rs           62 lines  (command suggestions)
├── commands/
│   ├── mod.rs               13 lines
│   └── help.rs             160 lines
└── lib.rs                    9 lines  (minimal re-exports)

Total: ~8,800 lines
```

### Key Problems

#### 1. Monolithic main.rs (4,820 lines)
- 28 command handlers as case arms in giant match statement
- Each handler: 20-210 lines of inline code
- Helper functions scattered throughout
- Difficult to navigate, test, or modify individual commands

#### 2. Repetitive Patterns
```rust
// This pattern appears 28 times:
let endpoint = resolve_endpoint(&client, at).await?;
print_stone_header(&client, &endpoint).await;

// JSON parsing chains appear 32+ times:
body.get("data").and_then(|d| d.as_array())
```

#### 3. Underutilized Abstractions
- `OutputWriter` exists in ui.rs but marked `#[allow(dead_code)]`
- Handlers use raw `println!()` with manual formatting instead
- No command trait or handler abstraction

#### 4. No Handler Extraction
- commands/ directory exists but only contains help.rs
- No lifecycle/, adoption/, discovery/ subdirectories
- All business logic lives in main.rs match arms

---

## Proposed Architecture

### Target Structure
```
src/rake/src/
├── main.rs                 ~150 lines  (entry point + setup only)
├── lib.rs                  ~100 lines  (public API + re-exports)
├── dispatch.rs             ~200 lines  (command router + middleware)
├── context.rs              ~100 lines  (CommandContext, shared state)
│
├── commands/
│   ├── mod.rs              ~80 lines   (Command trait + registry)
│   ├── help.rs             160 lines   (unchanged)
│   │
│   ├── discovery/
│   │   ├── mod.rs          ~30 lines
│   │   ├── observe.rs      ~80 lines
│   │   ├── watch.rs        ~100 lines
│   │   ├── list.rs         ~60 lines
│   │   └── status.rs       ~220 lines  (largest due to formatting)
│   │
│   ├── lifecycle/
│   │   ├── mod.rs          ~30 lines
│   │   ├── offer.rs        ~250 lines  (largest command)
│   │   ├── rest.rs         ~50 lines
│   │   ├── wake.rs         ~50 lines
│   │   ├── remove.rs       ~70 lines
│   │   └── upgrade.rs      ~120 lines
│   │
│   ├── adoption/
│   │   ├── mod.rs          ~30 lines
│   │   ├── adopt.rs        ~60 lines
│   │   ├── release.rs      ~40 lines
│   │   ├── borrow.rs       ~60 lines
│   │   ├── return_.rs      ~40 lines   (return is keyword)
│   │   └── find.rs         ~80 lines
│   │
│   ├── management/
│   │   ├── mod.rs          ~30 lines
│   │   ├── tend.rs         ~60 lines
│   │   ├── reconcile.rs    ~50 lines
│   │   └── refresh.rs      ~40 lines
│   │
│   └── pond/
│       ├── mod.rs          ~30 lines
│       ├── place.rs        ~70 lines
│       └── invite.rs       ~40 lines
│
├── api/
│   ├── mod.rs              ~50 lines
│   ├── client.rs           ~200 lines  (HTTP client wrapper)
│   └── responses.rs        ~100 lines  (typed response parsing)
│
├── ui/
│   ├── mod.rs              ~50 lines   (re-exports)
│   ├── terminal.rs         ~80 lines   (TerminalInfo, from ui.rs)
│   ├── output.rs           ~150 lines  (OutputWriter, enhanced)
│   ├── tables.rs           ~200 lines  (TableBuilder, CategoryGrid)
│   ├── formatting.rs       ~200 lines  (status_indicator, kv_line, etc.)
│   └── constants.rs        ~30 lines
│
├── discovery/              (unchanged, functional)
│   ├── mod.rs
│   ├── udp.rs
│   └── mdns.rs
│
├── cache/
│   ├── mod.rs              ~30 lines
│   ├── tending.rs          133 lines   (unchanged)
│   └── stone.rs            132 lines   (unchanged)
│
├── parser.rs               377 lines   (unchanged, functional)
├── command_manifest.rs     1,455 lines (unchanged, declarative)
└── suggestions.rs          62 lines    (unchanged)
```

### Estimated Line Counts

| Directory | Current | Target | Change |
|-----------|---------|--------|--------|
| main.rs | 4,820 | ~150 | -97% |
| commands/ | 173 | ~1,500 | handlers extracted |
| api/ | 0 | ~350 | HTTP abstraction |
| ui/ | 740 | ~710 | split into modules |
| discovery/ | 565 | ~565 | unchanged |
| cache/ | 265 | ~295 | reorganized |
| Other | 2,243 | ~2,243 | unchanged |
| **Total** | ~8,800 | ~5,800 | -34% |

Note: Total lines decrease because inline duplication is eliminated.

---

## Key Design Decisions

### 1. Command Trait Pattern

```rust
// commands/mod.rs
pub struct CommandContext<'a> {
    pub client: &'a reqwest::Client,
    pub endpoint: String,
    pub stone_name: String,
    pub quiet_mode: bool,
    pub output: OutputWriter,
}

#[async_trait]
pub trait Command {
    /// Execute the command
    async fn execute(&self, ctx: &CommandContext<'_>) -> anyhow::Result<()>;

    /// Whether this command requires endpoint resolution (default: true)
    fn requires_endpoint(&self) -> bool { true }

    /// Whether to print stone header (default: true if requires_endpoint)
    fn show_stone_header(&self) -> bool { self.requires_endpoint() }
}
```

### 2. Dispatcher with Middleware

```rust
// dispatch.rs
pub async fn dispatch(cli: Cli, client: &reqwest::Client) -> anyhow::Result<()> {
    // Pre-dispatch: resolve endpoint if needed
    let ctx = if command.requires_endpoint() {
        let endpoint = resolve_endpoint(client, cli.at).await?;
        if command.show_stone_header() {
            print_stone_header(client, &endpoint).await;
        }
        CommandContext::with_endpoint(client, endpoint, cli.quiet)
    } else {
        CommandContext::without_endpoint(client, cli.quiet)
    };

    // Dispatch
    command.execute(&ctx).await?;

    // Post-dispatch: suggestions if not quiet
    if !ctx.quiet_mode {
        suggestions::print_suggestions(command.name(), false);
    }

    Ok(())
}
```

### 3. Enhanced OutputWriter (Remove dead_code)

```rust
// ui/output.rs - actively use in all handlers
impl OutputWriter {
    // Existing methods made active
    pub fn success(&self, msg: impl Display);
    pub fn error(&self, msg: impl Display);
    pub fn info(&self, msg: impl Display);
    pub fn warn(&self, msg: impl Display);

    // New convenience methods
    pub fn service_action(&self, action: &str, service: &str, status: &str);
    pub fn api_error(&self, status: StatusCode, context: &str);
    pub fn not_found(&self, entity: &str, name: &str);
}
```

### 4. Typed API Response Parsing

```rust
// api/responses.rs
pub fn extract_data<T: DeserializeOwned>(body: &Value) -> Option<T> {
    body.get("data")
        .and_then(|d| serde_json::from_value(d.clone()).ok())
}

pub fn extract_array(body: &Value) -> Option<&Vec<Value>> {
    body.get("data").and_then(|d| d.as_array())
        .or_else(|| body.as_array())
}

pub fn extract_string(body: &Value, key: &str) -> Option<&str> {
    body.get(key).and_then(|v| v.as_str())
}
```

---

## Implementation Phases

### Phase 1: Foundation (Low Risk)
1. Create `context.rs` with CommandContext struct
2. Create `api/` directory with HTTP client wrapper
3. Create `api/responses.rs` with typed parsing helpers
4. Split `ui.rs` into `ui/` subdirectory (no logic changes)

### Phase 2: Command Trait (Medium Risk)
1. Define Command trait in `commands/mod.rs`
2. Create `dispatch.rs` with middleware pattern
3. Extract **one command** (e.g., `list`) as proof of concept
4. Verify build + tests pass

### Phase 3: Bulk Extraction (Low Risk per Command)
Extract commands by category:
1. Discovery: observe, watch, list, status
2. Lifecycle: offer, rest, wake, remove, upgrade
3. Adoption: adopt, release, borrow, return, find
4. Management: tend, reconcile, refresh
5. Pond: place, invite

### Phase 4: Cleanup
1. Remove dead code from main.rs
2. Update lib.rs with proper re-exports
3. Remove `#[allow(dead_code)]` from OutputWriter
4. Final main.rs should be ~150 lines

---

## What NOT to Change

Following YAGNI:

1. **command_manifest.rs** - Already well-structured, declarative
2. **parser.rs** - Zen syntax parsing works correctly
3. **discovery.rs** - UDP/mDNS logic is functional
4. **tending.rs / stone_cache.rs** - Cache logic is correct
5. **suggestions.rs** - Simple and effective

These modules follow SoC and don't need restructuring.

---

## Success Criteria

| Metric | Current | Target |
|--------|---------|--------|
| main.rs lines | 4,820 | <200 |
| Largest file | 4,820 (main.rs) | <400 (offer.rs) |
| Commands testable in isolation | 0 | 28 |
| Code duplication instances | ~60 | <10 |
| OutputWriter usage | 0% | 100% |

---

## Risk Assessment

| Risk | Mitigation |
|------|------------|
| Breaking CLI behavior | Extract one command at a time, test each |
| Zen syntax regression | parser.rs unchanged, integration tests |
| Performance impact | No additional allocations, same async patterns |
| Build time increase | Minimal - same total code, better organization |

---

## Comparison to Moss Refactoring

| Aspect | Moss | Rake |
|--------|------|------|
| Starting main.rs | 3,976 lines | 4,820 lines |
| Target main.rs | 54 lines | ~150 lines |
| Reduction | 99% | 97% |
| Handler extraction | bootstrap/, tasks/ | commands/category/ |
| Trait pattern | AppState DI | Command trait |
| Test improvement | 37 → 141 | TBD |

---

## Conclusion

The Rake refactoring follows proven patterns from the Moss refactoring:
- Extract handlers to focused modules
- Eliminate duplication through abstractions
- Keep working code unchanged (parser, discovery, manifest)
- Improve testability without changing behavior

**Recommendation:** Approve and proceed with Phase 1.
