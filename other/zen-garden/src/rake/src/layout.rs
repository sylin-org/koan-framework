//! Tag-based layout system for CLI output
//!
//! Provides composable builders for consistent terminal output with semantic
//! indentation levels and optional styling tags.
//!
//! # Example
//! ```ignore
//! let layout = Layout::new();
//!
//! // Header with underline
//! println!("{}", layout.header("STONE INFO")
//!     .level(IndentLevel::Card)
//!     .underline()
//!     .render());
//!
//! // Field with aligned label:value
//! println!("{}", layout.field("ENDPOINT")
//!     .value("192.168.1.100:7185")
//!     .level(IndentLevel::Section)
//!     .render());
//!
//! // Verbose debug field
//! println!("{}", layout.field("API URL")
//!     .value(&url)
//!     .level(IndentLevel::Content)
//!     .tag("verbose")
//!     .render());
//! ```
//!
//! # Indent Levels
//! | Level   | Spaces | Usage                          |
//! |---------|--------|--------------------------------|
//! | Page    | 0      | Root banner, top-level headers |
//! | Card    | 4      | Stone cards, major sections    |
//! | Section | 8      | Subsections, field groups      |
//! | Content | 12     | Field values, list items       |
//! | Detail  | 16     | Nested details, verbose output |

use crate::ui::{constants, TerminalInfo};
use garden_common::cli_colors::CliFormatter;

/// Semantic indentation levels for consistent nesting
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum IndentLevel {
    /// Root level (0 spaces) - banners, top headers
    Page = 0,
    /// Card level (4 spaces) - stone cards, major sections
    Card = 1,
    /// Section level (8 spaces) - subsections within cards
    Section = 2,
    /// Content level (12 spaces) - field values, list items
    Content = 3,
    /// Detail level (16 spaces) - nested details, verbose output
    Detail = 4,
}

impl IndentLevel {
    /// Get the number of indent spaces for this level
    pub fn spaces(&self) -> usize {
        (*self as usize) * constants::DEFAULT_INDENT
    }

    /// Get the indent string for this level
    pub fn indent(&self) -> String {
        " ".repeat(self.spaces())
    }
}

/// Layout builder - entry point for creating layouts
///
/// Provides factory methods for creating header and field builders
/// with shared terminal info and formatting context.
pub struct Layout {
    term: TerminalInfo,
    fmt: CliFormatter,
}

impl Layout {
    /// Create a new layout with auto-detected terminal capabilities
    pub fn new() -> Self {
        Self {
            term: TerminalInfo::detect(),
            fmt: CliFormatter::new(),
        }
    }

    /// Create a layout with pre-detected terminal info
    pub fn with_term(term: TerminalInfo) -> Self {
        let fmt = CliFormatter::new();
        Self { term, fmt }
    }

    /// Get terminal info reference
    pub fn term(&self) -> &TerminalInfo {
        &self.term
    }

    /// Get formatter reference
    pub fn fmt(&self) -> &CliFormatter {
        &self.fmt
    }

    /// Create a header builder
    pub fn header<'a>(&'a self, title: &str) -> HeaderBuilder<'a> {
        HeaderBuilder::new(self, title)
    }

    /// Create a field builder
    pub fn field<'a>(&'a self, label: &str) -> FieldBuilder<'a> {
        FieldBuilder::new(self, label)
    }

    /// Create a line builder (plain text with indentation)
    pub fn line<'a>(&'a self, text: &str) -> LineBuilder<'a> {
        LineBuilder::new(self, text)
    }

    /// Create a status line builder (with status indicator prefix)
    pub fn status<'a>(&'a self, text: &str) -> StatusBuilder<'a> {
        StatusBuilder::new(self, text)
    }

    /// Print a blank line
    pub fn blank(&self) {
        println!();
    }
}

impl Default for Layout {
    fn default() -> Self {
        Self::new()
    }
}

// ============================================================================
// HEADER BUILDER
// ============================================================================

/// Builder for section headers with optional underline
///
/// Supports:
/// - Semantic indentation levels
/// - Optional underline (21-char default)
/// - Tags for styling/classification
pub struct HeaderBuilder<'a> {
    layout: &'a Layout,
    title: String,
    level: IndentLevel,
    underline: bool,
    underline_len: usize,
    tags: Vec<String>,
}

impl<'a> HeaderBuilder<'a> {
    const DEFAULT_UNDERLINE_LEN: usize = 21;

    fn new(layout: &'a Layout, title: &str) -> Self {
        Self {
            layout,
            title: title.to_string(),
            level: IndentLevel::Card, // Default to card level
            underline: false,
            underline_len: Self::DEFAULT_UNDERLINE_LEN,
            tags: Vec::new(),
        }
    }

    /// Set indentation level
    pub fn level(mut self, level: IndentLevel) -> Self {
        self.level = level;
        self
    }

    /// Enable underline below header
    pub fn underline(mut self) -> Self {
        self.underline = true;
        self
    }

    /// Set custom underline length
    pub fn underline_len(mut self, len: usize) -> Self {
        self.underline_len = len;
        self
    }

    /// Add a tag (can be called multiple times)
    pub fn tag(mut self, tag: &str) -> Self {
        self.tags.push(tag.to_string());
        self
    }

    /// Check if a specific tag is present
    pub fn has_tag(&self, tag: &str) -> bool {
        self.tags.iter().any(|t| t == tag)
    }

    /// Render the header to a string
    pub fn render(&self) -> String {
        let indent = self.level.indent();

        // Apply styling based on tags
        let title = if self.has_tag("dim") {
            self.layout.fmt.description(&self.title.to_uppercase())
        } else if self.has_tag("verbose") {
            format!("[verbose] {}", self.title)
        } else {
            self.layout.fmt.group(&self.title.to_uppercase())
        };

        if self.underline {
            let underline_char = if self.layout.term.supports_unicode {
                "─"
            } else {
                "-"
            };
            let underline = underline_char.repeat(self.underline_len);
            let underline_styled = self.layout.fmt.divider(&underline);
            format!("{}{}\n{}{}", indent, title, indent, underline_styled)
        } else {
            format!("{}{}", indent, title)
        }
    }

    /// Render and print the header
    pub fn print(&self) {
        println!("{}", self.render());
    }
}

// ============================================================================
// FIELD BUILDER
// ============================================================================

/// Builder for key-value field display with aligned columns
///
/// Default label width: 16 chars (matching existing kv_line)
pub struct FieldBuilder<'a> {
    layout: &'a Layout,
    label: String,
    value: Option<String>,
    level: IndentLevel,
    label_width: usize,
    tags: Vec<String>,
    uppercase_label: bool,
}

impl<'a> FieldBuilder<'a> {
    const DEFAULT_LABEL_WIDTH: usize = 16;

    fn new(layout: &'a Layout, label: &str) -> Self {
        Self {
            layout,
            label: label.to_string(),
            value: None,
            level: IndentLevel::Section, // Default to section level
            label_width: Self::DEFAULT_LABEL_WIDTH,
            tags: Vec::new(),
            uppercase_label: true, // Default to uppercase labels
        }
    }

    /// Set the field value
    pub fn value(mut self, value: impl Into<String>) -> Self {
        self.value = Some(value.into());
        self
    }

    /// Set indentation level
    pub fn level(mut self, level: IndentLevel) -> Self {
        self.level = level;
        self
    }

    /// Set custom label width (default: 16)
    pub fn label_width(mut self, width: usize) -> Self {
        self.label_width = width;
        self
    }

    /// Disable uppercase conversion for label
    pub fn lowercase(mut self) -> Self {
        self.uppercase_label = false;
        self
    }

    /// Add a tag (can be called multiple times)
    pub fn tag(mut self, tag: &str) -> Self {
        self.tags.push(tag.to_string());
        self
    }

    /// Check if a specific tag is present
    pub fn has_tag(&self, tag: &str) -> bool {
        self.tags.iter().any(|t| t == tag)
    }

    /// Render the field to a string
    pub fn render(&self) -> String {
        let indent = self.level.indent();
        let value = self.value.as_deref().unwrap_or("");

        let label = if self.uppercase_label {
            self.label.to_uppercase()
        } else {
            self.label.clone()
        };

        // Apply styling based on tags
        let (label_styled, value_styled) = if self.has_tag("verbose") {
            (
                format!("[verbose] {:<width$}", label, width = self.label_width.saturating_sub(10)),
                value.to_string(),
            )
        } else if self.has_tag("dim") {
            (
                self.layout.fmt.description(&format!("{:<width$}", label, width = self.label_width)),
                self.layout.fmt.description(value),
            )
        } else if self.has_tag("highlight") {
            (
                self.layout.fmt.label(&format!("{:<width$}", label, width = self.label_width)),
                self.layout.fmt.value(value),
            )
        } else {
            // Default: label in cyan, value plain
            (
                format!("{:<width$}", label, width = self.label_width),
                value.to_string(),
            )
        };

        format!("{}{} {}", indent, label_styled, value_styled)
    }

    /// Render and print the field
    pub fn print(&self) {
        println!("{}", self.render());
    }
}

// ============================================================================
// LINE BUILDER
// ============================================================================

/// Builder for plain text lines with indentation and optional styling
pub struct LineBuilder<'a> {
    layout: &'a Layout,
    text: String,
    level: IndentLevel,
    tags: Vec<String>,
}

impl<'a> LineBuilder<'a> {
    fn new(layout: &'a Layout, text: &str) -> Self {
        Self {
            layout,
            text: text.to_string(),
            level: IndentLevel::Card,
            tags: Vec::new(),
        }
    }

    /// Set indentation level
    pub fn level(mut self, level: IndentLevel) -> Self {
        self.level = level;
        self
    }

    /// Add a tag
    pub fn tag(mut self, tag: &str) -> Self {
        self.tags.push(tag.to_string());
        self
    }

    /// Check if a specific tag is present
    pub fn has_tag(&self, tag: &str) -> bool {
        self.tags.iter().any(|t| t == tag)
    }

    /// Render the line to a string
    pub fn render(&self) -> String {
        let indent = self.level.indent();

        let text = if self.has_tag("verbose") {
            format!("[verbose] {}", self.text)
        } else if self.has_tag("dim") {
            self.layout.fmt.description(&self.text)
        } else if self.has_tag("hint") {
            self.layout.fmt.hint(&self.text)
        } else {
            self.text.clone()
        };

        format!("{}{}", indent, text)
    }

    /// Render and print the line
    pub fn print(&self) {
        println!("{}", self.render());
    }
}

// ============================================================================
// STATUS BUILDER
// ============================================================================

/// Status indicator types
#[derive(Debug, Clone, Copy)]
pub enum StatusType {
    Ok,
    Info,
    Warn,
    Error,
    Pending,
}

impl StatusType {
    fn indicator(&self, color: bool) -> String {
        crate::ui::status_indicator(
            match self {
                Self::Ok => "ok",
                Self::Info => "info",
                Self::Warn => "warn",
                Self::Error => "error",
                Self::Pending => "pending",
            },
            color,
        )
    }
}

/// Builder for status lines with indicator prefix
///
/// Example: "    [thriving] Service started"
pub struct StatusBuilder<'a> {
    layout: &'a Layout,
    text: String,
    level: IndentLevel,
    status: StatusType,
    tags: Vec<String>,
}

impl<'a> StatusBuilder<'a> {
    fn new(layout: &'a Layout, text: &str) -> Self {
        Self {
            layout,
            text: text.to_string(),
            level: IndentLevel::Card,
            status: StatusType::Info,
            tags: Vec::new(),
        }
    }

    /// Set indentation level
    pub fn level(mut self, level: IndentLevel) -> Self {
        self.level = level;
        self
    }

    /// Set status type
    pub fn status_type(mut self, status: StatusType) -> Self {
        self.status = status;
        self
    }

    /// Shorthand for ok status
    pub fn ok(self) -> Self {
        self.status_type(StatusType::Ok)
    }

    /// Shorthand for info status
    pub fn info(self) -> Self {
        self.status_type(StatusType::Info)
    }

    /// Shorthand for warn status
    pub fn warn(self) -> Self {
        self.status_type(StatusType::Warn)
    }

    /// Shorthand for error status
    pub fn error(self) -> Self {
        self.status_type(StatusType::Error)
    }

    /// Shorthand for pending status
    pub fn pending(self) -> Self {
        self.status_type(StatusType::Pending)
    }

    /// Add a tag
    pub fn tag(mut self, tag: &str) -> Self {
        self.tags.push(tag.to_string());
        self
    }

    /// Check if a specific tag is present
    pub fn has_tag(&self, tag: &str) -> bool {
        self.tags.iter().any(|t| t == tag)
    }

    /// Render the status line to a string
    pub fn render(&self) -> String {
        let indent = self.level.indent();
        let indicator = self.status.indicator(self.layout.term.supports_color);

        let text = if self.has_tag("verbose") {
            format!("[verbose] {}", self.text)
        } else {
            self.text.clone()
        };

        format!("{}{} {}", indent, indicator, text)
    }

    /// Render and print the status line
    pub fn print(&self) {
        println!("{}", self.render());
    }
}

// ============================================================================
// TESTS
// ============================================================================

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_indent_levels() {
        assert_eq!(IndentLevel::Page.spaces(), 0);
        assert_eq!(IndentLevel::Card.spaces(), 4);
        assert_eq!(IndentLevel::Section.spaces(), 8);
        assert_eq!(IndentLevel::Content.spaces(), 12);
        assert_eq!(IndentLevel::Detail.spaces(), 16);
    }

    #[test]
    fn test_header_render() {
        let layout = Layout::new();

        // Basic header
        let output = layout.header("TEST").level(IndentLevel::Card).render();
        assert!(output.starts_with("    ")); // 4 spaces
        assert!(output.contains("TEST"));

        // Header with underline
        let output = layout
            .header("SECTION")
            .level(IndentLevel::Section)
            .underline()
            .render();
        assert!(output.starts_with("        ")); // 8 spaces
        assert!(output.contains("SECTION"));
        assert!(output.contains('\n')); // Has underline
    }

    #[test]
    fn test_field_render() {
        let layout = Layout::new();

        // Basic field
        let output = layout
            .field("ENDPOINT")
            .value("192.168.1.100")
            .level(IndentLevel::Section)
            .render();
        assert!(output.starts_with("        ")); // 8 spaces
        assert!(output.contains("ENDPOINT"));
        assert!(output.contains("192.168.1.100"));
    }

    #[test]
    fn test_field_alignment() {
        let layout = Layout::new();

        // Check that label is padded to 16 chars
        let output = layout
            .field("IP")
            .value("10.0.0.1")
            .level(IndentLevel::Page)
            .render();

        // "IP" should be padded
        // Format: "{indent}{label:<16} {value}"
        assert!(output.contains("IP              "));
    }

    #[test]
    fn test_status_render() {
        let layout = Layout::new();

        let output = layout
            .status("Service started")
            .level(IndentLevel::Card)
            .ok()
            .render();
        assert!(output.starts_with("    ")); // 4 spaces
        assert!(output.contains("[thriving]"));
        assert!(output.contains("Service started"));
    }

    #[test]
    fn test_tags() {
        let layout = Layout::new();

        // Verbose tag adds prefix
        let output = layout.line("Debug info").tag("verbose").render();
        assert!(output.contains("[verbose]"));

        // Field with verbose tag
        let output = layout.field("URL").value("http://localhost").tag("verbose").render();
        assert!(output.contains("[verbose]"));
    }
}
