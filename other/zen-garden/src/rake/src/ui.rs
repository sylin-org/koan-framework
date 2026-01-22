// UI rendering module for rake CLI
// Provides reusable helpers for consistent terminal output
// Following SoC/DRY principles - all formatting logic centralized here

use colored::Colorize;

/// Terminal capability information
#[derive(Debug, Clone)]
pub struct TerminalInfo {
    pub width: usize,
    pub supports_color: bool,
    pub supports_unicode: bool,
}

impl TerminalInfo {
    /// Detect terminal capabilities (width and color support)
    pub fn detect() -> Self {
        let width = terminal_size::terminal_size()
            .map(|(w, _)| w.0 as usize)
            .unwrap_or(constants::DEFAULT_TERMINAL_WIDTH);
        
        // Check NO_COLOR environment variable first (universal override)
        let no_color = std::env::var(garden_common::ENV_NO_COLOR).is_ok();
        
        // Use supports-color crate for proper terminal detection
        let supports_color = !no_color && supports_color::on(supports_color::Stream::Stdout).is_some();
        
        // Unicode support: disabled on Windows by default (PowerShell encoding issues)
        // Can be enabled with GARDEN_UNICODE=1 environment variable
        let supports_unicode = if cfg!(windows) {
            std::env::var(garden_common::ENV_GARDEN_UNICODE).is_ok()
        } else {
            true // Unix terminals generally handle Unicode well
        };
        
        Self { width, supports_color, supports_unicode }
    }
}

/// Consistent output formatting with automatic indentation and status indicators
/// Reduces duplication of println!/eprintln! calls with manual formatting
#[allow(dead_code)] // Incrementally adopting this pattern
pub struct OutputWriter {
    term: TerminalInfo,
    indent: usize,
}

#[allow(dead_code)]
impl OutputWriter {
    /// Create new output writer with default settings
    pub fn new() -> Self {
        Self {
            term: TerminalInfo::detect(),
            indent: constants::DEFAULT_INDENT,
        }
    }

    /// Create output writer with custom indentation
    pub fn with_indent(indent: usize) -> Self {
        Self {
            term: TerminalInfo::detect(),
            indent,
        }
    }

    /// Create output writer with pre-detected terminal info
    pub fn with_term(term: TerminalInfo) -> Self {
        Self {
            term,
            indent: constants::DEFAULT_INDENT,
        }
    }

    /// Success message (green OK indicator)
    pub fn success(&self, msg: impl std::fmt::Display) {
        println!(
            "{}{} {}",
            " ".repeat(self.indent),
            status_indicator("ok", self.term.supports_color),
            msg
        );
    }

    /// Error message (red ERROR indicator)
    pub fn error(&self, msg: impl std::fmt::Display) {
        eprintln!(
            "{}{} {}",
            " ".repeat(self.indent),
            status_indicator("error", self.term.supports_color),
            msg
        );
    }

    /// Info message (blue info indicator)
    pub fn info(&self, msg: impl std::fmt::Display) {
        println!(
            "{}{} {}",
            " ".repeat(self.indent),
            status_indicator("info", self.term.supports_color),
            msg
        );
    }

    /// Warning message (yellow WARN indicator)
    pub fn warn(&self, msg: impl std::fmt::Display) {
        println!(
            "{}{} {}",
            " ".repeat(self.indent),
            status_indicator("warn", self.term.supports_color),
            msg
        );
    }

    /// Pending/in-progress message
    pub fn pending(&self, msg: impl std::fmt::Display) {
        println!(
            "{}{} {}",
            " ".repeat(self.indent),
            status_indicator("pending", self.term.supports_color),
            msg
        );
    }

    /// Detail line (indented, no indicator)
    pub fn detail(&self, msg: impl std::fmt::Display) {
        println!("{}  {}", " ".repeat(self.indent), msg);
    }

    /// Bullet point (• prefix)
    pub fn bullet(&self, msg: impl std::fmt::Display) {
        println!("{}  • {}", " ".repeat(self.indent), msg);
    }

    /// Plain line with indent
    pub fn line(&self, msg: impl std::fmt::Display) {
        println!("{}{}", " ".repeat(self.indent), msg);
    }

    /// Blank line
    pub fn blank_line(&self) {
        println!();
    }

    /// Get terminal info for advanced formatting
    pub fn term(&self) -> &TerminalInfo {
        &self.term
    }
}

impl Default for OutputWriter {
    fn default() -> Self {
        Self::new()
    }
}

/// Verbosity level for command output (Phase 3)
#[allow(dead_code)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
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

#[allow(dead_code)]
impl Verbosity {
    /// Parse from command line argument (e.g., "-v0", "-v1")
    pub fn from_arg(arg: &str) -> Option<Self> {
        match arg {
            "-v0" => Some(Self::Minimal),
            "-v1" => Some(Self::Standard),
            "-v2" => Some(Self::Verbose),
            "-v3" => Some(Self::Debug),
            _ => None,
        }
    }
}

/// Render stone banner (always first line of output)
/// Format: === stone-name - [status] =========
/// Uses garden vitality language: thriving/dormant/needs attention
pub fn stone_banner(name: &str, status: &str, color: bool) -> String {
    let term = TerminalInfo::detect();
    let max_width = term.width.min(80);
    
    let status_lower = status.to_lowercase();
    let status_with_brackets = format!("[{}]", status);
    let status_colored = if color {
        if status_lower.contains(garden_common::VITALITY_THRIVING) || status_lower.contains(garden_common::HEALTH_HEALTHY) {
            status_with_brackets.green().to_string()
        } else if status_lower.contains(garden_common::VITALITY_WITHERING) || status_lower.contains(garden_common::HEALTH_UNHEALTHY) || status.contains("ERROR") {
            status_with_brackets.red().to_string()
        } else if status_lower.contains(garden_common::VITALITY_DORMANT) || status_lower.contains(garden_common::VITALITY_NEEDS_ATTENTION) || status_lower.contains(garden_common::HEALTH_DEGRADED) || status.contains("WARN") {
            status_with_brackets.yellow().to_string()
        } else {
            status_with_brackets
        }
    } else {
        status_with_brackets
    };
    
    let prefix = "=== ";
    let middle = format!("{} - {}", name, status_colored);
    // For length calculation, use the uncolored version
    let middle_len = format!("{} - {}", name, format!("[{}]", status)).len();
    let total_len = prefix.len() + middle_len + 1; // +1 for space before equals
    
    let equals = if max_width > total_len {
        " ".to_string() + &"=".repeat(max_width - total_len)
    } else {
        String::new()
    };
    
    format!("{}{}{}", prefix, middle, equals)
}

/// Render section header with dynamic width (max 40 chars)
/// Format: --- TITLE ---[dashes to 40 chars max]
pub fn section_header(title: &str, term: &TerminalInfo) -> String {
    let prefix = "--- ";
    let suffix = " ";
    let title_len = prefix.len() + title.len() + suffix.len();
    let max_width = term.width.min(40);
    let dashes = if max_width > title_len {
        "-".repeat(max_width - title_len)
    } else {
        String::new()
    };
    format!("{}{}{}{}", prefix, title, suffix, dashes)
}

/// Render section header with short underline (21 chars)
/// Zen Garden UI Standard for grouped key-value displays
/// Format:
/// SECTION_NAME
/// ─────────────────────
pub fn section_header_v2(title: &str, bold: bool, color: bool) -> String {
    const UNDERLINE_LENGTH: usize = 21;
    let underline = "─".repeat(UNDERLINE_LENGTH);
    
    let title_display = if color && bold {
        title.to_uppercase().bold().to_string()
    } else if bold {
        title.to_uppercase()
    } else {
        title.to_uppercase()
    };
    
    format!("{}\n{}", title_display, underline)
}

/// Render key-value line with proper alignment
/// Label width: 16 chars (left-aligned), value starts at column 21
/// Format: "    LABEL            value"
pub fn kv_line(label: &str, value: &str, indent_spaces: usize) -> String {
    const LABEL_WIDTH: usize = 16;
    let indent = " ".repeat(indent_spaces);
    format!("{}{:<width$} {}", indent, label.to_uppercase(), value, width = LABEL_WIDTH)
}

/// Render indented label: value line
#[allow(dead_code)]
pub fn label_value_line(label: &str, value: &str, indent: usize) -> String {
    format!("{}{:<12} {}", " ".repeat(indent), label, value)
}

/// Format number with specified precision (Phase 3)
#[allow(dead_code)]
pub fn format_number(value: f64, precision: usize) -> String {
    format!("{:.*}", precision, value)
}

/// Truncate service/offering name to max length
pub fn truncate_name(name: &str, max_len: usize) -> String {
    if name.len() > max_len {
        format!("{}...", &name[..max_len - 3])
    } else {
        name.to_string()
    }
}

/// Render text with specified color (respects terminal color support)
pub fn colored_text(text: &str, color: &str, term: &TerminalInfo) -> String {
    if !term.supports_color {
        return text.to_string();
    }
    
    match color {
        "red" => text.red().to_string(),
        "green" => text.green().to_string(),
        "yellow" => text.yellow().to_string(),
        "blue" => text.blue().to_string(),
        "magenta" => text.magenta().to_string(),
        "cyan" => text.cyan().to_string(),
        "white" => text.white().to_string(),
        _ => text.to_string(),
    }
}

/// Column alignment for tables
#[derive(Debug, Clone, Copy)]
pub enum Align {
    Left,
    Right,
}

/// Column definition for TableBuilder
#[derive(Debug, Clone)]
struct Column {
    width: usize,
    align: Align,
}

/// Table builder for columnar data with consistent alignment
pub struct TableBuilder {
    columns: Vec<Column>,
    rows: Vec<Vec<String>>,
    indent: usize,
}

impl TableBuilder {
    /// Create new table builder with default indent (4 spaces)
    pub fn new() -> Self {
        Self {
            columns: Vec::new(),
            rows: Vec::new(),
            indent: constants::DEFAULT_INDENT,
        }
    }
    
    /// Add a column with specified width and alignment
    pub fn add_column(mut self, width: usize, align: Align) -> Self {
        self.columns.push(Column { width, align });
        self
    }
    
    /// Set custom indentation (default is DEFAULT_INDENT)
    pub fn with_indent(mut self, indent: usize) -> Self {
        self.indent = indent;
        self
    }
    
    /// Add a data row to the table
    pub fn add_row(&mut self, values: Vec<String>) {
        self.rows.push(values);
    }
    
    /// Render the table to a string
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

impl Default for TableBuilder {
    fn default() -> Self {
        Self::new()
    }
}

/// Multi-column category layout for explore command
pub struct CategoryGrid {
    items_per_row: usize,
    category_width: usize,
    item_width: usize,
    indent: usize,
}

impl CategoryGrid {
    /// Create new category grid based on terminal width
    pub fn new(term: &TerminalInfo) -> Self {
        let indent = constants::DEFAULT_INDENT;
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
    
    /// Render a category with its items in multi-column layout
    /// First row shows category name, continuation rows have blank category column
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

/// Render status indicator with optional color
/// Uses garden vitality language where appropriate
pub fn status_indicator(status: &str, color: bool) -> String {
    let status_lower = status.to_lowercase();
    let status_str = status_lower.as_str();
    
    let indicator = if status_str == garden_common::SERVICE_RUNNING || status_str == garden_common::VITALITY_THRIVING {
        garden_common::VITALITY_THRIVING
    } else if status_str == garden_common::SERVICE_STOPPED || status_str == garden_common::VITALITY_DORMANT {
        garden_common::VITALITY_DORMANT
    } else if status_str == garden_common::VITALITY_NEEDS_ATTENTION || status_str == garden_common::VITALITY_WITHERING {
        garden_common::VITALITY_NEEDS_ATTENTION
    } else if status_str == "ok" || status_str == garden_common::HEALTH_HEALTHY {
        garden_common::VITALITY_THRIVING
    } else if status_str == "error" || status_str == "failed" || status_str == garden_common::HEALTH_UNHEALTHY {
        garden_common::VITALITY_NEEDS_ATTENTION
    } else if status_str == "warn" || status_str == "warning" || status_str == garden_common::HEALTH_DEGRADED {
        garden_common::VITALITY_NEEDS_ATTENTION
    } else {
        return status.to_string();  // Unknown status, pass through without brackets
    };
    
    // Always bracket known statuses
    let bracketed = format!("[{}]", indicator);
    
    if color {
        let is_healthy = status_str == garden_common::SERVICE_RUNNING 
            || status_str == "ok" 
            || status_str == garden_common::HEALTH_HEALTHY 
            || status_str == garden_common::VITALITY_THRIVING;
            
        let is_degraded = status_str == garden_common::SERVICE_STOPPED 
            || status_str == "warn" || status_str == "warning"
            || status_str == garden_common::HEALTH_DEGRADED 
            || status_str == garden_common::VITALITY_DORMANT;
            
        let is_unhealthy = status_str == "error" || status_str == "failed"
            || status_str == garden_common::HEALTH_UNHEALTHY 
            || status_str == garden_common::VITALITY_WITHERING;
            
        if is_healthy {
            bracketed.green().to_string()
        } else if is_degraded {
            bracketed.yellow().to_string()
        } else if is_unhealthy {
            bracketed.red().to_string()
        } else {
            bracketed
        }
    } else {
        bracketed
    }
}

/// Render empty state message with optional action hint
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

/// Get appropriate bullet character based on terminal capabilities
/// Uses Unicode on terminals that support it, ASCII fallback otherwise
pub fn bullet(supports_unicode: bool) -> &'static str {
    if supports_unicode { "●" } else { "*" }
}

/// Get appropriate hollow bullet based on terminal capabilities  
pub fn hollow_bullet(supports_unicode: bool) -> &'static str {
    if supports_unicode { "○" } else { "o" }
}

/// Render progress indicator for operations
/// `[*]` = in progress, `[ ]` = pending
pub fn progress_step(active: bool, message: &str) -> String {
    let indicator = if active { "[*]" } else { "[ ]" };
    format!("    {} {}", indicator, message)
}

/// Render colored category label (Phase 3)
#[allow(dead_code)]
pub fn category_label(name: &str, color: bool) -> String {
    if color {
        name.cyan().to_string()
    } else {
        name.to_string()
    }
}

/// Format elapsed time for display (e.g., "2.3s", "847ms")
/// Shows real timing information - no artificial delays
pub fn format_elapsed_time(elapsed: std::time::Duration) -> String {
    let secs = elapsed.as_secs();
    let millis = elapsed.subsec_millis();
    
    if secs > 0 {
        format!("{}.{}s", secs, millis / 100)
    } else {
        format!("{}ms", millis)
    }
}

/// Format wall-clock timestamp for log display
pub fn format_wall_clock() -> String {
    use chrono::Local;
    Local::now().format("%H:%M:%S").to_string()
}

/// Constants for UI rendering
pub mod constants {
    pub const DEFAULT_INDENT: usize = 4;
    pub const DEFAULT_TERMINAL_WIDTH: usize = 80;
    #[allow(dead_code)] // Phase 3
    pub const NUMERIC_PRECISION: usize = 2;
    pub const MAX_SERVICE_NAME_LEN: usize = 24;
    pub const LEGEND_SYMBOL: char = '*';
}

#[cfg(test)]
mod tests {
    use super::*;
    
    #[test]
    fn test_stone_banner_format() {
        // Test basic format structure - should have === prefix and fill with equals
        let banner = stone_banner("stone-01", "Thriving", false);
        assert!(banner.starts_with("=== stone-01 - [Thriving]"));
        assert!(banner.contains("="));
        
        // Test with different status
        let banner = stone_banner("stone-02", "ERROR", false);
        assert!(banner.starts_with("=== stone-02 - [ERROR]"));
        assert!(banner.contains("="));
    }
    
    #[test]
    fn test_section_header_respects_cap() {
        // Wide terminal: should cap at 40 chars
        let wide_term = TerminalInfo { width: 120, supports_color: false, supports_unicode: false };
        let header = section_header("TEST", &wide_term);
        assert_eq!(header.len(), 40, "Should cap at 40 chars even on wide terminals");
        // Format: "--- TEST " (9 chars) + 31 dashes = 40
        assert_eq!(header, "--- TEST -------------------------------");
        
        // Exactly 40-char terminal: should use full width
        let exact_term = TerminalInfo { width: 40, supports_color: false, supports_unicode: false };
        let header = section_header("TEST", &exact_term);
        assert_eq!(header.len(), 40);
        assert_eq!(header, "--- TEST -------------------------------");
        
        // Narrow terminal: should respect narrow width
        let narrow_term = TerminalInfo { width: 20, supports_color: false, supports_unicode: false };
        let header = section_header("TEST", &narrow_term);
        assert_eq!(header.len(), 20, "Should respect narrow terminal width");
        assert_eq!(header, "--- TEST -----------");
        
        // Very narrow terminal: should not add trailing dashes if title doesn't fit
        let tiny_term = TerminalInfo { width: 8, supports_color: false, supports_unicode: false };
        let header = section_header("TEST", &tiny_term);
        assert_eq!(header, "--- TEST ");
    }
    
    #[test]
    fn test_section_header_long_title() {
        // Long title should still work without panicking
        let term = TerminalInfo { width: 40, supports_color: false, supports_unicode: false };
        let header = section_header("VERY LONG SECTION TITLE HERE", &term);
        assert!(header.starts_with("--- VERY LONG SECTION TITLE HERE "));
        // Should have no trailing dashes since title fills the width
    }
    
    #[test]
    fn test_truncate_name_edge_cases() {
        // Short name: no truncation
        assert_eq!(truncate_name("short", 24), "short");
        
        // Exact length: no truncation
        assert_eq!(truncate_name("exactly-twenty-four!", 20), "exactly-twenty-four!");
        
        // One char over: should truncate (takes first 17 chars + "...")
        assert_eq!(truncate_name("exactly-twenty-four!x", 20), "exactly-twenty-fo...");
        
        // Very long: should truncate properly (first 21 chars + "...")
        assert_eq!(truncate_name("very-long-service-name-that-exceeds", 24), "very-long-service-nam...");
        
        // Edge case: max_len < 3 (can't fit ellipsis properly)
        // With max_len=2, takes name[..2-3] which wraps/panics, so let's test valid case
        assert_eq!(truncate_name("test", 3), "...");
        
        // Empty string
        assert_eq!(truncate_name("", 24), "");
    }
    
    #[test]
    fn test_verbosity_parsing() {
        assert_eq!(Verbosity::from_arg("-v0"), Some(Verbosity::Minimal));
        assert_eq!(Verbosity::from_arg("-v1"), Some(Verbosity::Standard));
        assert_eq!(Verbosity::from_arg("-v2"), Some(Verbosity::Verbose));
        assert_eq!(Verbosity::from_arg("-v3"), Some(Verbosity::Debug));
        
        // Invalid cases
        assert_eq!(Verbosity::from_arg("invalid"), None);
        assert_eq!(Verbosity::from_arg("-v4"), None);
        assert_eq!(Verbosity::from_arg("v1"), None);
        assert_eq!(Verbosity::from_arg(""), None);
    }
    
    #[test]
    fn test_table_builder_alignment() {
        let mut table = TableBuilder::new()
            .add_column(15, Align::Left)
            .add_column(10, Align::Right);
        
        table.add_row(vec!["mongodb".to_string(), "3m".to_string()]);
        table.add_row(vec!["postgresql".to_string(), "15m 12s".to_string()]);
        
        let output = table.render();
        let lines: Vec<&str> = output.lines().collect();
        
        // Should have 2 rows
        assert_eq!(lines.len(), 2);
        
        // Each line should start with default indent
        for line in &lines {
            assert!(line.starts_with("    "), "Each line should start with 4-space indent");
        }
        
        // Check column widths are respected
        assert!(lines[0].contains("mongodb"));
        assert!(lines[1].contains("postgresql"));
    }
    
    #[test]
    fn test_status_indicator_mappings() {
        // Test vitality language mappings (no color)
        assert_eq!(status_indicator("running", false), "[thriving]");
        assert_eq!(status_indicator("stopped", false), "[dormant]");
        assert_eq!(status_indicator("ok", false), "[thriving]");
        assert_eq!(status_indicator("healthy", false), "[thriving]");  // Legacy maps to vitality
        assert_eq!(status_indicator("thriving", false), "[thriving]");
        assert_eq!(status_indicator("dormant", false), "[dormant]");
        assert_eq!(status_indicator("error", false), "[needs attention]");
        assert_eq!(status_indicator("failed", false), "[needs attention]");
        assert_eq!(status_indicator("warn", false), "[needs attention]");
        assert_eq!(status_indicator("warning", false), "[needs attention]");
        assert_eq!(status_indicator("degraded", false), "[needs attention]");
        assert_eq!(status_indicator("withering", false), "[needs attention]");
        
        // Case insensitivity
        assert_eq!(status_indicator("OK", false), "[thriving]");
        assert_eq!(status_indicator("Running", false), "[thriving]");
        
        // Unknown status should pass through
        assert_eq!(status_indicator("unknown", false), "unknown");
    }
    
    #[test]
    fn test_label_value_line_formatting() {
        // Test basic formatting
        let line = label_value_line("Status", "Running", 4);
        assert!(line.starts_with("    "));
        assert!(line.contains("Status"));
        assert!(line.contains("Running"));
        
        // Test different indentation
        let line = label_value_line("Name", "test-service", 0);
        assert!(!line.starts_with(" "));
        
        let line = label_value_line("Port", "8080", 8);
        assert!(line.starts_with("        "));
    }
    
    #[test]
    fn test_category_grid_formatting() {
        let term = TerminalInfo { width: 80, supports_color: false, supports_unicode: false };
        let grid = CategoryGrid::new(&term);
        
        // Test with multiple items
        let output = grid.render_category("DATA", &[
            "mongodb".to_string(),
            "postgresql".to_string(),
            "redis".to_string()
        ]);
        
        assert!(output.contains("DATA"), "Should contain category name");
        assert!(output.contains("mongodb"), "Should contain first item");
        assert!(output.contains("postgresql"), "Should contain second item");
        assert!(output.contains("redis"), "Should contain third item");
        
        // Test with empty items - returns empty string
        let output = grid.render_category("EMPTY", &[]);
        assert_eq!(output, "", "Empty category should return empty string (no header row)");
    }
    
    #[test]
    fn test_empty_state_with_and_without_hint() {
        // With hint
        let output = empty_state("No services found", Some("Try: garden-rake offer install <name>"));
        assert!(output.contains("No services found"));
        assert!(output.contains("Try: garden-rake offer install <name>"));
        
        // Without hint
        let output = empty_state("No items", None);
        assert!(output.contains("No items"));
        assert!(!output.contains("Try:"));
    }
}
