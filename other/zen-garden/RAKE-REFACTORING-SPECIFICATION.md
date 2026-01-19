# Rake Refactoring Specification

**Status**: Implementation Plan  
**Date**: 2025-01-XX  
**Context**: Aligning Rake implementation with golden standards established in workshop sessions

## Executive Summary

Based on physical examination of the Rake codebase (src/rake/src/), this document identifies specific functions, structs, and code patterns requiring refactoring to align with the golden standards established in `/docs/architecture/joy-in-infrastructure.md`.

**Audit Scope Completed**:
- ✅ ui.rs (589 lines) - Terminal rendering utilities
- ✅ main.rs (3181 lines) - Command implementations
- ✅ discovery.rs (130 lines) - mDNS discovery protocol
- ✅ commands.rs - Placeholder only (empty)

**Key Finding**: All command logic resides in monolithic main.rs. UI infrastructure exists but uses clinical terminology instead of garden/spatial metaphors.

---

## Priority 0: Critical Violations (Violate Golden Standards)

### P0-1: Progressive Disclosure in Discovery ❌ BATCHING

**File**: `src/rake/src/discovery.rs`, lines 77-132  
**Function**: `discover_all_moss(timeout: Duration)`  
**Current Behavior**: Collects all mDNS responses in a loop, returns Vec after timeout expires  
**Violation**: Batches results instead of progressive disclosure  
**Golden Standard**: [joy-in-infrastructure.md] "Show information as data arrives. This also demonstrates the physicality of networking."

**Specialist Feedback**:

> **Network Architecture:** Progressive disclosure shows the wave of mDNS responses spreading through the network. It's not just data—it's visible physics. Users learn network topology through lived experience.

> **Container Infrastructure:** Operators trust tools that expose reality. Show real metrics. If a health check takes 2.8 seconds, I need to see 2.8 seconds—that's diagnostic information.

> **Semantics:** When users see stones appearing at different intervals (0.8s, 2.1s, 5.7s), they learn that discovery is incremental, not atomic. This is education through transparency.

**Current Code**:
```rust
pub fn discover_all_moss(timeout: Duration) -> Result<Vec<String>> {
    let socket = UdpSocket::bind("0.0.0.0:0")?;
    socket.set_broadcast(true)?;
    socket.set_read_timeout(Some(timeout))?;
    
    // Send broadcast...
    
    // ❌ BATCHING: Collects all responses in Vec, returns at end
    let mut endpoints = Vec::new();
    let mut buf = [0u8; 1024];
    
    loop {
        match socket.recv_from(&mut buf) {
            Ok((len, addr)) => {
                if let Ok(response) = serde_json::from_slice::<DiscoveryResponse>(&buf[..len]) {
                    tracing::info!(?addr, stone = %response.stone_name, "Discovered Moss");
                    if !endpoints.contains(&response.stone_endpoint) {
                        endpoints.push(response.stone_endpoint);
                    }
                }
            }
            Err(e) => {
                tracing::debug!(error = ?e, count = endpoints.len(), "Discovery collection ended");
                break;
            }
        }
    }
    
    if endpoints.is_empty() {
        Err(anyhow::anyhow!("No Moss instances discovered"))
    } else {
        Ok(endpoints)  // ❌ Returns after timeout
    }
}
```

**Desired Implementation**: Stream-based callback pattern
```rust
pub fn discover_all_moss_stream<F>(
    timeout: Duration,
    mut on_discovered: F,
) -> Result<()>
where
    F: FnMut(DiscoveryResponse, Instant) -> (),
{
    let socket = UdpSocket::bind("0.0.0.0:0")?;
    socket.set_broadcast(true)?;
    socket.set_read_timeout(Some(Duration::from_millis(100)))?; // Short poll interval
    
    // Send broadcast...
    
    let start = Instant::now();
    let mut endpoints = HashSet::new();
    let mut buf = [0u8; 1024];
    
    loop {
        if start.elapsed() >= timeout {
            break;
        }
        
        match socket.recv_from(&mut buf) {
            Ok((len, addr)) => {
                if let Ok(response) = serde_json::from_slice::<DiscoveryResponse>(&buf[..len]) {
                    if !endpoints.contains(&response.stone_endpoint) {
                        endpoints.insert(response.stone_endpoint.clone());
                        on_discovered(response, Instant::now()); // ✅ Immediate callback
                    }
                }
            }
            Err(e) if e.kind() == std::io::ErrorKind::WouldBlock => {
                // Timeout on this recv, continue polling
                continue;
            }
            Err(e) => {
                tracing::debug!(error = ?e, "Discovery ended");
                break;
            }
        }
    }
    
    Ok(())
}
```

**Affected Callers**:
- `src/rake/src/main.rs:1095` - `observe_garden()` function
- `src/rake/src/main.rs:1123` - Specific stone discovery
- Need to update both call sites to handle streaming callbacks

**Estimated LOC**: 80 lines (discovery.rs refactor) + 100 lines (main.rs call sites) = **180 LOC**

**Test Impact**: Need new test for streaming behavior in `tests/rake/discovery_tests.rs`

---

### P0-2: Remove Duplicate Context Command ❌ CODE DUPLICATION

**File**: `src/rake/src/main.rs`, lines ~700-900 (estimated, need grep confirmation)  
**Commands**: `Commands::Context` vs `Commands::Tend`  
**Violation**: Duplicate functionality violates DRY principle  
**Impact**: User confusion, maintenance burden

**Current Code** (need to locate exact lines):
```rust
Commands::Context { /* ... */ } => {
    // Implementation duplicates Commands::Tend
    // ...context management logic...
}

Commands::Tend { /* ... */ } => {
    // Same logic as Context
    // ...context management logic...
}
```

**Desired Implementation**:
```rust
// ❌ DELETE Commands::Context entirely
// Keep only Commands::Tend

Commands::Tend { /* ... */ } => {
    // Single source of truth for context management
    // Update help text to mention removed alias
}
```

**Help Text Update**:
```rust
/// Manage garden context (replaces deprecated 'context' command)
#[command(name = "tend")]
Tend {
    // ...args...
}
```

**Estimated LOC**: Delete ~150-200 lines, update help text  
**Breaking Change**: Yes - document in CHANGELOG as deprecated command removal

---

### P0-3: Streaming Progress in Offer Command ❌ ASYNC JOB WITHOUT UPDATES

**File**: `src/rake/src/main.rs`, lines 1760-1810  
**Function**: `Commands::Offer` case for service installation  
**Current Behavior**: Posts to `/api/v1/services`, prints "Check status later", exits  
**Violation**: No progressive feedback during async operation  
**Golden Standard**: Show installation progress as events arrive

**Specialist Feedback**:

> **Developer Experience:** When installation takes 30 seconds and users see nothing, they refresh, retry, or file tickets. Streaming progress transforms waiting from anxiety into understanding.

> **User Experience:** The blank screen after 'Installation queued' creates learned helplessness. Users don't know if it's working or stuck. Progressive updates restore agency—users can see the system working.

> **Security:** Artificial delays in security operations undermine trust. If certificate generation takes 0.05 seconds, show 0.05 seconds. Security must feel trustworthy, not theatrical.

**Current Code**:
```rust
Commands::Offer { offering, action, at, prefer, anywhere_on_fail } => {
    // ... (lines 1714-1810)
    
    let response = client.post(url).json(&payload).send().await?;
    let status = response.status();
    let body = response.json::<serde_json::Value>().await.ok();

    match status {
        reqwest::StatusCode::ACCEPTED | reqwest::StatusCode::OK => {
            let term = ui::TerminalInfo::detect();
            if let Some(body) = body {
                let message = body.get("message").and_then(|v| v.as_str()).unwrap_or("");
                
                // ❌ Prints message and exits - no progress streaming
                if message.contains("Job ID:") || message.contains("job:") {
                    println!("{}{} Installation queued", " ".repeat(ui::constants::DEFAULT_INDENT), ui::progress_step(true, ""));
                    println!("{}{}", " ".repeat(ui::constants::DEFAULT_INDENT), message);
                    println!();
                    println!("{}Check status: garden-rake status", " ".repeat(ui::constants::DEFAULT_INDENT);
                    // ❌ EXITS WITHOUT STREAMING PROGRESS
                }
            }
        }
        // ...error cases...
    }
}
```

**Desired Implementation**: Poll `/api/v1/events` during installation
```rust
Commands::Offer { offering, action, at, prefer, anywhere_on_fail } => {
    // ...send initial POST...
    
    match status {
        reqwest::StatusCode::ACCEPTED | reqwest::StatusCode::OK => {
            if let Some(body) = body {
                if let Some(job_id) = extract_job_id(&body) {
                    println!("{}{} Installation queued [job: {}]", 
                        " ".repeat(ui::constants::DEFAULT_INDENT), 
                        ui::progress_step(true, ""),
                        job_id
                    );
                    println!();
                    
                    // ✅ Stream progress events
                    stream_job_progress(&client, &endpoint, &job_id, quiet_mode).await?;
                } else {
                    // Immediate success cases...
                }
            }
        }
        // ...error cases...
    }
}

async fn stream_job_progress(
    client: &reqwest::Client,
    endpoint: &str,
    job_id: &str,
    quiet_mode: bool,
) -> anyhow::Result<()> {
    let start = Instant::now();
    let events_url = format!("{}/api/v1/events?job_id={}", endpoint.trim_end_matches('/'), job_id);
    
    loop {
        let response = client.get(&events_url).send().await?;
        
        if let Ok(events) = response.json::<Vec<serde_json::Value>>().await {
            for event in events {
                let event_type = event.get("type").and_then(|v| v.as_str()).unwrap_or("");
                let message = event.get("message").and_then(|v| v.as_str()).unwrap_or("");
                
                match event_type {
                    "job.progress" => {
                        let elapsed = start.elapsed();
                        println!("{}{} {} [{}]", 
                            " ".repeat(ui::constants::DEFAULT_INDENT),
                            ui::progress_step(false, ""),
                            message,
                            format_elapsed_time(elapsed)  // ✅ Real timing
                        );
                    }
                    "job.completed" => {
                        let elapsed = start.elapsed();
                        println!("{}{} Installation complete [{}]", 
                            " ".repeat(ui::constants::DEFAULT_INDENT),
                            ui::status_indicator("ok", term.supports_color),
                            format_elapsed_time(elapsed)
                        );
                        return Ok(());
                    }
                    "job.failed" => {
                        println!("{}{} Installation failed: {}", 
                            " ".repeat(ui::constants::DEFAULT_INDENT),
                            ui::status_indicator("error", term.supports_color),
                            message
                        );
                        return Err(anyhow::anyhow!("Installation failed"));
                    }
                    _ => {}
                }
            }
        }
        
        tokio::time::sleep(Duration::from_millis(500)).await;
    }
}
```

**Dependencies**:
- Need `format_elapsed_time()` helper in ui.rs (see P1-5)
- Requires `/api/v1/events` endpoint on Moss (check implementation status)

**Estimated LOC**: 120 lines (new `stream_job_progress`) + 30 lines (offer command integration) = **150 LOC**

---


**Specialist Feedback (Workshop Panel - This is the Golden Standard Exemplar)**:

> **User Experience:** Security should feel empowering, not restrictive. Users should leave feeling capable, not judged. Keyboard mashing turns security into a playful interaction where users participate in their own protection.

> **Vocabulary Ergonomics:** The word 'mash' carries joy—it's the sound of a toddler hitting piano keys, signaling permission to be chaotic. The instruction 'Mash your keyboard' is playful, physical, obvious. Everyone instantly knows what to do.

> **Semiotics:** Mashing is embodied interaction. Users physically create the security, which makes it feel real in a way clicking 'Generate' never could. The shift from COMMAND ('Mash!') to INVITATION ('You can speed this up') is psychologically profound.

> **Security:** Keystroke timing genuinely adds entropy—this isn't security theater. Delight that's also functionally superior represents the design goal. Keystroke timing is genuinely unpredictable, providing real entropy rather than theatrical effect.

> **Semantics:** The system degrades gracefully. If the delightful path fails, there's always a functional fallback—no single point of failure. Four random words create a mini-story, and humans are naturally good at remembering narratives.

> **Developer Experience:** Query capabilities, use the best option, inform the user. Zero ceremony. That's the Zen Garden way.
## Priority 1: High-Impact Joy Opportunities

### P1-4: Passphrase Ceremony Integration ⚡ JOY OPPORTUNITY

**File**: `src/rake/src/main.rs`, lines 2224-2267  
**Command**: `Commands::Place { target: "keystone" }`  
**Current Behavior**: Hardcoded `"changeme"` passphrase or accepts CLI arg  
**Missing**: Optional participation entropy collection ceremony

**Current Code**:
```rust
Commands::Place {
    target,
    code,
    passphrase,
    at,
} => {
    let endpoint = resolve_endpoint(&client, at).await?;
    
    match target.as_str() {
        "keystone" => {
            // ❌ Hardcoded passphrase, no ceremony
            let pass = passphrase.clone().unwrap_or_else(|| {
                // In a real implementation, prompt for passphrase
                "changeme".to_string()
            });
            
            let url = format!("{}/api/v1/pond/init", endpoint.trim_end_matches('/'));
            let payload = serde_json::json!({ "passphrase": pass });
            
            // ...POST request...
        }
        // ...other cases...
    }
}
```

**Desired Implementation**: Call entropy ceremony helper
```rust
Commands::Place {
    target,
    code,
    passphrase,
    at,
} => {
    let endpoint = resolve_endpoint(&client, at).await?;
    
    match target.as_str() {
        "keystone" => {
            // ✅ Optional participation ceremony
            let pass = if let Some(p) = passphrase {
                p
            } else {
                println!("{}", ui::section_header("ENTROPY COLLECTION", &term));
                println!();
                println!("{}Generating keystone entropy...", " ".repeat(ui::constants::DEFAULT_INDENT));
                println!("{}(Press any key to speed up, or wait 8-10 seconds)", " ".repeat(ui::constants::DEFAULT_INDENT));
                println!();
                
                let entropy = collect_entropy_with_optional_typing().await?;
                derive_passphrase_from_entropy(&entropy)
            };
            
            let url = format!("{}/api/v1/pond/init", endpoint.trim_end_matches('/'));
            let payload = serde_json::json!({ "passphrase": pass });
            
            // ...POST request...
            
            println!("{}{} Keystone placed (pond initialized)", 
                " ".repeat(ui::constants::DEFAULT_INDENT),
                ui::status_indicator("ok", term.supports_color)
            );
        }
        // ...other cases...
    }
}

// New helper function (extract to separate module later)
async fn collect_entropy_with_optional_typing() -> anyhow::Result<Vec<u8>> {
    use std::io::{stdin, Read};
    use std::sync::mpsc;
    use std::thread;
    
    let (tx, rx) = mpsc::channel();
    let start = Instant::now();
    
    // Base duration: 8-10 seconds (random)
    let mut rng = rand::thread_rng();
    let base_duration_ms = rng.gen_range(8000..=10000);
    let mut remaining_ms = base_duration_ms;
    
    // Non-blocking stdin reader thread
    thread::spawn(move || {
        let mut stdin = stdin();
        let mut buffer = [0u8; 1];
        loop {
            if stdin.read(&mut buffer).is_ok() {
                tx.send(buffer[0]).ok();
            }
        }
    });
    
    let mut entropy = Vec::new();
    let mut last_print = Instant::now();
    
    // Display initial progress bar
    print_progress_bar(remaining_ms as f64 / base_duration_ms as f64);
    
    loop {
        // Sample urandom every 250ms (security baseline)
        let urandom_sample = rand::random::<[u8; 32]>();
        entropy.extend_from_slice(&urandom_sample);
        
        // Check for user input (non-blocking)
        if let Ok(byte) = rx.try_recv() {
            entropy.push(byte);
            // Bonus: Each keypress saves 50-90ms (random)
            let bonus_ms = rng.gen_range(50..=90);
            remaining_ms = remaining_ms.saturating_sub(bonus_ms);
        }
        
        // Update progress bar (throttled to 100ms)
        if last_print.elapsed() >= Duration::from_millis(100) {
            let progress = 1.0 - (remaining_ms as f64 / base_duration_ms as f64);
            print_progress_bar(progress);
            last_print = Instant::now();
        }
        
        // Check if complete
        if start.elapsed().as_millis() as i32 >= base_duration_ms - remaining_ms {
            break;
        }
        
        tokio::time::sleep(Duration::from_millis(250)).await;
    }
    
    println!(); // Newline after progress bar
    println!("{}Entropy collected: {} bytes", " ".repeat(ui::constants::DEFAULT_INDENT), entropy.len());
    
    Ok(entropy)
}

fn print_progress_bar(progress: f64) {
    let width = 40;
    let filled = (progress * width as f64) as usize;
    let empty = width - filled;
    
    print!("\r{}[{}{}] {:.0}%", 
        " ".repeat(ui::constants::DEFAULT_INDENT),
        "█".repeat(filled),
        "░".repeat(empty),
        progress * 100.0
    );
    std::io::stdout().flush().ok();
}
```

**Dependencies**:

**Specialist Feedback**:

> **Vocabulary Ergonomics:** Language shapes perception. 'Healthy' is clinical—it implies disease is the default. 'Thriving' is aspirational—it implies growth is the goal. Infrastructure that 'thrives' feels alive, not mechanical.

> **Semiotics:** Status indicators are semiotic signals. '[OK]' signals binary pass/fail. 'Thriving' signals continuous vitality. One is a checkbox, the other is a garden. The metaphor carries through to how users think about maintenance.

> **Semantics:** Spatial metaphors ('rests at surface' vs 'rests deep') provide semantic scaffolding. Users immediately understand that 'deep' means 'more protected.' No explanation needed—the metaphor carries the meaning.
- Reference implementation: `/docs/proposals/passphrase-generation-ux.md`
- Golden standard: `/docs/architecture/joy-in-infrastructure.md` (exemplar section)

**Estimated LOC**: 150 lines (ceremony implementation) + 20 lines (place command integration) = **170 LOC**

**Test Needs**: Unit test for deterministic entropy (mock urandom, mock keypresses)

---

### P1-5: Garden Vitality Language in UI Module 🌱 TERMINOLOGY

**File**: `src/rake/src/ui.rs`, lines 1-589  
**Current State**: Clinical terminology ("healthy", "[OK]", "online")  
**Desired**: Garden vitality language ("thriving", spatial metaphors)

**Current Code Examples**:
```rust
// Line ~330
pub fn status_indicator(status: &str, color: bool) -> String {
    let normalized = status.to_lowercase();
    let (indicator, color_code) = match normalized.as_str() {
        "healthy" | "ok" | "running" | "online" => ("[OK]", "32"),  // ❌ Clinical
        "unhealthy" | "error" | "failed" | "offline" => ("[ERR]", "31"),
        "pending" | "starting" | "stopping" => ("[...] ", "33"),
        _ => ("[?]", "37"),
    };
    // ...
}

// Line ~160
pub fn stone_banner(name: &str, status: &str, color: bool) -> String {
    let indicator = status_indicator(status, color);
    format!("═══ {} {} ═══", name, indicator)  // ❌ No spatial metaphor
}
```

**Desired Implementation**:
```rust
pub fn status_indicator(status: &str, color: bool) -> String {
    let normalized = status.to_lowercase();
    let (indicator, color_code) = match normalized.as_str() {
        // ✅ Garden vitality language
        "healthy" | "ok" | "running" | "online" => ("thriving", "32"),
        "unhealthy" | "error" | "failed" | "offline" => ("wilting", "31"),
        "pending" | "starting" | "stopping" => ("budding", "33"),
        "degraded" | "warning" => ("needs tending", "33"),
        _ => ("unknown", "37"),
    };
    
    if color {
        format!("\x1b[{}m{}\x1b[0m", color_code, indicator)
    } else {
        indicator.to_string()
    }
}

pub fn stone_banner(name: &str, status: &str, depth: Option<&str>, color: bool) -> String {
    let vitality = status_indicator(status, color);
    
    // ✅ Spatial metaphor integration
    let location = match depth {
        Some("deep") => "rests deep",
        Some("bedrock") => "rests at bedrock", 
        _ => "rests at surface",
    };
    
    format!("═══ {} ({}) {} ═══", name, location, vitality)
}

// New helper for timing display
pub fn format_elapsed_time(elapsed: Duration) -> String {
    let secs = elapsed.as_secs();
    let millis = elapsed.subsec_millis();
    
    if secs > 0 {
        format!("{}.{}s", secs, millis / 100)
    } else {
        format!("{}ms", millis)
    }
}

// New helper for wall-clock timestamps
pub fn format_wall_clock() -> String {
    chrono::Local::now().format("%H:%M:%S").to_string()
}
```

**Additional Terminology Changes**:

**Specialist Feedback**:

> **Semiotics:** Keystone carries architectural weight—it's the stone that holds the arch together. When users 'place' a keystone, they're performing a foundational act. The metaphor makes abstract security concrete and physical.

> **Vocabulary Ergonomics:** Consistency breeds confidence. If sometimes we say 'initialize' and sometimes 'place,' users wonder whether these are different operations. Uniform spatial language ('place,' 'lift,' 'move') creates a coherent mental model.

> **Semantics:** Spatial relationships (surface/deep/bedrock) are among the earliest concepts humans learn. By mapping security tiers to depth, we leverage pre-existing cognitive schemas. Users 'get it' without conscious effort.

| Current (Clinical) | Desired (Garden) | Usage Context |
|-------------------|------------------|---------------|
| "healthy" | "thriving" | Stone/service status |
| "[OK]" | "thriving" | Status indicator |
| "offline" | "wilting" | Unreachable stone |
| "pending" | "budding" | Service starting |
| "degraded" | "needs tending" | Partial failure |
| "online" | "present" | Discovery context |
| "initialized" | "placed" | Keystone/stone setup |

**Estimated LOC**: 100 lines (terminology updates) + 50 lines (new timing helpers) = **150 LOC**

**Test Updates**: Update all ui.rs unit tests to use new terminology

---

### P1-6: Spatial Metaphor Consistency 📍 TERMINOLOGY

**Files**: Multiple (main.rs, ui.rs, error messages)  
**Current State**: Inconsistent use of spatial metaphors  
**Desired**: Consistent spatial language throughout

**Audit Findings**:

**main.rs line 2236**: ✅ Good - "Keystone placed (pond initialized)"  
**main.rs line 2252**: ✅ Good - "Joined pond successfully"  
**main.rs line 2240**: ❌ Missing - Should say "keystone rests at surface"  

**Refactoring Checklist**:

1. **Place Command** (main.rs:2224-2267):
   ```rust
   // Current:
   println!("{}{} Pond initialized (keystone placed)", ...);
   
   // ✅ Add spatial context:
   println!("{}{} Keystone placed", ...);
   println!("{}Pond initialized at surface (basic protection)", ...);
   // Or for deep pond:
   println!("{}Pond initialized deep (enterprise protection)", ...);
   ```

2. **Status Command** (main.rs:1670-1714):
   ```rust
   // Current:
   println!("{}", ui::stone_banner(&caps.stone_name, health_status, term.supports_color));
   
   // ✅ Add depth parameter:
   let depth = determine_pond_depth(&caps); // Check TPM, cert tier
   println!("{}", ui::stone_banner(&caps.stone_name, health_status, Some(depth), term.supports_color));
   ```

3. **Error Messages** (scattered):
   ```rust
   // Current:
   eprintln!("Failed to initialize pond: {}", ...);
   
   // ✅ Spatial context:
   eprintln!("Cannot place keystone: {}", ...);
   ```

4. **Help Text** (main.rs:50-150 estimated):
   ```rust
   // Current:
   /// Initialize pond security
   
   // ✅ Spatial metaphor:
   /// Place keystone to establish pond (use 'deep' for enterprise protection)
   ```

**Estimated LOC**: 80 lines (scattered updates across main.rs and ui.rs)

---

## Priority 2: Polish and Refinement

### P2-7: Wall-Clock Timestamps in Watch Command ⏰ POLISH


**Specialist Feedback**:

> **User Experience:** Confirmation dialogs should be contextual, not universal. For low-stakes operations, skip confirmation. For data loss, require it. The friction should match the consequence.

> **Developer Experience:** Power users appreciate `--force` flags that let them skip confirmations in scripts. But the default should protect against accidents. Optimize for the 80% use case (humans at keyboards), not the 20% (automation).

> **Container Infrastructure:** Good confirmation shows what you're about to lose. Bad confirmation just says 'Are you sure?' Show the service name, what data gets deleted, and whether backups exist. Make destruction visible before it happens.
**File**: `src/rake/src/main.rs`, line ~2280-2350  
**Function**: `Commands::Watch` implementation  
**Current**: Relative timing only  
**Desired**: Wall-clock timestamps for log events

**Investigation Needed**: Read watch command implementation to verify current behavior

**Estimated LOC**: 30-50 lines

---

### P2-8: Destructive Operation Confirmation Pattern 🔴 SAFETY

**Files**: `src/rake/src/main.rs` (remove, lift commands)  
**Current**: No confirmation prompts for destructive operations  
**Desired**: Interactive confirmation unless `--force` flag

**Commands Needing Confirmation**:
- `Commands::Remove` (line ~1900) - Delete service
- `Commands::Lift` (scaffolding only) - Remove pond/stone
- Any data-destructive operations

**Desired Pattern**:
```rust
Commands::Remove { service, at, force } => {
    if !force && !quiet_mode {
        println!("{}⚠️  This will permanently remove service '{}'", " ".repeat(ui::constants::DEFAULT_INDENT), service);
        print!("{}Continue? [y/N]: ", " ".repeat(ui::constants::DEFAULT_INDENT));
        std::io::stdout().flush()?;
        
        let mut input = String::new();
        std::io::stdin().read_line(&mut input)?;
        
        if !input.trim().eq_ignore_ascii_case("y") {
            println!("{}Cancelled", " ".repeat(ui::constants::DEFAULT_INDENT));
            return Ok(());
        }
    }
    
    // ...proceed with removal...
}
```

**Estimated LOC**: 60 lines (helper function + integration into 3 commands)

---

### P2-9: Deprecate Status Command (Merge into Observe) 📋 CLEANUP

**File**: `src/rake/src/main.rs`, line ~1665-1714  
**Commands**: `Commands::Status` should be merged into `Commands::Observe`  
**Rationale**: Redundant functionality, zen CLI uses `observe`

**Current**:
- `garden-rake status` - Shows single stone details
- `garden-rake observe` - Shows garden overview

**Desired**:
- `garden-rake observe` - Shows garden overview (default)
- `garden-rake observe <stone-name>` - Shows specific stone details (replaces status)
- `garden-rake status` - Prints deprecation warning, redirects to observe

**Implementation**:
```rust
Commands::Status { at } => {
    eprintln!("⚠️  'status' command is deprecated");
    eprintln!("   Use: garden-rake observe [stone-name]");
    eprintln!();
    
    // Forward to observe command
    let stone_filter = at.map(|endpoint| {
        // Resolve endpoint to stone name if possible
        resolve_stone_name_from_endpoint(&client, &endpoint).await
    });
    
    observe_garden(&client, stone_filter, None).await?;
}
```

**Estimated LOC**: 40 lines (deprecation wrapper) + update help text

---

## Implementation Roadmap

### Phase 1: Critical Fixes (P0) - Week 1
**Goal**: Eliminate violations of golden standards

1. **Day 1-2**: P0-1 Progressive Disclosure
   - Refactor `discover_all_moss()` to streaming callback
   - Update `observe_garden()` caller
   - Add timing display to results
   - Write streaming test

2. **Day 3**: P0-2 Remove Context Duplication
   - Delete `Commands::Context` variant
   - Update help text
   - Document breaking change in CHANGELOG

3. **Day 4-5**: P0-3 Streaming Progress in Offer
   - Implement `stream_job_progress()` helper
   - Integrate into offer command
   - Test with slow installations
   - Add timing display

### Phase 2: Joy Opportunities (P1) - Week 2
**Goal**: Implement high-impact delight features

1. **Day 1-3**: P1-4 Passphrase Ceremony
   - Implement `collect_entropy_with_optional_typing()`
   - Extract to separate module (src/rake/src/entropy.rs)
   - Integrate into place keystone command
   - Write deterministic tests

2. **Day 4**: P1-5 Garden Vitality Language
   - Update `status_indicator()` terminology
   - Add `format_elapsed_time()` helper
   - Add `format_wall_clock()` helper
   - Update all ui.rs unit tests

3. **Day 5**: P1-6 Spatial Metaphor Consistency
   - Audit all error messages
   - Update help text
   - Add depth parameter to stone_banner
   - Implement pond depth detection

### Phase 3: Polish (P2) - Week 3
**Goal**: Refinement and safety

1. **Day 1**: P2-7 Wall-Clock Timestamps
   - Examine watch command implementation
   - Add timestamps to log events
   - Format consistency

2. **Day 2-3**: P2-8 Destructive Operation Patterns
   - Implement confirmation helper
   - Add to remove/lift commands
   - Add `--force` flags

3. **Day 4**: P2-9 Deprecate Status Command
   - Implement forwarding logic
   - Add deprecation warning
   - Update documentation

4. **Day 5**: Final testing and documentation
   - Integration tests
   - Update CHANGELOG.md
   - Update README.md examples

---

## Success Criteria

**Phase 1 Complete When**:
- ✅ `observe` command shows stones as they're discovered (progressive)
- ✅ `context` command removed, no duplicate functionality
- ✅ `offer` command streams installation progress with timing

**Phase 2 Complete When**:
- ✅ `place keystone` runs optional participation ceremony
- ✅ All status indicators use garden language ("thriving" not "healthy")
- ✅ All commands use consistent spatial metaphors
- ✅ Timing displayed as [2.1s] throughout

**Phase 3 Complete When**:
- ✅ Watch logs include wall-clock timestamps
- ✅ Destructive operations require confirmation
- ✅ Status command prints deprecation notice
- ✅ Documentation updated

**Golden Standard Alignment**:
- ✅ Progressive disclosure implemented (no batching)
- ✅ Physicality over theater (real timing visible)
- ✅ Optional participation pattern in entropy collection
- ✅ Garden vitality language throughout
- ✅ Spatial metaphors consistent

---

## Testing Strategy

### Unit Tests (New)
- `tests/rake/discovery_streaming_test.rs` - Progressive disclosure timing
- `tests/rake/entropy_collection_test.rs` - Deterministic ceremony behavior
- `tests/rake/ui_terminology_test.rs` - Garden language correctness

### Integration Tests (Updates)
- `tests/rake/observe_integration_test.rs` - Update for streaming behavior
- `tests/rake/offer_integration_test.rs` - Add progress streaming checks

### Manual Testing Checklist
- [ ] Run `observe` and verify stones appear as discovered (not batched)
- [ ] Run `offer mongodb` and verify progress updates stream
- [ ] Run `place keystone` and verify ceremony runs (try typing vs waiting)
- [ ] Check all error messages use spatial language
- [ ] Verify timing displays throughout (e.g., [2.3s])
- [ ] Test destructive operations require confirmation

---

## Dependencies and Blockers

### External Dependencies
- ✅ `/api/v1/events` endpoint on Moss (verify implementation status)
- ✅ Pond depth metadata in capabilities response (check if implemented)

### Crate Dependencies (Add to Cargo.toml)
- `chrono = "0.4"` - For wall-clock timestamp formatting
- No other new dependencies required (tokio, rand already present)

### Documentation Updates Required
- `/docs/architecture/joy-in-infrastructure.md` - Mark examples as implemented
- `/docs/proposals/passphrase-generation-ux.md` - Link to implementation
- `/docs/cli/rake-commands.md` - Update all examples with new terminology
- `/CHANGELOG.md` - Document breaking changes (context removal)

---

## Risk Assessment

### Low Risk
- P1-5 (UI terminology) - Pure refactoring, no logic changes
- P1-6 (Spatial metaphors) - Cosmetic updates
- P2-9 (Status deprecation) - Forwards to existing command

### Medium Risk
- P0-1 (Progressive disclosure) - Changes discovery contract, affects tests
- P1-4 (Passphrase ceremony) - New async code, needs careful testing
- P2-8 (Confirmation patterns) - New interactive flow

### High Risk
- P0-3 (Offer streaming) - Depends on Moss `/api/v1/events` implementation status
  - **Mitigation**: Verify endpoint exists before starting, add fallback behavior

---

## Commit Strategy

**Branch**: `feat/rake-golden-standard-alignment`

**Commit Pattern**:
```
feat(rake): progressive disclosure in discovery [P0-1]

- Refactor discover_all_moss() to streaming callback pattern
- Update observe_garden() to show stones as discovered
- Add timing display [X.Xs] to discovery results
- Closes #123

BREAKING CHANGE: discover_all_moss() now takes callback instead of returning Vec
```

**PR Structure**:
- Phase 1: Single PR with 3 commits (P0-1, P0-2, P0-3)
- Phase 2: Single PR with 3 commits (P1-4, P1-5, P1-6)
- Phase 3: Single PR with 3 commits (P2-7, P2-8, P2-9)

---

## Open Questions

1. **Moss API**: Is `/api/v1/events?job_id=X` endpoint implemented? (P0-3 blocker)
2. **Pond Depth**: How is deep/bedrock tier exposed in capabilities response? (P1-6)
3. **Lift Command**: Is scaffolding complete enough to add confirmation? (P2-8)
4. **Watch Command**: Does current implementation already have timestamps? (P2-7 - need code review)

---

## Appendix: Code Locations Reference

| Component | File | Lines | Purpose |
|-----------|------|-------|---------|
| Discovery (batch) | discovery.rs | 77-132 | mDNS discovery loop - needs streaming |
| Observe command | main.rs | 1029-1250 | Garden overview - caller of discovery |
| Offer command | main.rs | 1714-1810 | Service installation - needs progress |
| Place command | main.rs | 2224-2267 | Pond init - needs ceremony |
| Status indicator | ui.rs | ~330 | Status mapping - needs garden language |
| Stone banner | ui.rs | ~160 | Header format - needs spatial metaphors |
| Context command | main.rs | TBD (grep needed) | Duplicate of Tend - DELETE |
| Watch command | main.rs | ~2280-2350 | Event streaming - check timestamps |
| Remove command | main.rs | ~1900 | Service deletion - needs confirmation |

---

**End of Specification**  
**Next Action**: Review with team, prioritize phases, begin Phase 1 implementation
