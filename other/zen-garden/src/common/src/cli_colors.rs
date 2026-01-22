//! CLI color helpers for Zen Garden tools
//!
//! Provides semantic color functions for consistent terminal output across
//! garden-rake and garden-moss. Uses ANSI escape codes directly to avoid
//! external dependencies.
//!
//! # Visual Dictionary
//!
//! ## Hierarchy (weight + brightness)
//! | Role      | Color        | Usage                          |
//! |-----------|--------------|--------------------------------|
//! | Title     | Bright White | GARDEN-RAKE, SURVEY            |
//! | Structure | Bold White   | DISCOVERY, SERVICES, ADOPTION  |
//! | Dividers  | Dim White    | ───────────, ═══════════       |
//!
//! ## Content (gray scale)
//! | Role         | Color      | Usage                          |
//! |--------------|------------|--------------------------------|
//! | Commands     | Light Gray | survey, adopt, prune           |
//! | Examples     | Light Gray | garden-rake survey --json      |
//! | Values       | White      | Discovery, 42, enabled         |
//! | Descriptions | Dim Gray   | View the current garden state  |
//!
//! ## Metadata (cyan)
//! | Role   | Color    | Usage                        |
//! |--------|----------|------------------------------|
//! | Labels | Cyan     | Category:, Vitality:, Stone: |
//! | Hints  | Dim Cyan | Use 'garden-rake <cmd>?' ... |
//!
//! ## Vitality Status (traffic light - reserved)
//! | Status          | Color    | Usage                           |
//! |-----------------|----------|---------------------------------|
//! | Thriving        | Green    | ● thriving, ✓ pass, running     |
//! | Needs Attention | Yellow   | ● needs attention, ⚠ warn       |
//! | Withering       | Red      | ● withering, ✗ fail, stopped    |
//! | Dormant         | Dim Gray | ● dormant, ? unknown            |
//!
//! ## Design Principles
//! - **Gray scale for content**: Commands/examples match what users type
//! - **Cyan for metadata**: Labels and hints are informational, not actionable
//! - **Traffic light reserved**: Green/Yellow/Red only for vitality status
//! - **Brightness = hierarchy**: Titles brightest, descriptions dimmest

use std::io::IsTerminal;

/// ANSI color codes for terminal output
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum AnsiColor {
    Reset,
    Bold,
    Dim,
    Red,
    Green,
    Yellow,
    Blue,
    Magenta,
    Cyan,
    White,
    BrightBlack, // Gray
    BrightWhite,
}

impl AnsiColor {
    /// Get the ANSI escape sequence for this color
    pub fn code(&self) -> &'static str {
        match self {
            Self::Reset => "\x1b[0m",
            Self::Bold => "\x1b[1m",
            Self::Dim => "\x1b[2m",
            Self::Red => "\x1b[31m",
            Self::Green => "\x1b[32m",
            Self::Yellow => "\x1b[33m",
            Self::Blue => "\x1b[34m",
            Self::Magenta => "\x1b[35m",
            Self::Cyan => "\x1b[36m",
            Self::White => "\x1b[37m",
            Self::BrightBlack => "\x1b[90m", // Gray
            Self::BrightWhite => "\x1b[97m",
        }
    }
}

/// Terminal color support detection result
#[derive(Debug, Clone)]
pub struct ColorSupport {
    /// Whether colors are supported
    pub enabled: bool,
}

impl ColorSupport {
    /// Detect terminal color support
    ///
    /// Checks:
    /// 1. NO_COLOR environment variable (universal disable)
    /// 2. Whether stdout is a terminal
    pub fn detect() -> Self {
        let no_color = std::env::var(crate::ENV_NO_COLOR).is_ok();
        let is_terminal = std::io::stdout().is_terminal();

        Self {
            enabled: !no_color && is_terminal,
        }
    }

    /// Create with colors explicitly enabled (for testing)
    #[allow(dead_code)]
    pub fn enabled() -> Self {
        Self { enabled: true }
    }

    /// Create with colors explicitly disabled
    #[allow(dead_code)]
    pub fn disabled() -> Self {
        Self { enabled: false }
    }
}

/// CLI text formatter with semantic color methods
///
/// Usage:
/// ```ignore
/// let fmt = CliFormatter::new();
/// println!("{}", fmt.command("offer"));
/// println!("{}", fmt.description("Install a service"));
/// println!("{}", fmt.group("SERVICES"));
/// ```
pub struct CliFormatter {
    support: ColorSupport,
}

impl CliFormatter {
    /// Create a new formatter with auto-detected color support
    pub fn new() -> Self {
        Self {
            support: ColorSupport::detect(),
        }
    }

    /// Create a formatter with explicit color support
    pub fn with_support(support: ColorSupport) -> Self {
        Self { support }
    }

    /// Check if colors are enabled
    pub fn colors_enabled(&self) -> bool {
        self.support.enabled
    }

    /// Apply color to text if colors are enabled
    fn apply(&self, text: &str, color: AnsiColor) -> String {
        if self.support.enabled {
            format!("{}{}{}", color.code(), text, AnsiColor::Reset.code())
        } else {
            text.to_string()
        }
    }

    /// Apply bold + color to text
    fn apply_bold(&self, text: &str, color: AnsiColor) -> String {
        if self.support.enabled {
            format!("{}{}{}{}", AnsiColor::Bold.code(), color.code(), text, AnsiColor::Reset.code())
        } else {
            text.to_string()
        }
    }

    // ========================================================================
    // HIERARCHY - Title, Structure, Dividers
    // ========================================================================

    /// Format a title (bright white) - highest hierarchy
    /// Usage: GARDEN-RAKE, SURVEY, main headers
    pub fn title(&self, text: &str) -> String {
        self.apply_bold(text, AnsiColor::BrightWhite)
    }

    /// Format a structure/category header (bold white)
    /// Usage: DISCOVERY, SERVICES, ADOPTION - section groupings
    pub fn group(&self, text: &str) -> String {
        self.apply_bold(text, AnsiColor::White)
    }

    /// Format a divider line (dim white)
    /// Usage: ───────────, ═══════════
    pub fn divider(&self, text: &str) -> String {
        if self.support.enabled {
            format!("{}{}{}", AnsiColor::Dim.code(), text, AnsiColor::Reset.code())
        } else {
            text.to_string()
        }
    }

    // ========================================================================
    // CONTENT - Commands, Examples, Values, Descriptions
    // ========================================================================

    /// Format a command name (light gray)
    /// Usage: survey, adopt, prune - what users type
    pub fn command(&self, text: &str) -> String {
        self.apply(text, AnsiColor::BrightBlack)
    }

    /// Format a syntax example (light gray)
    /// Usage: garden-rake survey --json - copy-paste ready
    pub fn example(&self, text: &str) -> String {
        self.apply(text, AnsiColor::BrightBlack)
    }

    /// Format a value (white)
    /// Usage: Discovery, 42, enabled - data and identifiers
    pub fn value(&self, text: &str) -> String {
        self.apply(text, AnsiColor::White)
    }

    /// Format a description (dim gray)
    /// Usage: View the current garden state - explanatory text
    pub fn description(&self, text: &str) -> String {
        if self.support.enabled {
            format!("{}{}{}", AnsiColor::Dim.code(), text, AnsiColor::Reset.code())
        } else {
            text.to_string()
        }
    }

    // ========================================================================
    // METADATA - Labels, Hints
    // ========================================================================

    /// Format a label (cyan)
    /// Usage: Category:, Vitality:, Stone: - metadata markers
    pub fn label(&self, text: &str) -> String {
        self.apply(text, AnsiColor::Cyan)
    }

    /// Format a hint (dim cyan)
    /// Usage: Use 'garden-rake <cmd>?' for help - tips and suggestions
    pub fn hint(&self, text: &str) -> String {
        if self.support.enabled {
            format!("{}{}{}{}", AnsiColor::Dim.code(), AnsiColor::Cyan.code(), text, AnsiColor::Reset.code())
        } else {
            text.to_string()
        }
    }

    // ========================================================================
    // VITALITY STATUS - Traffic Light (reserved for status only)
    // ========================================================================

    /// Format thriving status (green)
    /// Usage: ● thriving, ✓ pass, running - healthy state
    pub fn thriving(&self, text: &str) -> String {
        self.apply(text, AnsiColor::Green)
    }

    /// Format needs-attention status (yellow)
    /// Usage: ● needs attention, ⚠ warn, degraded - warning state
    pub fn needs_attention(&self, text: &str) -> String {
        self.apply(text, AnsiColor::Yellow)
    }

    /// Format withering status (red)
    /// Usage: ● withering, ✗ fail, stopped - error state
    pub fn withering(&self, text: &str) -> String {
        self.apply(text, AnsiColor::Red)
    }

    /// Format dormant status (dim gray)
    /// Usage: ● dormant, ? unknown - offline/unknown state
    pub fn dormant(&self, text: &str) -> String {
        if self.support.enabled {
            format!("{}{}{}", AnsiColor::Dim.code(), text, AnsiColor::Reset.code())
        } else {
            text.to_string()
        }
    }

    // ========================================================================
    // LEGACY ALIASES (for backward compatibility)
    // ========================================================================

    /// Alias for thriving() - use for success messages
    pub fn success(&self, text: &str) -> String {
        self.thriving(text)
    }

    /// Alias for needs_attention() - use for warning messages
    pub fn warning(&self, text: &str) -> String {
        self.needs_attention(text)
    }

    /// Alias for withering() - use for error messages
    pub fn error(&self, text: &str) -> String {
        self.withering(text)
    }

    /// Format a section header (cyan) - legacy, prefer group()
    pub fn section(&self, text: &str) -> String {
        self.apply(text, AnsiColor::Cyan)
    }

    /// Format emphasized text (cyan) - legacy
    pub fn emphasis(&self, text: &str) -> String {
        self.apply(text, AnsiColor::Cyan)
    }

    // ========================================================================
    // COMPOUND FORMATTERS
    // ========================================================================

    /// Format a command with its description: "command    description"
    pub fn command_line(&self, cmd: &str, desc: &str, width: usize) -> String {
        format!("{:<width$} {}", self.command(cmd), self.description(desc), width = width)
    }

    /// Format a key-value pair: "Key: value"
    pub fn kv(&self, key: &str, value: &str) -> String {
        format!("{}: {}", self.label(key), self.value(value))
    }
}

impl Default for CliFormatter {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_color_codes() {
        assert_eq!(AnsiColor::Reset.code(), "\x1b[0m");
        assert_eq!(AnsiColor::Green.code(), "\x1b[32m");
        assert_eq!(AnsiColor::BrightBlack.code(), "\x1b[90m");
    }

    #[test]
    fn test_formatter_disabled() {
        let fmt = CliFormatter::with_support(ColorSupport::disabled());
        assert_eq!(fmt.command("test"), "test");
        assert_eq!(fmt.description("desc"), "desc");
        assert_eq!(fmt.group("GROUP"), "GROUP");
    }

    #[test]
    fn test_formatter_enabled() {
        let fmt = CliFormatter::with_support(ColorSupport::enabled());
        // Command = light gray (BrightBlack)
        assert!(fmt.command("test").contains("\x1b[90m"));
        // Description = dim
        assert!(fmt.description("desc").contains("\x1b[2m"));
        // Group = bold white
        assert!(fmt.group("GROUP").contains("\x1b[1m")); // Bold
        assert!(fmt.group("GROUP").contains("\x1b[37m")); // White
    }

    #[test]
    fn test_vitality_colors() {
        let fmt = CliFormatter::with_support(ColorSupport::enabled());
        // Thriving = green
        assert!(fmt.thriving("healthy").contains("\x1b[32m"));
        // Needs attention = yellow
        assert!(fmt.needs_attention("degraded").contains("\x1b[33m"));
        // Withering = red
        assert!(fmt.withering("failed").contains("\x1b[31m"));
        // Dormant = dim
        assert!(fmt.dormant("unknown").contains("\x1b[2m"));
    }

    #[test]
    fn test_command_line_format() {
        let fmt = CliFormatter::with_support(ColorSupport::disabled());
        let line = fmt.command_line("offer", "Install a service", 20);
        assert!(line.contains("offer"));
        assert!(line.contains("Install a service"));
    }
}
