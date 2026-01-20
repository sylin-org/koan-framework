//! Console output module for Moss
//! 
//! Provides structured console events with multiple output modes (Silent, Minimal, Informative, Verbose).
//! Supports remote console control via API and graceful deduplication of high-frequency events.
//!
//! Also provides legacy functions for first-boot TTY output.

use std::fs::OpenOptions;
use std::io::{Write, IsTerminal};
use std::sync::{Arc, RwLock};
use std::collections::HashMap;
use std::time::Instant;
use anyhow::{Context, Result};
use chrono::Local;

// ================================================================================================
// CONSOLE EVENT SYSTEM
// ================================================================================================

/// Console output mode - determines what events are displayed
#[derive(Debug, Clone, Copy, PartialEq, Eq, serde::Serialize, serde::Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum ConsoleMode {
    /// No console output (Windows service, systemd with no TTY)
    Silent,
    /// Startup + critical events only (daemon default)
    Minimal,
    /// Major lifecycle events (interactive default)
    Informative,
    /// Full debug output (opt-in)
    Verbose,
}

impl Default for ConsoleMode {
    fn default() -> Self {
        Self::Minimal
    }
}

impl std::fmt::Display for ConsoleMode {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            Self::Silent => write!(f, "silent"),
            Self::Minimal => write!(f, "minimal"),
            Self::Informative => write!(f, "informative"),
            Self::Verbose => write!(f, "verbose"),
        }
    }
}

impl std::str::FromStr for ConsoleMode {
    type Err = anyhow::Error;
    
    fn from_str(s: &str) -> Result<Self, Self::Err> {
        match s.to_lowercase().as_str() {
            "silent" => Ok(Self::Silent),
            "minimal" => Ok(Self::Minimal),
            "informative" => Ok(Self::Informative),
            "verbose" => Ok(Self::Verbose),
            _ => Err(anyhow::anyhow!("Invalid console mode: {}", s)),
        }
    }
}

/// Event categories for structured console output
#[allow(dead_code)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum EventCategory {
    System,
    Config,
    Manifests,
    Offerings,
    Services,
    Jobs,
    Storage,
    Network,
    Docker,
    Discovery,
    Health,
    API,
    Security,
    Ops,
    Cluster,
}

impl EventCategory {
    /// Get the padded display name (9 characters)
    pub fn display_name(&self) -> &'static str {
        match self {
            Self::System => "System   ",
            Self::Config => "Config   ",
            Self::Manifests => "Manifests",
            Self::Offerings => "Offerings",
            Self::Services => "Services ",
            Self::Jobs => "Jobs     ",
            Self::Storage => "Storage  ",
            Self::Network => "Network  ",
            Self::Docker => "Docker   ",
            Self::Discovery => "Discovery",
            Self::Health => "Health   ",
            Self::API => "API      ",
            Self::Security => "Security ",
            Self::Ops => "Ops      ",
            Self::Cluster => "Cluster  ",
        }
    }
    
    /// Get color hint for this category (DRY - single source of truth)
    pub fn color_hint(&self) -> AnsiColor {
        match self {
            Self::System => AnsiColor::Cyan,
            Self::Config => AnsiColor::Blue,
            Self::Manifests => AnsiColor::Magenta,
            Self::Offerings => AnsiColor::Magenta,
            Self::Services => AnsiColor::Green,
            Self::Jobs => AnsiColor::Yellow,
            Self::Storage => AnsiColor::Blue,
            Self::Network => AnsiColor::Cyan,
            Self::Docker => AnsiColor::Blue,
            Self::Discovery => AnsiColor::Cyan,
            Self::Health => AnsiColor::Green,
            Self::API => AnsiColor::White,
            Self::Security => AnsiColor::Red,
            Self::Ops => AnsiColor::Yellow,
            Self::Cluster => AnsiColor::Magenta,
        }
    }
}

/// Event status for structured console output
#[allow(dead_code)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum EventStatus {
    // System
    Starting, Ready, Shutting, Stopped, FirstBoot, FirstBootDone, FsReady, FsError, FsPending,
    FsWritable, FsRemount, SignalReceived, AdminShutdown, Draining, PreinstallComplete,
    HttpError, Connected,
    
    // Config
    Reading, Loaded, Merged, ParseError, ReadError, NotFound, PreinstallFound, PreinstallLoaded,
    PreinstallError,
    
    // Manifests
    Scanning, Found, Loading, Parsed, Validated, CompatRules, Refresh, Updated, Invalid,
    DirFound, DirMissing, TryingCompose, CompatError, NoCompat,
    
    // Offerings
    Building, Built, BuildError, Planting, RebuildError,
    
    // Services (reuse Starting/Stopped from System)
    Requesting, Pulling, Creating, Running, Healthy, Stopping, Removing, Removed,
    Upgrading, Upgraded, Adopting, OrphanFound, NoManifest, AdoptError, ScanComplete, ScanError,
    ListError, CleanupError, StopError, StartError, UpgradeError,
    
    // Jobs (reuse Retry from Docker, CompatError from Manifests)
    Queued, Started, Progress, Completed, Failed, Cancelled, PreinstallDone,
    
    // Storage
    Saving, SaveError, LoadError, DeleteError, WriteError, MkdirError, ChmodError, MoveError,
    
    // Network
    Listening, Binding, BindError,
    
    // Docker
    Disconnected, PullProgress, PullComplete, Retry, ImagePull,
    
    // Discovery
    Request, Response, MdnsActive, MdnsError, UdpError, LanternReg, LanternUnreachable,
    LanternError, LanternFatal,
    
    // Health
    Ok, Degraded, Unhealthy,
    
    // API
    Post, Get, Put, Delete, SseLag, LogStreamError,
    
    // Security
    KeystoneGen, KeystoneLoad, KeystoneExp, AuthEnable, AuthDisable, AuthSuccess, AuthDenied,
    AuthExpired, RateLimited, StoneTrust, StoneReject, TlsEnabled,
    
    // Ops (remove duplicate Validated)
    Active, Cordon, DrainStart, DrainDone, Uncordon, RetireSched, RetireStart, RetireDone,
    StoneJoin, StoneLeave, RefreshReq, DecodeError, ValidationError, UnknownComponent,
    Staged, RestartReq, RestartTriggered, RestartWarning, RestartError, ShutdownReq, ShutdownDone,
    ShutdownTimeout, ShutdownError, Kill, Conflict, ForceFlag, ForceError,
    
    // Cluster  
    Joined, Left, LeaderElected, LeaderLost, Syncing, Synced,
}

impl EventStatus {
    /// Get the padded display name (14 characters)
    pub fn display_name(&self) -> &'static str {
        match self {
            // System
            Self::Starting => "STARTING      ",
            Self::Ready => "READY         ",
            Self::Shutting => "SHUTTING_DOWN ",
            Self::Stopped => "STOPPED       ",
            Self::FirstBoot => "FIRST_BOOT    ",
            Self::FirstBootDone => "FIRST_BOOT_DONE",
            Self::FsReady => "FS_READY      ",
            Self::FsError => "FS_ERROR      ",
            Self::FsPending => "FS_PENDING    ",
            Self::FsWritable => "FS_WRITABLE   ",
            Self::FsRemount => "FS_REMOUNT    ",
            Self::SignalReceived => "SIGNAL_RCVD   ",
            Self::AdminShutdown => "ADMIN_SHUTDOWN",
            Self::Draining => "DRAINING      ",
            Self::PreinstallComplete => "PREINSTALL_OK ",
            Self::HttpError => "HTTP_ERROR    ",
            Self::Connected => "CONNECTED     ",
            
            // Config
            Self::Reading => "READING       ",
            Self::Loaded => "LOADED        ",
            Self::Merged => "MERGED        ",
            Self::ParseError => "PARSE_ERROR   ",
            Self::ReadError => "READ_ERROR    ",
            Self::NotFound => "NOT_FOUND     ",
            Self::PreinstallFound => "PREINSTALL_FOUND",
            Self::PreinstallLoaded => "PREINSTALL_LOADED",
            Self::PreinstallError => "PREINSTALL_ERR",
            
            // Manifests
            Self::Scanning => "SCANNING      ",
            Self::Found => "FOUND         ",
            Self::Loading => "LOADING       ",
            Self::Parsed => "PARSED        ",
            Self::Validated => "VALIDATED     ",
            Self::CompatRules => "COMPAT_RULES  ",
            Self::Refresh => "REFRESH       ",
            Self::Updated => "UPDATED       ",
            Self::Invalid => "INVALID       ",
            Self::DirFound => "DIR_FOUND     ",
            Self::DirMissing => "DIR_MISSING   ",
            Self::TryingCompose => "TRYING_COMPOSE",
            Self::CompatError => "COMPAT_ERROR  ",
            Self::NoCompat => "NO_COMPAT     ",
            
            // Offerings
            Self::Building => "BUILDING      ",
            Self::Built => "BUILT         ",
            Self::BuildError => "BUILD_ERROR   ",
            Self::Planting => "PLANTING      ",
            Self::RebuildError => "REBUILD_ERROR ",
            
            // Services (Starting/Stopped are in System, DeleteError in Storage)
            Self::Requesting => "REQUESTING    ",
            Self::Pulling => "PULLING       ",
            Self::Creating => "CREATING      ",
            Self::Running => "RUNNING       ",
            Self::Healthy => "HEALTHY       ",
            Self::Stopping => "STOPPING      ",
            Self::Removing => "REMOVING      ",
            Self::Removed => "REMOVED       ",
            Self::Upgrading => "UPGRADING     ",
            Self::Upgraded => "UPGRADED      ",
            Self::Adopting => "ADOPTING      ",
            Self::OrphanFound => "ORPHAN_FOUND  ",
            Self::NoManifest => "NO_MANIFEST   ",
            Self::AdoptError => "ADOPT_ERROR   ",
            Self::ScanComplete => "SCAN_COMPLETE ",
            Self::ScanError => "SCAN_ERROR    ",
            Self::ListError => "LIST_ERROR    ",
            Self::CleanupError => "CLEANUP_ERROR ",
            Self::StopError => "STOP_ERROR    ",
            Self::StartError => "START_ERROR   ",
            Self::UpgradeError => "UPGRADE_ERROR ",
            
            // Jobs (Retry in Docker, CompatError in Manifests)
            Self::Queued => "QUEUED        ",
            Self::Started => "STARTED       ",
            Self::Progress => "PROGRESS      ",
            Self::Completed => "COMPLETED     ",
            Self::Failed => "FAILED        ",
            Self::Cancelled => "CANCELLED     ",
            Self::PreinstallDone => "PREINSTALL_DONE",
            
            // Storage
            Self::Saving => "SAVING        ",
            Self::SaveError => "SAVE_ERROR    ",
            Self::LoadError => "LOAD_ERROR    ",
            Self::DeleteError => "DELETE_ERROR  ",
            Self::WriteError => "WRITE_ERROR   ",
            Self::MkdirError => "MKDIR_ERROR   ",
            Self::ChmodError => "CHMOD_ERROR   ",
            Self::MoveError => "MOVE_ERROR    ",
            
            // Network
            Self::Listening => "LISTENING     ",
            Self::Binding => "BINDING       ",
            Self::BindError => "BIND_ERROR    ",
            
            // Docker (Connected is in System)
            Self::Disconnected => "DISCONNECTED  ",
            Self::PullProgress => "PULL_PROGRESS ",
            Self::PullComplete => "PULL_COMPLETE ",
            Self::Retry => "RETRY         ",
            Self::ImagePull => "IMAGE_PULL    ",
            
            // Discovery
            Self::Request => "REQUEST       ",
            Self::Response => "RESPONSE      ",
            Self::MdnsActive => "MDNS_ACTIVE   ",
            Self::MdnsError => "MDNS_ERROR    ",
            Self::UdpError => "UDP_ERROR     ",
            Self::LanternReg => "LANTERN_REG   ",
            Self::LanternUnreachable => "LANTERN_UNREACH",
            Self::LanternError => "LANTERN_ERROR ",
            Self::LanternFatal => "LANTERN_FATAL ",
            
            // Health
            Self::Ok => "OK            ",
            Self::Degraded => "DEGRADED      ",
            Self::Unhealthy => "UNHEALTHY     ",
            
            // API
            Self::Post => "POST          ",
            Self::Get => "GET           ",
            Self::Put => "PUT           ",
            Self::Delete => "DELETE        ",
            Self::SseLag => "SSE_LAG       ",
            Self::LogStreamError => "LOG_STREAM_ERR",
            
            // Security
            Self::KeystoneGen => "KEYSTONE_GEN  ",
            Self::KeystoneLoad => "KEYSTONE_LOAD ",
            Self::KeystoneExp => "KEYSTONE_EXP  ",
            Self::AuthEnable => "AUTH_ENABLE   ",
            Self::AuthDisable => "AUTH_DISABLE  ",
            Self::AuthSuccess => "AUTH_SUCCESS  ",
            Self::AuthDenied => "AUTH_DENIED   ",
            Self::AuthExpired => "AUTH_EXPIRED  ",
            Self::RateLimited => "RATE_LIMITED  ",
            Self::StoneTrust => "STONE_TRUST   ",
            Self::StoneReject => "STONE_REJECT  ",
            Self::TlsEnabled => "TLS_ENABLED   ",
            
            // Ops
            Self::Active => "ACTIVE        ",
            Self::Cordon => "CORDON        ",
            Self::DrainStart => "DRAIN_START   ",
            Self::DrainDone => "DRAIN_DONE    ",
            Self::Uncordon => "UNCORDON      ",
            Self::RetireSched => "RETIRE_SCHED  ",
            Self::RetireStart => "RETIRE_START  ",
            Self::RetireDone => "RETIRE_DONE   ",
            Self::StoneJoin => "STONE_JOIN    ",
            Self::StoneLeave => "STONE_LEAVE   ",
            Self::RefreshReq => "REFRESH_REQ   ",
            Self::DecodeError => "DECODE_ERROR  ",
            Self::ValidationError => "VALIDATION_ERR",
            Self::UnknownComponent => "UNKNOWN_COMP  ",
            Self::Staged => "STAGED        ",
            Self::RestartReq => "RESTART_REQ   ",
            Self::RestartTriggered => "RESTART_TRIG  ",
            Self::RestartWarning => "RESTART_WARN  ",
            Self::RestartError => "RESTART_ERROR ",
            Self::ShutdownReq => "SHUTDOWN_REQ  ",
            Self::ShutdownDone => "SHUTDOWN_DONE ",
            Self::ShutdownTimeout => "SHUTDOWN_TMOUT",
            Self::ShutdownError => "SHUTDOWN_ERROR",
            Self::Kill => "KILL          ",
            Self::Conflict => "CONFLICT      ",
            Self::ForceFlag => "FORCE_FLAG    ",
            Self::ForceError => "FORCE_ERROR   ",
            
            // Cluster
            Self::Joined => "JOINED        ",
            Self::Left => "LEFT          ",
            Self::LeaderElected => "LEADER_ELECTED",
            Self::LeaderLost => "LEADER_LOST   ",
            Self::Syncing => "SYNCING       ",
            Self::Synced => "SYNCED        ",
        }
    }
    
    /// Determine if this status represents an error/failure
    pub fn is_error(&self) -> bool {
        matches!(self,
            Self::ParseError | Self::ReadError | Self::BuildError | Self::RebuildError |
            Self::Failed | Self::SaveError | Self::LoadError | Self::DeleteError |
            Self::WriteError | Self::MkdirError | Self::ChmodError | Self::MoveError |
            Self::BindError | Self::MdnsError | Self::UdpError | Self::LanternError |
            Self::LanternFatal | Self::Unhealthy | Self::LogStreamError | Self::DecodeError |
            Self::ValidationError | Self::RestartError | Self::ShutdownError | Self::ShutdownTimeout |
            Self::FsError | Self::HttpError | Self::CleanupError | Self::StopError |
            Self::StartError | Self::UpgradeError | Self::ScanError |
            Self::ListError | Self::AdoptError | Self::CompatError | Self::PreinstallError |
            Self::Invalid | Self::NoManifest | Self::DirMissing |
            Self::ForceError
        )
    }
    
    /// Determine if this status represents a warning
    pub fn is_warning(&self) -> bool {
        matches!(self,
            Self::Retry | Self::Degraded | Self::RestartWarning | Self::FsPending |
            Self::LanternUnreachable | Self::OrphanFound | Self::Conflict | Self::ForceFlag
        )
    }
    
    /// Determine if this status represents success/completion
    pub fn is_success(&self) -> bool {
        matches!(self,
            Self::Ready | Self::Completed | Self::Loaded | Self::Built | Self::Validated |
            Self::Healthy | Self::Connected | Self::Upgraded | Self::FirstBootDone |
            Self::FsReady | Self::PreinstallComplete | Self::ShutdownDone | Self::Synced |
            Self::PullComplete | Self::Ok | Self::Joined | Self::LeaderElected |
            Self::FsWritable | Self::PreinstallDone
        )
    }
    
    /// Get severity hint for this status (DRY - single source of truth for semantic level)
    pub fn severity_hint(&self) -> Severity {
        if self.is_error() {
            Severity::Error
        } else if self.is_warning() {
            Severity::Warning
        } else if self.is_success() {
            Severity::Info
        } else {
            Severity::Debug // In-progress, reading, scanning, etc.
        }
    }
}

/// A structured console event
#[derive(Debug, Clone)]
/// Console event with formatting hints for different output contexts
pub struct ConsoleEvent {
    pub timestamp: chrono::DateTime<Local>,
    pub category: EventCategory,
    pub status: EventStatus,
    pub target: String,
    pub details: Option<String>,
}

impl ConsoleEvent {
    pub fn new(category: EventCategory, status: EventStatus, target: impl Into<String>) -> Self {
        Self {
            timestamp: Local::now(),
            category,
            status,
            target: target.into(),
            details: None,
        }
    }
    
    #[allow(dead_code)]
    pub fn with_details(mut self, details: impl Into<String>) -> Self {
        self.details = Some(details.into());
        self
    }
    
    /// Get formatting hint for this event (DRY - single source of truth)
    pub fn format_hint(&self) -> FormatHint {
        FormatHint {
            category_color: self.category.color_hint(),
            status_severity: self.status.severity_hint(),
            should_bold: self.status.is_error() || matches!(self.status, EventStatus::Starting | EventStatus::Ready | EventStatus::Stopped),
        }
    }
}

/// Formatting hints for rendering events in different contexts (TTY, SSE, JSON, logs)
#[allow(dead_code)]
#[derive(Debug, Clone, Copy)]
pub struct FormatHint {
    pub category_color: AnsiColor,
    pub status_severity: Severity,
    pub should_bold: bool,
}

/// ANSI color hints (consumers can ignore if not supported)
#[derive(Debug, Clone, Copy, PartialEq)]
pub enum AnsiColor {
    Reset,
    Red,
    Green,
    Yellow,
    Blue,
    Cyan,
    Magenta,
    White,
}

impl AnsiColor {
    /// Get ANSI escape code (only for TTY consumers)
    pub fn ansi_code(&self) -> &'static str {
        match self {
            Self::Reset => "\x1b[0m",
            Self::Red => "\x1b[31m",
            Self::Green => "\x1b[32m",
            Self::Yellow => "\x1b[33m",
            Self::Blue => "\x1b[34m",
            Self::Cyan => "\x1b[36m",
            Self::Magenta => "\x1b[35m",
            Self::White => "\x1b[37m",
        }
    }
}

/// Severity level for status (semantic, not visual)
#[allow(dead_code)]
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord)]
pub enum Severity {
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4,
}

/// Event deduplicator to prevent high-frequency event spam
struct EventDeduplicator {
    seen: HashMap<(EventCategory, EventStatus, String), Instant>,
    ttl_seconds: u64,
}

impl EventDeduplicator {
    fn new(ttl_seconds: u64) -> Self {
        Self {
            seen: HashMap::new(),
            ttl_seconds,
        }
    }
    
    /// Check if event should be emitted (returns true) or suppressed (returns false)
    fn should_emit(&mut self, event: &ConsoleEvent) -> bool {
        let key = (event.category, event.status, event.target.clone());
        let now = Instant::now();
        
        // Clean up expired entries (simple approach: check on each call)
        self.seen.retain(|_, last_seen| now.duration_since(*last_seen).as_secs() < self.ttl_seconds);
        
        // Check if we've seen this event recently
        if let Some(last_seen) = self.seen.get(&key) {
            if now.duration_since(*last_seen).as_secs() < self.ttl_seconds {
                return false; // Suppress duplicate
            }
        }
        
        // Record this event
        self.seen.insert(key, now);
        true
    }
}

/// Output formatter trait for different rendering contexts (DRY principle)
pub trait OutputFormatter {
    /// Format an event for the specific output context
    fn format(&self, event: &ConsoleEvent) -> String;
}

/// TTY console formatter (supports colors)
pub struct TtyFormatter {
    color_enabled: bool,
}

impl TtyFormatter {
    pub fn new() -> Self {
        Self {
            color_enabled: Self::detect_color_support(),
        }
    }
    
    /// Detect if terminal supports colors
    fn detect_color_support() -> bool {
        std::env::var("NO_COLOR").is_err() && 
        std::io::stdin().is_terminal()
    }
}

impl OutputFormatter for TtyFormatter {
    fn format(&self, event: &ConsoleEvent) -> String {
        let time_str = event.timestamp.format("%H:%M:%S");
        let category_str = event.category.display_name();
        let status_str = event.status.display_name();
        
        let base = format!(
            "{} {} │ {} │ {}",
            time_str,
            category_str,
            status_str,
            event.target
        );
        
        let with_details = if let Some(ref details) = event.details {
            format!("{} → {}", base, details)
        } else {
            base
        };
        
        // Apply colors based on format hints
        if self.color_enabled {
            let hint = event.format_hint();
            let color = match hint.status_severity {
                Severity::Error | Severity::Critical => AnsiColor::Red,
                Severity::Warning => AnsiColor::Yellow,
                Severity::Info => AnsiColor::Green,
                Severity::Debug => AnsiColor::Cyan,
            };
            
            let bold = if hint.should_bold { "\x1b[1m" } else { "" };
            format!("{}{}{}{}", bold, color.ansi_code(), with_details, AnsiColor::Reset.ansi_code())
        } else {
            with_details
        }
    }
}

/// SSE stream formatter (no colors, structured for event streaming)
#[allow(dead_code)]
pub struct SseFormatter;

impl OutputFormatter for SseFormatter {
    fn format(&self, event: &ConsoleEvent) -> String {
        let time_str = event.timestamp.format("%H:%M:%S");
        let hint = event.format_hint();
        
        // Structured format with severity prefix for SSE consumers
        let severity_prefix = match hint.status_severity {
            Severity::Error | Severity::Critical => "[ERROR]",
            Severity::Warning => "[WARN]",
            Severity::Info => "[INFO]",
            Severity::Debug => "[DEBUG]",
        };
        
        format!(
            "{} {} {} │ {} │ {}{}",
            time_str,
            severity_prefix,
            event.category.display_name().trim(),
            event.status.display_name().trim(),
            event.target,
            event.details.as_ref().map(|d| format!(" → {}", d)).unwrap_or_default()
        )
    }
}

/// Console printer with pluggable formatters
pub struct ConsolePrinter {
    mode: Arc<RwLock<ConsoleMode>>,
    deduplicator: Arc<RwLock<EventDeduplicator>>,
    formatter: Box<dyn OutputFormatter + Send + Sync>,
}

impl ConsolePrinter {
    #[allow(dead_code)]
    pub fn new(mode: ConsoleMode) -> Self {
        Self::with_dedup_ttl(mode, 10)
    }
    
    /// Create console printer with custom deduplication TTL
    pub fn with_dedup_ttl(mode: ConsoleMode, dedup_ttl_secs: u64) -> Self {
        Self {
            mode: Arc::new(RwLock::new(mode)),
            deduplicator: Arc::new(RwLock::new(EventDeduplicator::new(dedup_ttl_secs))),
            formatter: Box::new(TtyFormatter::new()),
        }
    }
    
    /// Create console printer with custom formatter (for SSE, API, etc.)
    #[allow(dead_code)]
    pub fn with_formatter(mode: ConsoleMode, formatter: Box<dyn OutputFormatter + Send + Sync>) -> Self {
        Self {
            mode: Arc::new(RwLock::new(mode)),
            deduplicator: Arc::new(RwLock::new(EventDeduplicator::new(10))),
            formatter,
        }
    }
    
    /// Update console mode (for remote control)
    pub fn set_mode(&self, mode: ConsoleMode) {
        if let Ok(mut m) = self.mode.write() {
            *m = mode;
        }
    }
    
    /// Get current console mode
    pub fn get_mode(&self) -> ConsoleMode {
        self.mode.read().map(|m| *m).unwrap_or_default()
    }
    
    /// Emit a console event (respects mode filtering and deduplication)
    pub fn emit(&self, event: ConsoleEvent) {
        let mode = self.get_mode();
        
        // Filter by mode
        if !self.should_display(&event, mode) {
            return;
        }
        
        // Check deduplication for high-frequency events
        let should_emit = {
            let mut dedup = self.deduplicator.write().unwrap();
            dedup.should_emit(&event)
        };
        
        if !should_emit {
            return;
        }
        
        // Format using pluggable formatter and print
        let formatted = self.formatter.format(&event);
        println!("{}", formatted);
    }
    
    /// Determine if event should be displayed in given mode
    fn should_display(&self, event: &ConsoleEvent, mode: ConsoleMode) -> bool {
        match mode {
            ConsoleMode::Silent => false,
            ConsoleMode::Minimal => {
                // Only critical system events
                matches!(event.category, EventCategory::System) && 
                matches!(event.status, 
                    EventStatus::Starting | EventStatus::Ready | EventStatus::Stopped |
                    EventStatus::FirstBoot | EventStatus::FirstBootDone | EventStatus::FsError
                )
            }
            ConsoleMode::Informative => {
                // All high-level lifecycle events (exclude verbose-only)
                // Special case: Docker | CONNECTED is visible, Services | CONNECTED is not
                if event.status == EventStatus::Connected {
                    matches!(event.category, EventCategory::Docker)
                } else {
                    !matches!(event.status,
                        EventStatus::Reading | EventStatus::TryingCompose | EventStatus::NoCompat |
                        EventStatus::LanternUnreachable | EventStatus::SseLag
                    )
                }
            }
            ConsoleMode::Verbose => true, // Show everything
        }
    }
}

/// Detect platform-appropriate console mode default
pub fn detect_platform_console_mode() -> ConsoleMode {
    // Windows service detection
    #[cfg(target_os = "windows")]
    {
        if std::env::var("USERDOMAIN").is_ok() && !std::io::stdin().is_terminal() {
            return ConsoleMode::Silent; // Windows service
        }
        if std::io::stdin().is_terminal() {
            return ConsoleMode::Informative; // Windows interactive
        }
    }
    
    // Linux systemd/interactive detection
    #[cfg(not(target_os = "windows"))]
    {
        // Check for systemd without TTY
        if std::env::var("INVOCATION_ID").is_ok() && !std::io::stdin().is_terminal() {
            return ConsoleMode::Minimal; // systemd daemon
        }
        
        if std::io::stdin().is_terminal() {
            return ConsoleMode::Informative; // Interactive terminal
        }
    }
    
    ConsoleMode::Minimal // Safe default
}

// ================================================================================================
// LEGACY FIRST-BOOT TTY FUNCTIONS (preserved for backward compatibility)
// ================================================================================================

/// Ensure /etc is writable with retries for early-boot timing issues
/// Returns Ok(true) if writeable, Ok(false) if permanently read-only
pub async fn ensure_etc_writable() -> Result<bool> {
    const MAX_RETRIES: u32 = 10;
    const RETRY_DELAY_MS: u64 = 500;
    
    let test_path = "/etc/.moss-write-test";
    
    for attempt in 1..=MAX_RETRIES {
        match std::fs::write(test_path, "test") {
            Ok(_) => {
                // Writable - cleanup test file
                let _ = std::fs::remove_file(test_path);
                if attempt > 1 {
                    tracing::info!(attempt, "/ etc became writable after retries");
                }
                return Ok(true);
            }
            Err(e) if e.kind() == std::io::ErrorKind::PermissionDenied || 
                       e.raw_os_error() == Some(30) => { // EROFS = 30
                
                if attempt == 1 {
                    tracing::warn!("/etc is not yet writable, will retry (may be early boot timing)");
                }
                
                // On first attempt, try remounting
                if attempt == 1 {
                    let output = tokio::process::Command::new("mount")
                        .args(["-o", "remount,rw", "/"])
                        .output()
                        .await;
                    
                    if let Ok(result) = output {
                        if result.status.success() {
                            tracing::info!("Attempted remount of root filesystem as read-write");
                        }
                    }
                }
                
                // Wait before retry unless it's the last attempt
                if attempt < MAX_RETRIES {
                    tokio::time::sleep(tokio::time::Duration::from_millis(RETRY_DELAY_MS)).await;
                } else {
                    tracing::error!(
                        attempts = MAX_RETRIES,
                        "/ etc remained read-only after all retries"
                    );
                    return Ok(false);
                }
            }
            Err(e) => {
                return Err(anyhow::anyhow!("Unexpected error testing /etc writability: {}", e));
            }
        }
    }
    
    Ok(false)
}

/// Write text directly to TTY1 console
/// Falls back to stdout if TTY not available
pub fn tty_write(text: &str) -> Result<()> {
    // Try to open /dev/tty1 for writing
    match OpenOptions::new()
        
        .append(true)
        .open("/dev/tty1")
    {
        Ok(mut tty) => {
            writeln!(tty, "{}", text)
                .context("Failed to write to /dev/tty1")?;
            tty.flush()
                .context("Failed to flush TTY")?;
        }
        Err(_) => {
            // Fallback to stdout (for testing or non-Linux systems)
            println!("{}", text);
        }
    }
    Ok(())
}

/// Display a header with box frame
/// Example:
/// ╔══════════════════════════════════════╗
/// ║       Zen Garden - First Boot        ║
/// ╚══════════════════════════════════════╝
pub fn display_header(title: &str) -> Result<()> {
    let width = 40;
    let padding = (width - title.len() - 2) / 2;
    let extra = if (width - title.len() - 2) % 2 == 1 { 1 } else { 0 };
    
    let top = format!("╔{}╗", "═".repeat(width - 2));
    let middle = format!("║{}{}{}║", 
        " ".repeat(padding),
        title,
        " ".repeat(padding + extra)
    );
    let bottom = format!("╚{}╝", "═".repeat(width - 2));
    
    tty_write("")?;
    tty_write(&top)?;
    tty_write(&middle)?;
    tty_write(&bottom)?;
    tty_write("")?;
    Ok(())
}

/// Display an item with simple indentation
/// Example: "  Stone Name: stone-meadow-42"
pub fn display_item(label: &str, value: &str) -> Result<()> {
    tty_write(&format!("  {}: {}", label, value))
}

/// Display a success message with [OK] indicator
/// Example: "  [OK] Docker daemon connected"
pub fn display_success(message: &str) -> Result<()> {
    tty_write(&format!("  [OK] {}", message))
}

/// Display an error message with [FAIL] indicator
/// Example: "  [FAIL] Failed to generate name"
pub fn display_error(message: &str) -> Result<()> {
    tty_write(&format!("  [FAIL] {}", message))
}

/// Display a waiting/progress message with [WAIT] indicator
/// Example: "  [WAIT] Checking name availability..."
pub fn display_wait(message: &str) -> Result<()> {
    tty_write(&format!("  [WAIT] {}", message))
}

/// Check if this is a first run (stone name matches "stone-new-*")
/// Check if this is the first run by looking for the initialization flag file
pub fn is_first_run() -> bool {
    !std::path::Path::new(garden_common::names::FIRST_RUN_FLAG).exists()
}

/// Mark first-run initialization as complete
pub async fn mark_first_run_complete() -> Result<()> {
    tokio::fs::write(garden_common::names::FIRST_RUN_FLAG, "")
        .await
        .context("Failed to create first-run completion flag")?;
    Ok(())
}

/// Generate a unique stone name with collision detection
/// 
/// Uses adjective-noun pattern with mDNS collision checking (10 attempts).
/// Falls back to hex suffix if all attempts collide.
pub async fn generate_unique_name() -> Result<String> {
    const ADJECTIVES: &[&str] = &[
        "azure", "bronze", "coral", "crimson", "emerald", "golden", "indigo",
        "jade", "lunar", "marble", "obsidian", "pearl", "quartz", "ruby",
        "silver", "topaz", "turquoise", "violet", "amber", "crystal"
    ];
    
    const NOUNS: &[&str] = &[
        "meadow", "summit", "river", "forest", "canyon", "valley", "harbor",
        "glacier", "prairie", "desert", "delta", "ridge", "plateau", "grove",
        "basin", "stream", "cliff", "shore", "peak", "dune"
    ];
    
    use rand::seq::SliceRandom;
    use rand::SeedableRng;
    // Use StdRng which is Send-safe for background tasks
    let mut rng = rand::rngs::StdRng::from_entropy();
    
    // Try 10 random combinations
    for attempt in 1..=10 {
        let adjective = ADJECTIVES.choose(&mut rng).unwrap();
        let noun = NOUNS.choose(&mut rng).unwrap();
        let candidate = format!("stone-{}-{}", adjective, noun);
        
        display_wait(&format!("Checking availability: {} (attempt {}/10)", candidate, attempt))?;
        
        // Check mDNS collision
        if !check_mdns_collision(&candidate).await {
            display_success(&format!("Name available: {}", candidate))?;
            return Ok(candidate);
        }
        
        display_wait(&format!("Name collision detected: {}", candidate))?;
    }
    
    // All attempts failed, use hex suffix
    let hex_suffix = format!("{:04x}", rand::random::<u16>());
    let fallback = format!("stone-{}", hex_suffix);
    display_wait(&format!("Using fallback name: {}", fallback))?;
    Ok(fallback)
}

/// Check if a stone name already exists on the network via mDNS
/// Returns true if collision detected, false if available
async fn check_mdns_collision(name: &str) -> bool {
    // Query mDNS for _moss._tcp.local with instance name matching stone name
    // Timeout after 2 seconds
    let mdns_name = format!("{}._moss._tcp.local", name);
    
    // Use avahi-browse to check for existing service
    match tokio::process::Command::new("avahi-browse")
        .args(["-t", "-r", "-p", "_moss._tcp"])
        .output()
        .await
    {
        Ok(output) => {
            let stdout = String::from_utf8_lossy(&output.stdout);
            // Check if our stone name appears in the output
            stdout.contains(&mdns_name) || stdout.contains(name)
        }
        Err(_) => {
            // avahi-browse not available or failed, assume no collision
            false
        }
    }
}

/// Set system hostname by writing directly to /etc/hostname
pub async fn set_hostname(name: &str) -> Result<()> {
    display_wait(&format!("Setting hostname to {}", name))?;
    
    // Write directly to /etc/hostname (more reliable than hostnamectl with NoNewPrivileges)
    tokio::fs::write("/etc/hostname", format!("{}\n", name))
        .await
        .context("Failed to write /etc/hostname")?;
    
    // Also set the running hostname using sethostname syscall
    // This requires the CAP_SYS_ADMIN capability but works with NoNewPrivileges
    let output = tokio::process::Command::new("hostname")
        .arg(name)
        .output()
        .await
        .context("Failed to execute hostname command")?;
    
    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        display_error(&format!("Warning: hostname command failed: {}", stderr))?;
        // Don't fail completely - the file write succeeded
    }
    
    display_success(&format!("Hostname set to {}", name))?;
    Ok(())
}

/// Read the system hostname from /etc/hostname.
///
/// This is the source of truth for what will be announced over mDNS (`<hostname>.local`).
pub async fn get_hostname() -> Result<String> {
    let content = tokio::fs::read_to_string("/etc/hostname")
        .await
        .context("Failed to read /etc/hostname")?;
    let hostname = content.trim().to_string();
    if hostname.is_empty() {
        anyhow::bail!("/etc/hostname was empty");
    }
    Ok(hostname)
}

/// Update /etc/hosts to reflect a hostname change.
pub async fn update_hosts_file(old_name: &str, new_name: &str) -> Result<()> {
    display_wait("Updating /etc/hosts")?;
    
    // Read current hosts file
    let hosts_content = tokio::fs::read_to_string("/etc/hosts")
        .await
        .context("Failed to read /etc/hosts")?;
    
    // Replace explicit old hostname entries, plus legacy stone-new-* entries.
    let updated_content = hosts_content
        .lines()
        .map(|line| {
            if line.contains(old_name) {
                line.replace(old_name, new_name)
            } else if line.contains("stone-new-") {
                // Back-compat for older installers that used stone-new-<guid>
                line.replace("stone-new-", new_name.strip_prefix("stone-").unwrap_or(new_name))
            } else {
                line.to_string()
            }
        })
        .collect::<Vec<_>>()
        .join("\n");
    
    // Write back
    tokio::fs::write("/etc/hosts", updated_content)
        .await
        .context("Failed to write /etc/hosts")?;
    
    display_success("Updated /etc/hosts")?;
    Ok(())
}

/// Restart avahi-daemon to update mDNS announcements
pub async fn restart_avahi() -> Result<()> {
    display_wait("Restarting avahi-daemon")?;
    
    let output = tokio::process::Command::new("systemctl")
        .args(["restart", "avahi-daemon"])
        .output()
        .await
        .context("Failed to restart avahi-daemon")?;
    
    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        // Don't fail - avahi restart is optional
        display_error(&format!("Warning: avahi restart failed: {}", stderr))?;
        tty_write("  (mDNS will update on next system reboot)")?;
    } else {
        display_success("Avahi daemon restarted")?;
    }
    Ok(())
}

/// Test mDNS resolution by pinging the stone's hostname
pub async fn test_mdns_resolution(stone_name: &str) -> Result<()> {
    display_wait(&format!("Testing mDNS resolution for {}.local", stone_name))?;
    
    // Wait a moment for avahi to propagate the announcement
    tokio::time::sleep(tokio::time::Duration::from_secs(2)).await;
    
    // Try to ping the .local hostname (single ping, 2 second timeout)
    let hostname = format!("{}.local", stone_name);
    let output = tokio::process::Command::new("ping")
        .args(["-c", "1", "-W", "2", &hostname])
        .output()
        .await
        .context("Failed to execute ping command")?;
    
    if output.status.success() {
        display_success(&format!("mDNS resolution confirmed: {}.local is reachable", stone_name))?;
    } else {
        display_error(&format!("Warning: {}.local not yet reachable via mDNS", stone_name))?;
        tty_write("  (May take a few moments for network propagation)")?;
    }
    
    Ok(())
}

/// Write MOTD (Message of the Day) file
pub fn write_motd(stone_name: &str, url: &str) -> Result<()> {
    display_wait("Creating message of the day")?;
    
    let motd_content = format!(
r#"
╔══════════════════════════════════════╗
║       Zen Garden Stone Ready         ║
╚══════════════════════════════════════╝

  Stone Name: {}
  Management URL: {}
  Username: stone
  Password: garden

  Run 'systemctl status garden-moss' to check service status
  Visit {} to manage services

"#,
        stone_name,
        url,
        url
    );
    
    std::fs::write("/etc/motd", motd_content)
        .context("Failed to write /etc/motd")?;
    
    display_success("Message of the day created")?;
    Ok(())
}

/// Update Moss configuration file with new stone name
pub async fn update_moss_config(new_name: &str) -> Result<()> {
    display_wait("Updating Moss configuration")?;
    
    let config_path = format!("{}/{}", garden_common::names::CONFIG_DIR, garden_common::names::MOSS_CONFIG);
    
    // Read current config
    let config_content = tokio::fs::read_to_string(&config_path)
        .await
        .context(format!("Failed to read {}", garden_common::names::MOSS_CONFIG))?;
    
    let mut found = false;
    let mut updated_lines: Vec<String> = Vec::new();
    for line in config_content.lines() {
        let trimmed = line.trim();

        // Preferred modern key
        if trimmed.starts_with("stone_name") {
            let indent = line.len() - line.trim_start().len();
            updated_lines.push(format!("{}stone_name = \"{}\"", " ".repeat(indent), new_name));
            found = true;
            continue;
        }

        // Legacy key used in older templates
        if trimmed.starts_with("name =") || trimmed.starts_with("name=") {
            let indent = line.len() - line.trim_start().len();
            updated_lines.push(format!("{}name = \"{}\"", " ".repeat(indent), new_name));
            found = true;
            continue;
        }

        updated_lines.push(line.to_string());
    }

    // If neither key existed, insert a modern stone_name near the top (after any header comments).
    if !found {
        let mut inserted = false;
        let mut with_insert: Vec<String> = Vec::new();
        for line in &updated_lines {
            if !inserted {
                let t = line.trim();
                if t.is_empty() || t.starts_with('#') {
                    with_insert.push(line.clone());
                    continue;
                }
                with_insert.push(format!("stone_name = \"{}\"", new_name));
                inserted = true;
            }
            with_insert.push(line.clone());
        }
        if !inserted {
            with_insert.push(format!("stone_name = \"{}\"", new_name));
        }
        updated_lines = with_insert;
    }

    let updated_content = updated_lines.join("\n");
    
    // Write back
    tokio::fs::write(&config_path, updated_content)
        .await
        .context(format!("Failed to write {}", garden_common::names::MOSS_CONFIG))?;
    
    display_success("Configuration updated")?;
    Ok(())
}

/// Get local IP address synchronously (for use in non-async contexts)
pub fn get_local_ip_sync() -> String {
    use std::net::IpAddr;
    
    if let Ok(addrs) = local_ip_address::list_afinet_netifas() {
        for (_, ip) in addrs {
            if let IpAddr::V4(ipv4) = ip {
                // Skip loopback and link-local addresses
                if !ipv4.is_loopback() && !ipv4.is_link_local() {
                    return ipv4.to_string();
                }
            }
        }
    }
    
    // Fallback to hostname-based lookup
    "127.0.0.1".to_string()
}

