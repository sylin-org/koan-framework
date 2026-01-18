# Rake CLI UX/UI Design Specification

**Date**: 2026-01-18  
**Status**: Approved for Implementation  
**Scope**: Command-line interface output standardization

---

## Executive Summary

This document defines the unified visual system for all rake commands. The design prioritizes consistency, space efficiency, and terminal compatibility while eliminating technical artifacts like raw JSON dumps.

**Priority**: HIGH - CLI is primary user interface for system interaction.

**Key Decisions**:
- Default verbosity level: `-v1` (standard output)
- Terminal width: Query when available, fallback to 80 columns
- Numeric precision: 2 decimals
- Service name truncation: 24 characters
- Legend symbol: `*` (asterisk)

---

## Current State Analysis

### 🔴 Critical Issues

#### 1. **Raw JSON Dumps** (`status` command)
```
Health: {"status":"healthy","timestamp":"2026-01-18T06:48:43.425046174+00:00",...}
```
- **Problem**: Unparsed JSON exposed to users
- **Impact**: Cognitive overload, unusable for quick assessment
- **Severity**: CRITICAL

#### 2. **Inconsistent Headers**
- `observe`: Uses `═══ GARDEN OVERVIEW ═══` 
- `explore`: No header at all
- `status`: Uses plain text "Stone: stone-crimson-summit"
- **Problem**: No predictable structure

#### 3. **Mixed Symbol Systems**
- Bullets: `●` (observe)
- Tree chars: `└─` (observe)  
- Dashes: `-` (explore)
- None: (status)
- **Problem**: No semantic meaning to symbols

#### 4. **Discovery Messaging**
```
Discovering stones...
Tending to: stone-crimson-summit.local (192.168.1.107:7185)
```
- Appears in some commands but not consistently
- "Tending to" is confusing terminology

---

## Proposed Visual System

### Core Principles

1. **Consistency First**: All commands follow same structure
2. **Information Hierarchy**: Clear visual weight for importance
3. **Scanability**: Key info stands out at a glance
4. **Progressive Disclosure**: Detail on demand, not by default
5. **Human-Friendly**: No technical artifacts (JSON, UUIDs, etc.)

### Visual Language Standard

#### **Command Output Structure**

```
[OPTIONAL: Progress indicator during operation]

╭─ COMMAND TITLE ─────────────────────────────────────╮
│ Stone: stone-name                                    │
│ [Context info if relevant]                          │
├──────────────────────────────────────────────────────┤
│                                                      │
│ [Primary content with consistent formatting]        │
│                                                      │
╰──────────────────────────────────────────────────────╯

[OPTIONAL: Suggestions/actions]
```

#### **Symbol System**

| Symbol | Meaning | Usage |
|--------|---------|-------|
| `✓` | Success/Healthy | Completed actions, healthy status |
| `✗` | Error/Failure | Failed operations, critical issues |
| `⚠` | Warning | Degraded state, compatibility issues |
| `○` | Stopped/Offline | Service not running |
| `●` | Running/Active | Service currently running |
| `▸` | Action available | Suggested next steps |
| `└─` | Tree leaf | Last item in hierarchical list |
| `├─` | Tree branch | Item in hierarchical list |

#### **Status Indicators**

- **Healthy**: `✓ Healthy` (green)
- **Degraded**: `⚠ Degraded` (yellow)  
- **Offline**: `○ Offline` (gray)
- **Running**: `● Running` (green)
- **Stopped**: `○ Stopped` (gray)
- **Failed**: `✗ Failed` (red)

#### **Spacing & Alignment**

- **Standard indent**: 2 spaces
- **Label width**: 16 chars (left-aligned)
- **Value alignment**: Start at column 18
- **Section spacing**: 1 blank line between sections
- **Command spacing**: 1 blank line before suggestions

---

## Command-Specific Redesigns

### `garden-rake status`

#### ❌ Current (BAD)
```
Stone: stone-crimson-summit
Health: {"status":"healthy","timestamp":"2026-01-18T06:48:43.425046174+00:00",...}
CPU: 4 cores, x86_64
Memory: 7766 MB
```

#### ✅ Proposed (GOOD)
```
╭─ STONE STATUS ──────────────────────────────────────╮
│ Stone: stone-crimson-summit                          │
│ Arch:  x86_64 · 4 cores                             │
├──────────────────────────────────────────────────────┤
│                                                      │
│ HEALTH            ✓ Healthy                          │
│                                                      │
│ Memory            7.1 GB available of 7.6 GB         │
│                   93% free                           │
│                                                      │
│ Disk              48.5 GB available of 53.6 GB       │
│                   91% free                           │
│                                                      │
│ Docker            ✓ Available                        │
│                                                      │
│ GPU               Intel UHD Graphics 600 (vulkan)    │
│                                                      │
╰──────────────────────────────────────────────────────╯
```

**Key Improvements**:
- ✓ Parsed JSON into readable format
- ✓ Clear status indicators
- ✓ Percentage + absolute values
- ✓ Consistent label alignment
- ✓ Professional box drawing

---

### `garden-rake explore`

#### ❌ Current (MIXED)
```
AI
  ollama           ollama/ollama:latest - Ollama local AI models

DATA
  couchbase        couchbase:community - Couchbase NoSQL database
  ...

Legend: (!) restricted (fallback or incompatible)
```

#### ✅ Proposed (GOOD)
```
╭─ AVAILABLE OFFERINGS ───────────────────────────────╮
│ Stone: stone-crimson-summit                          │
├──────────────────────────────────────────────────────┤
│                                                      │
│ AI                                                   │
│   ollama          Ollama local AI models             │
│                   ollama/ollama:latest               │
│                                                      │
│ DATA                                                 │
│   couchbase       Couchbase NoSQL database           │
│                   couchbase:community                │
│                                                      │
│   elasticsearch   Elasticsearch search and analytics │
│                   elasticsearch:8.11.0               │
│                                                      │
│   mongodb         MongoDB NoSQL database             │
│                   mongo:4.4 (fallback)               │
│                   ⚠ AVX required - using older ver   │
│                                                      │
│   postgresql      PostgreSQL relational database     │
│                   pgvector/pgvector:pg16             │
│                                                      │
│ MESSAGING                                            │
│   rabbitmq        RabbitMQ message broker            │
│                   rabbitmq:3-management-alpine       │
│                                                      │
│ VECTOR                                               │
│   milvus          Milvus vector database             │
│                   milvusdb/milvus:latest             │
│                   ✗ Incompatible: requires 8GB+ RAM  │
│                                                      │
│   weaviate        Weaviate vector database           │
│                   semitechnologies/weaviate:latest   │
│                                                      │
╰──────────────────────────────────────────────────────╯

To install: garden-rake offer <name>
```

**Key Improvements**:
- ✓ Separated description from image name (image as secondary info)
- ✓ Clear warning symbols for compatibility issues
- ✓ Inline compatibility notes (not legend)
- ✓ Consistent indentation
- ✓ Clear action prompt at bottom

---

### `garden-rake observe`

#### ✅ Current (ALREADY GOOD)
```
═══ GARDEN OVERVIEW ═══

●  stone-crimson-summit (x86_64)
   CPU: 4 cores  │  Memory: 7766 MB  │  GPUs: 1
   OFFERINGS:
   └─ mongodb       Run   0.14%   82.20 MB  ↓ 5.69 KB  3m 43s
```

#### ✅ Refined Proposal
```
╭─ GARDEN OVERVIEW ───────────────────────────────────╮
│                                                      │
│ ● stone-crimson-summit                               │
│   x86_64 · 4 cores · 7.6 GB RAM · 1 GPU            │
│                                                      │
│   SERVICES                                           │
│   └─ mongodb       ● Running   3m 43s               │
│      CPU: 0.14%   Memory: 82 MB   Network: 5.7 KB  │
│                                                      │
╰──────────────────────────────────────────────────────╯
```

**Key Improvements**:
- ✓ Consistent box style (matches other commands)
- ✓ Separated metrics to second line for clarity
- ✓ Using dot notation for specs (·)
- ✓ Humanized units (MB not MB, KB not KB)

---

### `garden-rake offer <name>`

#### ❌ Current (MINIMAL)
```
✓ create mongodb (pending)

Suggestions:
  • garden-rake observe              View service status
  • garden-rake watch <service>      Stream service logs
  • garden-rake explore              Browse more offerings
```

#### ✅ Proposed (BETTER)
```
╭─ OFFERING: MONGODB ─────────────────────────────────╮
│ Status: Installing...                                │
├──────────────────────────────────────────────────────┤
│                                                      │
│ Image:   mongo:4.4 (fallback)                        │
│ Note:    Using MongoDB 4.4 - your CPU lacks AVX      │
│                                                      │
│ ● Pulling image...                                   │
│ ● Creating container...                              │
│ ● Starting service...                                │
│                                                      │
╰──────────────────────────────────────────────────────╯

▸ garden-rake watch mongodb    View installation progress
▸ garden-rake observe          Check service status
```

**Key Improvements**:
- ✓ Shows what's happening (progress)
- ✓ Explains compatibility decisions inline
- ✓ Clear next actions with context
- ✓ Consistent visual structure

---

### `garden-rake list`

#### ❌ Current (NOT SHOWN - needs design)

#### ✅ Proposed
```
╭─ INSTALLED SERVICES ────────────────────────────────╮
│ Stone: stone-crimson-summit                          │
├──────────────────────────────────────────────────────┤
│                                                      │
│ ● mongodb         Running · 3m 43s                   │
│   Category: data                                     │
│   Health: ✓ Healthy                                  │
│                                                      │
│ ○ postgresql      Stopped                            │
│   Category: data                                     │
│   Health: ○ Offline                                  │
│                                                      │
╰──────────────────────────────────────────────────────╯

2 services installed (1 running, 1 stopped)
```

---

## Implementation Architecture

### Design Principles (SoC/DRY)

**Separation of Concerns:**
- `ui.rs` - Pure rendering functions (no business logic)
- Command modules - Business logic only (delegate formatting to ui.rs)
- `terminal.rs` - Terminal capability detection and sizing

**DRY Approach:**
- Shared rendering helpers eliminate duplication
- Centralized constraints (width, indents, precision)
- Single source of truth for formatting rules

---

## Technical Specification

### Module Structure

```
src/rake/src/
├── ui.rs           # All UI rendering (NEW)
├── terminal.rs     # Terminal detection (NEW)
├── commands/
│   ├── status.rs   # Uses ui.rs helpers
│   ├── explore.rs  # Uses ui.rs helpers
│   ├── observe.rs  # Uses ui.rs helpers
│   └── ...
```

### Core UI Module (`src/rake/src/ui.rs`)

```rust
// Terminal capability detection
pub struct TerminalInfo {
    pub width: usize,
    pub supports_color: bool,
}

impl TerminalInfo {
    pub fn detect() -> Self {
        let width = terminal_size()
            .map(|(w, _)| w.0 as usize)
            .unwrap_or(80);
        
        let supports_color = supports_color::on(supports_color::Stream::Stdout);
        
        Self { width, supports_color }
    }
}

// Verbosity level
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Verbosity {
    Minimal = 0,    // -v0
    Standard = 1,   // -v1 (DEFAULT)
    Verbose = 2,    // -v2
    Debug = 3,      // -v3
}

impl Default for Verbosity {
    fn default() -> Self {
        Self::Standard
    }
}

// Stone banner (always first line)
pub fn stone_banner(name: &str, status: &str, color: bool) -> String {
    if color {
        let colored_status = match status {
            s if s.contains("Healthy") || s.contains("[OK]") => s.green(),
            s if s.contains("[ERROR]") => s.red(),
            s if s.contains("[WARN]") => s.yellow(),
            _ => s.normal(),
        };
        format!("[{} - {}]", name, colored_status)
    } else {
        format!("[{} - {}]", name, status)
    }
}

// Section header with dynamic width
pub fn section_header(title: &str, term: &TerminalInfo) -> String {
    let prefix = "--- ";
    let suffix = " ---";
    let title_len = prefix.len() + title.len() + suffix.len();
    let dashes = if term.width > title_len {
        "-".repeat(term.width - title_len)
    } else {
        String::new()
    };
    format!("{}{}{}{}", prefix, title, suffix, dashes)
}

// Indented label: value line
pub fn label_value_line(label: &str, value: &str, indent: usize) -> String {
    format!("{}{:<12}{}", " ".repeat(indent), label, value)
}

// Right-aligned numeric column
pub fn format_number(value: f64, precision: usize) -> String {
    format!("{:.*}", precision, value)
}

// Truncate service names to max length
pub fn truncate_name(name: &str, max_len: usize) -> String {
    if name.len() > max_len {
        format!("{}...", &name[..max_len - 3])
    } else {
        name.to_string()
    }
}

// Table builder for columnar data
pub struct TableBuilder {
    columns: Vec<Column>,
    rows: Vec<Vec<String>>,
    indent: usize,
}

struct Column {
    width: usize,
    align: Align,
}

enum Align {
    Left,
    Right,
}

impl TableBuilder {
    pub fn new() -> Self {
        Self {
            columns: Vec::new(),
            rows: Vec::new(),
            indent: 4,
        }
    }
    
    pub fn add_column(mut self, width: usize, align: Align) -> Self {
        self.columns.push(Column { width, align });
        self
    }
    
    pub fn add_row(&mut self, values: Vec<String>) {
        self.rows.push(values);
    }
    
    pub fn render(&self) -> String {
        let mut output = String::new();
        let indent_str = " ".repeat(self.indent);
        
        for row in &self.rows {
            output.push_str(&indent_str);
            for (i, value) in row.iter().enumerate() {
                if let Some(col) = self.columns.get(i) {
                    let formatted = match col.align {
                        Align::Left => format!("{:<width$}", value, width = col.width),
                        Align::Right => format!("{:>width$}", value, width = col.width),
                    };
                    output.push_str(&formatted);
                    if i < row.len() - 1 {
                        output.push_str("  ");
                    }
                }
            }
            output.push('\n');
        }
        output
    }
}

// Multi-column category layout
pub struct CategoryGrid {
    items_per_row: usize,
    category_width: usize,
    item_width: usize,
    indent: usize,
}

impl CategoryGrid {
    pub fn new(term: &TerminalInfo) -> Self {
        let indent = 4;
        let category_width = 12;
        let item_width = 16;
        let available = term.width.saturating_sub(indent + category_width);
        let items_per_row = (available / item_width).max(1);
        
        Self {
            items_per_row,
            category_width,
            item_width,
            indent,
        }
    }
    
    pub fn render_category(&self, category: &str, items: &[String]) -> String {
        let mut output = String::new();
        let indent_str = " ".repeat(self.indent);
        
        for (i, chunk) in items.chunks(self.items_per_row).enumerate() {
            output.push_str(&indent_str);
            
            if i == 0 {
                // First row: category name
                output.push_str(&format!("{:<width$}", category, width = self.category_width));
            } else {
                // Continuation rows: blank category column
                output.push_str(&" ".repeat(self.category_width));
            }
            
            for item in chunk {
                output.push_str(&format!("{:<width$}", item, width = self.item_width));
            }
            output.push('\n');
        }
        output
    }
}

// Status indicators with optional color
pub fn status_indicator(status: &str, color: bool) -> String {
    let indicator = match status.to_lowercase().as_str() {
        "running" => "[running]",
        "stopped" => "[stopped]",
        "ok" | "healthy" => "[OK]",
        "error" | "failed" => "[ERROR]",
        "warn" | "warning" => "[WARN]",
        _ => status,
    };
    
    if color {
        match status.to_lowercase().as_str() {
            "running" | "ok" | "healthy" => indicator.green().to_string(),
            "stopped" | "warn" | "warning" => indicator.yellow().to_string(),
            "error" | "failed" => indicator.red().to_string(),
            _ => indicator.to_string(),
        }
    } else {
        indicator.to_string()
    }
}

// Empty state message
pub fn empty_state(message: &str, action_hint: Option<&str>) -> String {
    let mut output = String::new();
    output.push_str(&format!("    {}\n", message));
    if let Some(hint) = action_hint {
        output.push('\n');
        output.push_str(hint);
        output.push('\n');
    }
    output
}

// Constants
pub mod constants {
    pub const DEFAULT_INDENT: usize = 4;
    pub const DEFAULT_TERMINAL_WIDTH: usize = 80;
    pub const NUMERIC_PRECISION: usize = 2;
    pub const MAX_SERVICE_NAME_LEN: usize = 24;
    pub const LEGEND_SYMBOL: char = '*';
}
```

### Usage Example

```rust
// In src/rake/src/commands/status.rs
use crate::ui::{self, TerminalInfo, Verbosity};

pub fn render_status(stone_name: &str, health_data: &HealthData, verbosity: Verbosity) -> String {
    let term = TerminalInfo::detect();
    let mut output = String::new();
    
    // Stone banner
    output.push_str(&ui::stone_banner(stone_name, &health_data.status, term.supports_color));
    output.push_str("\n\n");
    
    // Skip section header for single-section output at verbosity >= 1
    if verbosity >= Verbosity::Standard {
        // Data rows
        output.push_str(&ui::label_value_line("HEALTH", &health_data.health_summary, 4));
        output.push_str("\n");
        output.push_str(&ui::label_value_line(
            "MEMORY",
            &format!("{} / {} ({:.2}% free)", health_data.mem_avail, health_data.mem_total, health_data.mem_pct),
            4
        ));
        output.push_str("\n");
        // ... more fields
    }
    
    output
}
```

### Dependencies

```toml
# Cargo.toml additions
[dependencies]
colored = "2.1"              # Terminal colors
terminal_size = "0.3"        # Terminal width detection
supports-color = "3.0"       # Color support detection
```

---

## Implementation Roadmap

### Phase 1: Core Infrastructure (Est: 1-2 days)
1. Create `src/rake/src/ui.rs` with all helper functions
2. Create `src/rake/src/terminal.rs` for capability detection
3. Add verbosity flag parsing to CLI
4. Add color detection and fallback logic
5. Write unit tests for rendering functions

**Deliverables:**
- ✓ Stone banner rendering
- ✓ Section header with dynamic width
- ✓ Table builder for columnar data
- ✓ Category grid for multi-column layout
- ✓ Status indicators with color support
- ✓ Constants module with all constraints

### Phase 2: Command Updates (Est: 2-3 days)
Priority order:
1. **`status`** - Parse JSON, use label_value_line helpers (CRITICAL)
2. **`explore`** - Multi-column categories with CategoryGrid
3. **`observe`** - Table format with TableBuilder
4. **`offer`** - Progress indicators and empty states
5. **`list`** - Service table with TableBuilder

**Deliverables:**
- ✓ All commands use ui.rs helpers (no inline formatting)
- ✓ Verbosity levels implemented (-v0, -v1, -v2, -v3)
- ✓ Empty states for all commands
- ✓ Error formatting standardized

### Phase 3: Polish & Testing (Est: 1 day)
- Error message consistency across commands
- Multi-stone output formatting
- Terminal width edge cases (< 80, > 200)
- Color fallback testing
- Help text standardization
- Documentation updates

**Test Matrix:**
- Terminal widths: 80, 100, 120, 160+ columns
- Color support: Enabled, disabled
- Verbosity: -v0, -v1 (default), -v2, -v3
- Edge cases: Empty data, errors, long names

---

## Testing Checklist

### Functional Tests
- [ ] `garden-rake status` - JSON parsed correctly at all verbosity levels
- [ ] `garden-rake explore` - Multi-column categories render correctly
- [ ] `garden-rake observe` - Table alignment correct with varying data
- [ ] `garden-rake offer <name>` - Progress feedback clear
- [ ] `garden-rake list` - Service table formatted properly
- [ ] Empty states render without headers
- [ ] Error states show summary + details
- [ ] Multi-stone output separated correctly

### Terminal Compatibility
- [ ] 80-column terminals (minimum baseline)
- [ ] 120-column terminals (common laptop)
- [ ] 160+ column terminals (wide monitors)
- [ ] Windows cmd.exe (no color, ASCII-only)
- [ ] Windows PowerShell (color support)
- [ ] Linux terminals (full color support)
- [ ] SSH sessions (may lack color)

### Verbosity Levels
- [ ] `-v0` minimal output (names + status, no spacing)
- [ ] `-v1` standard output (default, as designed)
- [ ] `-v2` verbose (adds container IDs, full images)
- [ ] `-v3` debug (includes raw JSON/metadata)

### Color Support
- [ ] Colors applied when terminal supports it
- [ ] Graceful fallback to ASCII indicators when no color
- [ ] Consistent color scheme across commands
- [ ] Status colors semantic (green/yellow/red)

---

## Success Metrics

**Quantitative:**
- Zero raw JSON dumps in user-facing output
- 100% of commands use ui.rs helpers (no inline formatting)
- All status indicators use ASCII constants
- Terminal width queried on every render
- Color detection automatic with fallback

**Qualitative:**
- Users can scan output in <3 seconds
- No confusion about command results
- Professional appearance matching modern CLIs
- Consistent visual language across all commands
- Terminal compatibility (works everywhere)

---

## References

### Inspiration from Other CLIs
- **Docker CLI**: Clean table formatting, right-aligned numbers
- **Kubernetes kubectl**: Multi-resource output with consistent headers
- **Terraform**: Color-coded status indicators with ASCII fallback
- **Ansible**: Progressive task feedback with clear status
- **Git**: Concise status output, minimal verbosity by default

### Related Documentation
- `/other/zen-garden/src/rake/README.md` - Rake CLI overview
- `/other/zen-garden/src/moss/README.md` - Moss API documentation
- `/other/zen-garden/docs/ARCHITECTURE.md` - System architecture

---

## Appendix: Full Example Comparison

### Before (Current Mixed State)
```
garden-rake status
Discovering stones...
Tending to: stone-crimson-summit.local (192.168.1.107:7185)

Stone: stone-crimson-summit
Health: {"status":"healthy","timestamp":"2026-01-18T06:48:43.425046174+00:00","components":{"memory":{"status":"healthy","available_gb":"7.1","total_gb":"7.6","usage_percent":"7.02"},"disk":{"status":"healthy","usage_percent":"9.38","free_gb":"48.5","total_gb":"53.6"},"docker":{"status":"healthy","available":true}},"checks":{"disk":{"status":"pass"},"docker":{"status":"pass"},"memory":{"status":"pass"}}}
CPU: 4 cores, x86_64
Memory: 7766 MB
GPUs:
  - Intel Intel Corporation GeminiLake [UHD Graphics 600] (rev 03) (vulkan)
```

### After (Approved Standard -v1)
```
[stone-crimson-summit - Healthy]

    HEALTH      [OK] All systems operational
    MEMORY      7.1 GB / 7.6 GB (93% free)
    DISK        48.5 GB / 53.6 GB (91% free)
    DOCKER      [OK] Available
    GPU         Intel UHD Graphics 600 (vulkan)
    ARCH        x86_64 (4 cores)
```

**Visual Difference**: 
- Clean, parseable, professional
- Information hierarchy clear
- No technical artifacts exposed
- Space-efficient (6 lines vs 13 lines)
- Consistent 4-space indentation
- ASCII-safe status indicators

---

**Document Version**: 2.0  
**Last Updated**: 2026-01-18  
**Status**: ✓ Approved for Implementation  
**Next Action**: Begin Phase 1 - Create ui.rs module with helper functions
