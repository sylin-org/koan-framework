# Zen Garden v2: Dual-Syntax CLI/API Implementation Plan

**Status:** Ready to Execute  
**Target:** Phase 1-3 (Core API + CLI + Pond Structure)  
**Timeline:** Iterative, test-driven  
**Reference:** `/docs/proposals/CLI-API-TAXONOMY-V2-FINAL.md`

---

## Current State Analysis

### Moss (Daemon)
- **Framework:** Axum HTTP server
- **Current Routes:** `/api/operations/*`, `/api/services/*`, `/api/offerings/*`
- **Architecture:** AppState with DockerManager, TemplateLoader, event broadcasting
- **File:** `src/moss/src/main.rs` (3606 lines)
- **Status:** Functional but needs restructuring for v1 versioning

### Rake (CLI)
- **Framework:** Clap derive
- **Current Commands:** Subcommand enum (`Status`, `Offer`, `Remove`, `Upgrade`, `Stop`, `Start`, `List`)
- **Discovery:** UDP broadcast auto-discovery, stone caching (hot cache architecture)
- **Tending:** Already implemented (90s TTL, file-based cache)
- **File:** `src/rake/src/main.rs` (2326 lines)
- **Status:** Needs dual-syntax parser + zen verbs

### Lantern (Registry)
- **Framework:** Axum HTTP server
- **File:** `src/lantern/src/main.rs`
- **Status:** Needs `/api/v1/garden/*` routes for multi-stone topology

---

## Phase 1: Core API Restructuring (Moss + Lantern)

### 1.1 Moss API Versioning & Route Consolidation

**Goal:** Migrate from `/api/operations/*` to `/api/v1/*` with zen sub-resource actions

**Tasks:**
1. Create new route structure:
   ```rust
   // V1 API routes
   .route("/api/v1/services", get(list_services_v1))
   .route("/api/v1/services/:service", get(get_service_v1))
   .route("/api/v1/services", post(create_service_v1))       // Replaces /api/operations/offer
   .route("/api/v1/services/:service/rest", post(rest_service_v1))    // Zen sub-resource
   .route("/api/v1/services/:service/wake", post(wake_service_v1))    // Zen sub-resource
   .route("/api/v1/services/:service/nourish", post(nourish_service_v1))  // Zen sub-resource
   .route("/api/v1/services/:service", delete(delete_service_v1))     // Replaces /api/operations/remove
   .route("/api/v1/services/:service/logs", get(stream_logs_v1))
   
   // Garden/stone routes
   .route("/api/v1/garden", get(get_garden_v1))               // All stones
   .route("/api/v1/garden/stones/:stone_name", get(get_stone_v1))
   .route("/api/v1/stone", get(get_local_stone_v1))           // Consolidated capabilities + metrics
   
   // Offerings catalog
   .route("/api/v1/offerings", get(list_offerings_v1))
   .route("/api/v1/offerings/:name", get(get_offering_info_v1))
   .route("/api/v1/offerings/_refresh", post(refresh_offerings_v1))  // Admin op
   
   // Events
   .route("/api/v1/events", get(stream_events_v1))
   
   // System admin
   .route("/api/v1/system/reconcile", post(reconcile_v1))
   .route("/api/v1/system/refresh", post(refresh_v1))
   ```

2. Keep legacy routes (backwards compat):
   ```rust
   // Legacy routes (redirect or deprecation warnings)
   .route("/api/operations/offer/:offering", post(offer_service_legacy))
   .route("/api/operations/remove/:target", post(remove_service_legacy))
   // etc.
   ```

3. Consolidate `/api/v1/stone` endpoint:
   - Merge `/capabilities` + `/metrics` + `/health` into single comprehensive response
   - Return `RuntimeInfo` with capabilities, metrics, health status

4. Add versionless route handling:
   ```rust
   // Versionless routes respond with v1, add X-Api-Version header
   .route("/api/services", get(list_services_versionless))
   ```

**Files to Modify:**
- `src/moss/src/main.rs` (router definition)
- Create `src/moss/src/api/v1/` module structure
- Create `src/moss/src/api/responses.rs` for response types

**Testing:**
- Unit tests for each v1 endpoint
- Integration test: v1 vs legacy routes produce equivalent results
- Test versionless routes return v1 + header

---

### 1.2 Suggestions Payload Structure

**Goal:** Add `suggestions: Option<Vec<String>>` to all API responses

**Response Structure:**
```rust
#[derive(Serialize)]
struct ApiResponse<T> {
    #[serde(flatten)]
    data: T,
    #[serde(skip_serializing_if = "Option::is_none")]
    suggestions: Option<Vec<String>>,
}
```

**Suggestion Logic:**
- Context-aware based on operation (create → observe, stop → start, etc.)
- State-driven (degraded service → observe details, no services → explore)
- Max 3-4 suggestions per response
- Empty if `X-Quiet` header present

**Implementation:**
1. Create `src/moss/src/suggestions.rs` module
2. Add `SuggestionEngine` trait:
   ```rust
   trait SuggestionEngine {
       fn generate_suggestions(&self, context: &OperationContext) -> Vec<String>;
   }
   ```
3. Integrate into all v1 handlers

**Files to Create:**
- `src/moss/src/api/suggestions.rs`

**Testing:**
- Test suggestion generation for each operation type
- Test quiet mode suppresses suggestions
- Test suggestion count limits (max 4)

---

### 1.3 Lantern Garden Topology API

**Goal:** Multi-stone discovery and topology aggregation

**New Routes:**
```rust
// Lantern routes
.route("/api/v1/garden", get(get_garden_topology))
.route("/api/v1/garden/events", get(stream_garden_events))  // SSE
.route("/api/v1/peers", get(discover_peers))
```

**Response Structure:**
```rust
#[derive(Serialize)]
struct GardenTopology {
    stones: Vec<StoneInfo>,
    total_services: u32,
    healthy_stones: u32,
    degraded_stones: u32,
    pond_status: Option<PondStatus>,  // For later Phase 3
}

#[derive(Serialize)]
struct StoneInfo {
    name: String,
    endpoint: String,
    health: String,
    services_count: u32,
    uptime_seconds: u64,
}
```

**Discovery Mechanism:**
- Lantern maintains registry of stones (from mDNS/UDP announcements)
- Polls `/api/v1/stone` from each known stone periodically
- Aggregates into unified garden view
- Broadcasts topology changes via SSE

**Files to Modify:**
- `src/lantern/src/main.rs` (add routes)
- `src/lantern/src/registry.rs` (add topology aggregation logic)

**Testing:**
- Test topology with 0, 1, 3+ stones
- Test SSE stream receives updates
- Test stone health status propagation

---

## Phase 2: Rake CLI Dual Syntax

### 2.1 Zen Verb Parser with Positional Keywords

**Goal:** Parse `garden-rake offer mongo at stone-02` without `--at` flags

**Strategy:** Pre-parse positional keywords before Clap

**Implementation:**
1. Create `src/rake/src/parser.rs`:
   ```rust
   pub enum ParsedCommand {
       Zen(ZenCommand),
       Normative(NormativeCommand),
   }
   
   pub struct ZenCommand {
       pub verb: ZenVerb,
       pub target: Option<String>,  // service name
       pub at: Option<String>,       // stone name/endpoint
       pub quietly: bool,
       pub until: Option<String>,    // condition for watch
   }
   
   pub enum ZenVerb {
       Offer, Rest, Wake, Nourish, Release,
       Observe, Watch, Touch, Tend,
       Place, Lift, Invite, Explore, Garden
   }
   ```

2. Positional keyword detection:
   ```rust
   fn extract_at_keyword(args: &[String]) -> (Vec<String>, Option<String>) {
       // Find "at" keyword, extract following arg
       // Return (args_without_at, at_value)
   }
   
   fn extract_quietly_keyword(args: &[String]) -> (Vec<String>, bool) {
       // Find "quietly" keyword at end
       // Return (args_without_quietly, is_quiet)
   }
   
   fn extract_until_keyword(args: &[String]) -> (Vec<String>, Option<String>) {
       // Find "until" keyword, extract following arg
       // Return (args_without_until, until_condition)
   }
   ```

3. Two-phase parsing:
   ```rust
   fn parse_command(args: Vec<String>) -> ParsedCommand {
       // Phase 1: Detect zen vs normative
       let first = &args[0];
       if is_zen_verb(first) {
           // Phase 2a: Extract positional keywords
           let (args, at) = extract_at_keyword(&args);
           let (args, quietly) = extract_quietly_keyword(&args);
           let (args, until) = extract_until_keyword(&args);
           
           ParsedCommand::Zen(ZenCommand {
               verb: parse_zen_verb(first),
               target: args.get(1).cloned(),
               at,
               quietly,
               until,
           })
       } else {
           // Phase 2b: Standard Clap parsing
           ParsedCommand::Normative(parse_with_clap(&args))
       }
   }
   ```

**Files to Create:**
- `src/rake/src/parser.rs`
- `src/rake/src/zen.rs` (zen verb definitions)

**Files to Modify:**
- `src/rake/src/main.rs` (use new parser before Clap)

**Testing:**
- Test `offer mongo at stone-02` → extracts "at stone-02"
- Test `watch redis until 'ready' quietly` → extracts both
- Test `offer mongo at stone-02 --verbose` → rejects mixed syntax
- Test `services create mongo --at stone-02` → uses Clap normally

---

### 2.2 Quiet Mode Support

**Goal:** Suppress suggestions via flags or keyword

**Implementation:**
1. Detect quiet mode:
   ```rust
   struct QuietMode {
       enabled: bool,
   }
   
   impl QuietMode {
       fn from_context(cli: &Cli, env: &Environment) -> Self {
           let enabled = cli.quiet 
               || cli.succinct
               || env.var("GARDEN_QUIET").is_ok()
               || zen_command.quietly;
           QuietMode { enabled }
       }
   }
   ```

2. Add `X-Quiet: true` header to requests when quiet mode active

3. Suppress suggestion rendering in CLI output

**Files to Modify:**
- `src/rake/src/main.rs` (add quiet mode detection)
- `src/rake/src/client.rs` (add X-Quiet header)
- `src/rake/src/output.rs` (skip suggestions if quiet)

**Testing:**
- Test `--quiet` flag suppresses suggestions
- Test `quietly` keyword suppresses suggestions
- Test `GARDEN_QUIET=1` env var suppresses suggestions
- Test suggestions still generated by API (just not rendered)

---

### 2.3 Context/Tend Commands

**Goal:** Zen `tend` + normative `context` commands for stone focus

**Implementation:**
1. Add commands to Clap:
   ```rust
   #[derive(Subcommand)]
   enum Commands {
       // Zen
       Tend {
           /// Stone to tend to (omit to show current)
           stone: Option<String>,
       },
       
       // Normative
       Context {
           #[command(subcommand)]
           action: ContextAction,
       },
   }
   
   #[derive(Subcommand)]
   enum ContextAction {
       Set { stone: String },
       Show,
       Clear,
   }
   ```

2. Auto-tend on startup (already implemented in `resolve_endpoint`):
   - Auto-discovery caches result
   - Subsequent commands use cached stone

3. Update tending state:
   ```rust
   async fn handle_tend(stone: Option<String>, client: &reqwest::Client) -> anyhow::Result<()> {
       match stone {
           None => {
               // Show current tending
               show_tending_status()
           }
           Some(ref s) if s == "auto" => {
               // Clear cache, force re-discovery
               tending::clear_tending()?;
               let endpoint = discover_and_tend(client).await?;
               println!("Auto-discovered: {}", endpoint);
           }
           Some(s) => {
               // Set explicit tending
               let endpoint = resolve_target_endpoint(client, &s).await?;
               let caps = fetch_capabilities(client, &endpoint).await?;
               tending::write_tending(caps.stone_name, endpoint)?;
               println!("Now tending to: {}", caps.stone_name);
           }
       }
       Ok(())
   }
   ```

**Files to Modify:**
- `src/rake/src/main.rs` (add commands)
- `src/rake/src/tending.rs` (already exists, enhance output)

**Testing:**
- Test `tend` shows current tended stone
- Test `tend stone-02` sets tending
- Test `context set stone-02` (normative)
- Test `context show` displays tended stone
- Test `context clear` removes cache
- Test subsequent commands use tended stone

---

## Phase 3: Pond Security Structure (API Routes Only)

**Goal:** Prepare pond API routes for later cryptographic implementation

**New Moss Routes:**
```rust
.route("/api/v1/pond/init", post(pond_init_v1))
.route("/api/v1/pond", delete(pond_remove_v1))
.route("/api/v1/pond/invite", post(pond_invite_v1))
.route("/api/v1/pond/join", post(pond_join_v1))
.route("/api/v1/pond/stones/:stone_name", delete(pond_untrust_v1))
.route("/api/v1/pond/status", get(pond_status_v1))
```

**Stub Implementations:**
```rust
async fn pond_init_v1(
    State(state): State<AppState>,
    Json(payload): Json<PondInitRequest>,
) -> Result<Json<PondInitResponse>, (StatusCode, Json<ApiError>)> {
    // TODO: Phase 3b - implement cryptographic initialization
    // For now: return not_implemented error with clear message
    Err(error_response(
        StatusCode::NOT_IMPLEMENTED,
        "POND_NOT_IMPLEMENTED",
        "Pond security implementation pending (Phase 3b)".to_string(),
        None,
    ))
}
```

**New Rake Commands:**
```rust
#[derive(Subcommand)]
enum Commands {
    // Zen
    Place {
        target: PlaceTarget,  // "pebble" or "stone <code>"
    },
    Lift {
        target: LiftTarget,   // "pebble" or "stone <name>"
    },
    Invite,
    
    // Normative
    Pond {
        #[command(subcommand)]
        action: PondAction,
    },
}

#[derive(Subcommand)]
enum PondAction {
    Init,
    Remove,
    Invite,
    Join { code: String },
    Untrust { stone: String },
}
```

**Response Types:**
```rust
#[derive(Serialize, Deserialize)]
struct PondStatus {
    active: bool,
    cornerstone: Option<String>,
    stones: Vec<PondStoneInfo>,
    tier: String,
    note: String,
}

#[derive(Serialize, Deserialize)]
struct PondStoneInfo {
    name: String,
    is_cornerstone: bool,
    certificate_expires: Option<String>,
    joined_at: String,
}
```

**Files to Create:**
- `src/moss/src/api/v1/pond.rs` (stub handlers)
- `src/rake/src/commands/pond.rs` (CLI commands)

**Files to Modify:**
- `src/moss/src/main.rs` (add routes)
- `src/rake/src/main.rs` (add commands)

**Testing:**
- Test all pond routes return NOT_IMPLEMENTED
- Test CLI commands format requests correctly
- Test pond status in observe output (when active)

---

## Phase 4: Integration & Testing

### 4.1 End-to-End Workflow Tests

**Test Scenarios:**
1. **Zen Path:**
   ```bash
   garden-rake explore
   garden-rake offer mongodb
   garden-rake observe
   garden-rake watch mongodb
   garden-rake rest mongodb
   garden-rake wake mongodb
   garden-rake nourish mongodb
   garden-rake release mongodb
   ```

2. **Normative Path:**
   ```bash
   garden-rake list
   garden-rake services create mongodb
   garden-rake status
   garden-rake logs mongodb
   garden-rake services stop mongodb
   garden-rake services start mongodb
   garden-rake services update mongodb
   garden-rake services delete mongodb
   ```

3. **Multi-Stone:**
   ```bash
   garden-rake tend stone-02
   garden-rake observe
   garden-rake offer redis at stone-02
   garden-rake observe all
   ```

4. **Quiet Mode:**
   ```bash
   garden-rake offer mongodb quietly
   garden-rake services create redis --quiet
   GARDEN_QUIET=1 garden-rake observe
   ```

**Files to Create:**
- `tests/integration/dual_syntax.rs`
- `tests/integration/quiet_mode.rs`
- `tests/integration/multi_stone.rs`

---

### 4.2 Error Handling & Validation

**Syntax Mixing Rejection:**
```rust
fn validate_syntax(parsed: &ParsedCommand) -> Result<(), String> {
    match parsed {
        ParsedCommand::Zen(cmd) => {
            // Reject if any --flags present
            if has_dash_flags() {
                return Err("Zen commands use natural syntax. Try: garden-rake offer mongo at stone-02".to_string());
            }
        }
        ParsedCommand::Normative(cmd) => {
            // Reject if positional "at" keyword detected
            if has_at_keyword() {
                return Err("Standard commands use flags. Try: garden-rake services create mongo --at stone-02".to_string());
            }
        }
    }
    Ok(())
}
```

**Files to Create:**
- `src/rake/src/validation.rs`

---

## Phase 5: Documentation Updates

**Files to Update:**
1. `README.md` - Add zen-first examples
2. `docs/CLI-GUIDE.md` - Create comprehensive guide
3. `docs/API-REFERENCE.md` - Document v1 endpoints
4. `CHANGELOG.md` - Record breaking changes

**New Files to Create:**
1. `docs/examples/zen-workflow.md`
2. `docs/examples/normative-workflow.md`
3. `docs/examples/quiet-mode.md`
4. `docs/migration/v0-to-v1.md`

---

## Implementation Order (Detailed Steps)

### Week 1: API Foundation
- [ ] Day 1-2: Moss v1 route structure
- [ ] Day 3-4: Suggestions payload integration
- [ ] Day 5: Lantern garden topology routes

### Week 2: CLI Dual Syntax
- [ ] Day 1-2: Zen parser with positional keywords
- [ ] Day 3: Quiet mode implementation
- [ ] Day 4-5: Context/tend commands enhancement

### Week 3: Pond Structure + Testing
- [ ] Day 1-2: Pond API route stubs
- [ ] Day 3-4: Pond CLI commands
- [ ] Day 5: Integration tests

### Week 4: Documentation + Polish
- [ ] Day 1-3: Documentation updates
- [ ] Day 4-5: Final testing and refinement

---

## Success Criteria

1. ✅ Dual syntax works flawlessly (zen + normative paths)
2. ✅ Suggestions appear in API responses, rendered in CLI
3. ✅ Quiet mode suppresses suggestions reliably
4. ✅ Tend/context commands manage stone focus
5. ✅ Pond routes exist (stubs returning NOT_IMPLEMENTED)
6. ✅ Syntax mixing rejected with helpful errors
7. ✅ Auto-discovery tending works on startup
8. ✅ Legacy routes remain functional (backwards compat)

---

## Risk Mitigation

**Risk:** Positional "at" parsing conflicts with Clap
- **Mitigation:** Pre-parse before Clap, pass cleaned args

**Risk:** Breaking changes for existing users
- **Mitigation:** Keep legacy routes functional, add deprecation warnings

**Risk:** Complexity in dual syntax validation
- **Mitigation:** Comprehensive test coverage, clear error messages

**Risk:** Pond routes unused until Phase 3b
- **Mitigation:** Clear NOT_IMPLEMENTED responses, document in API

---

## Next Steps

1. **Commit this plan**
2. **Start Phase 1.1:** Create `src/moss/src/api/v1/` module structure
3. **Incremental PRs:** One sub-phase per commit
4. **Test-driven:** Write tests before implementation

**Ready to begin implementation!**
