// Binary-only modules (not needed by library)
mod dispatch;
mod parser;

// Use shared modules from the library
use garden_rake::command_manifest;
use garden_rake::commands;
use garden_rake::commands::Command;
use garden_rake::ui;

#[cfg(test)]
mod discovery_tests;

#[cfg(test)]
mod recommendation_tests;

use base64::Engine;
use clap::{Parser, Subcommand, ValueEnum};
use std::time::Duration;
use tracing_subscriber::EnvFilter;

// Use the single global cache from library's stone_cache module
// This ensures observe.rs and dispatch.rs share the same cache instance
use garden_rake::stone_cache::GLOBAL_CACHE;

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct TemplateInfo {
    pub name: String,
    pub category: String,
    pub description: String,
    #[serde(default)]
    pub tags: Vec<String>,
}

// Note: resolve_endpoint logic is in dispatch::resolve_endpoint (DRY)

#[derive(Debug, Clone, ValueEnum)]
enum PlacementMode {
    /// Show interactive menu with top recommendations
    Interactive,
    /// Automatically select the best stone without prompting
    Auto,
}

#[derive(Parser)]
#[command(name = "garden-rake")]
#[command(about = "Zen Garden management CLI - run without arguments to see command directory")]
#[command(version = concat!(env!("CARGO_PKG_VERSION"), ".", env!("BUILD_NUMBER")))]
struct Cli {
    /// Suppress suggestions (zen: quietly, env: GARDEN_QUIET)
    #[arg(short, long, global = true)]
    quiet: bool,

    /// Clear cached tending and force fresh discovery (zen: fresh)
    #[arg(long, global = true)]
    fresh: bool,

    /// Increase verbosity (-v info, -vv debug, -vvv trace)
    #[arg(short, long, global = true, action = clap::ArgAction::Count)]
    verbose: u8,

    #[command(subcommand)]
    command: Option<Commands>,
}

#[derive(Subcommand)]
enum Commands {
    /// Get Stone status (alias for stone details)
    #[command(hide = false)]
    Status {
        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Offer a service
    /// Offerings: list validated offerings, install, or inspect.
    ///
    /// Examples:
    ///   garden-rake offer                # List validated offerings by category
    ///   garden-rake offer mongodb        # Install mongodb (with compatibility fallback if needed)
    ///   garden-rake offer mongodb info   # Show offering details + compatibility decision
    ///   garden-rake offer mongo somewhere # Get intelligent placement recommendation
    Offer {
        /// Offering name (omit to list all offerings)
        offering: Option<String>,

        /// Optional action for a specific offering
        #[command(subcommand)]
        action: Option<OfferAction>,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,

        /// Bias recommendations (non-blocking). Examples: --prefer ssd, --prefer nvme
        #[arg(long, value_delimiter = ',', action = clap::ArgAction::Append)]
        prefer: Vec<String>,

        /// If an install fails due to compatibility, automatically recommend across all discovered stones.
        #[arg(long)]
        anywhere_on_fail: bool,
        
        /// Use intelligent placement recommendation (zen: somewhere, somewhere quietly)
        #[arg(long, value_enum)]
        placement_mode: Option<PlacementMode>,
    },

    /// List services
    List {
        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Remove a service (soft delete - container preserved as stray)
    Remove {
        /// Service name to remove
        service: String,

        /// Skip confirmation prompt
        #[arg(long)]
        force: bool,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long, visible_alias = "on")]
        at: Option<String>,
    },

    /// Uproot a service (hard delete - destroy container completely)
    #[command(
        long_about = "Permanently destroy a service and its container.\n\n\
        Unlike 'remove' which preserves the container as a stray, 'uproot' completely\n\
        destroys the container and cannot be recovered.\n\n\
        Examples:\n  \
        garden-rake uproot mongodb              # Destroy mongodb container\n  \
        garden-rake uproot mongodb --force      # Skip confirmation"
    )]
    Uproot {
        /// Service name to destroy
        service: String,

        /// Skip confirmation prompt
        #[arg(long)]
        force: bool,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long, visible_alias = "on")]
        at: Option<String>,
    },

    /// Adopt an existing container into Zen Garden management
    #[command(
        long_about = "Adopt an existing container into Zen Garden management.\n\n\
        Adopted containers are ones that already exist on the stone but weren't\n\
        created by Zen Garden (e.g., created manually or by other tooling).\n\n\
        Examples:\n  \
        garden-rake adopt my-mongodb-container  # Adopt a specific container\n  \
        garden-rake find strays                 # List adoptable containers first"
    )]
    Adopt {
        /// Container name to adopt
        container: String,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long, visible_alias = "on")]
        at: Option<String>,
    },

    /// Release an adopted service (stop managing, keep container running)
    #[command(
        long_about = "Release an adopted service back to the wild.\n\n\
        This removes the service from Zen Garden's management but leaves the\n\
        container running. Use this when you want to stop managing a service\n\
        without destroying it.\n\n\
        Examples:\n  \
        garden-rake release mongodb             # Release adopted mongodb"
    )]
    Release {
        /// Service name to release
        service: String,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long, visible_alias = "on")]
        at: Option<String>,
    },

    /// Locate strays (adoptable containers)
    #[command(
        long_about = "Locate adoptable containers (strays) on a stone.\n\n\
        Examples:\n  \
        garden-rake locate strays               # List containers not managed by Zen Garden"
    )]
    Locate {
        #[command(subcommand)]
        target: LocateTarget,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long, visible_alias = "on")]
        at: Option<String>,
    },

    /// Find running services across the garden
    #[command(
        long_about = "Find running services and return connection URIs.\n\n\
        Supports search by name, category, or tags:\n  \
        - By name: garden-rake find mongodb\n  \
        - By category: garden-rake find c:database\n  \
        - By tag: garden-rake find t:nosql\n\n\
        Output formats:\n  \
        - human (default): Readable output with hints\n  \
        - json: Machine-readable JSON\n  \
        - uri: Just the connection URI (hostname-based)\n  \
        - uri-ip: Just the connection URI (IP fallback)\n\n\
        Examples:\n  \
        garden-rake find mongodb                 # Find mongodb service\n  \
        garden-rake find c:database              # Find any database\n  \
        garden-rake find mongodb --format uri    # Just the connection string\n  \
        garden-rake find mongodb wishfully       # Auto-provision if not found"
    )]
    Find {
        /// Search query (name, c:category, or t:tag)
        query: String,

        /// Output format (human, json, uri, uri-ip)
        #[arg(long, default_value = "human")]
        format: String,

        /// Auto-provision if not found (zen: wishfully)
        #[arg(long)]
        wishful: bool,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long, visible_alias = "on")]
        at: Option<String>,
    },

    /// List adopted services
    #[command(
        long_about = "List services that were adopted from existing containers.\n\n\
        Example:\n  \
        garden-rake adopted                     # List all adopted services"
    )]
    Adopted {
        /// Moss endpoint (omit to auto-discover)
        #[arg(long, visible_alias = "on")]
        at: Option<String>,
    },

    /// List borrowed services
    #[command(
        long_about = "List external services that have been borrowed (registered but not managed).\n\n\
        Example:\n  \
        garden-rake borrowed                    # List all borrowed services"
    )]
    Borrowed {
        /// Moss endpoint (omit to auto-discover)
        #[arg(long, visible_alias = "on")]
        at: Option<String>,
    },

    /// Borrow an external service (register for reference/discovery)
    #[command(
        long_about = "Register an external network service for reference and discovery.\n\n\
        Borrowed services are external services (not on this stone) that you want\n\
        to include in service discovery and configuration.\n\n\
        Examples:\n  \
        garden-rake borrow redis from redis://company-cache:6379\n  \
        garden-rake borrow postgres --from postgresql://db-server:5432"
    )]
    Borrow {
        /// Name for this borrowed service
        name: String,

        /// URL/connection string for the external service
        #[arg(long)]
        from: Option<String>,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long, visible_alias = "on")]
        at: Option<String>,
    },

    /// Return (unregister) a borrowed service
    #[command(
        name = "return",
        long_about = "Unregister a borrowed external service.\n\n\
        This removes the service from the registry but doesn't affect the\n\
        external service itself.\n\n\
        Example:\n  \
        garden-rake return redis                # Unregister borrowed redis"
    )]
    Return {
        /// Name of the borrowed service to unregister
        name: String,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long, visible_alias = "on")]
        at: Option<String>,
    },

    /// Upgrade a service
    Upgrade {
        /// Service name to upgrade (omit for all services)
        service: Option<String>,

        /// Upgrade all services on stone
        #[arg(long)]
        all: bool,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Stop a service (rest mode)
    Rest {
        /// Service name to stop
        service: String,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Start a service (wake from rest)
    Wake {
        /// Service name to start
        service: String,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Phase 3 scaffolding: Place keystone or stone (zen syntax)
    #[command(
        long_about = "Initialize pond or join pond (zen syntax for 'pond init' or 'pond join').\n\n\
        Examples:\n  \
        garden-rake place keystone              # Initialize pond\n  \
        garden-rake place stone --code ABC123 # Join pond with invitation\n\n\
        Note: Pond security implementation pending (Phase 3b)."
    )]
    Place {
        /// Target: "keystone" or "stone"
        target: String,

        /// Invitation code (required for "stone")
        #[arg(long)]
        code: Option<String>,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
        
        /// Passphrase for encrypting pond certificate (keystone only)
        #[arg(long)]
        passphrase: Option<String>,
    },

    /// Phase 3 scaffolding: Invite a Stone (zen syntax)
    #[command(
        long_about = "Generate pond invitation code (zen syntax for 'pond invite').\n\n\
        Example:\n  \
        garden-rake invite\n\n\
        Note: Pond security implementation pending (Phase 3b)."
    )]
    Invite {
        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Observe garden state (all stones or filtered)
    #[command(
        long_about = "Observe garden state with optional filtering.\n\n\
        Examples:\n  \
        garden-rake observe                    # All stones\n  \
        garden-rake observe stone-01           # Specific stone\n  \
        garden-rake observe --offering mongodb,redis  # Filter by offerings"
    )]
    Observe {
        /// Specific stone name (omit for all stones)
        stone: Option<String>,

        /// Filter by offerings (comma-separated, e.g., "mongodb,redis")
        #[arg(long)]
        offering: Option<String>,
    },

    /// Watch real-time events from a Stone
    #[command(
        long_about = "Stream real-time events from moss operations.\n\n\
        Examples:\n  \
        garden-rake watch stone-01                        # Watch all events\n  \
        garden-rake watch stone-01 until 'completed'     # Exit when string appears\n  \
        garden-rake watch --at http://stone-01:7185      # Explicit endpoint
        garden-rake watch offering mongodb logs          # Watch offering logs"
    )]
    Watch {
        /// Subcommand: offering, stone, or none for events
        #[command(subcommand)]
        target: Option<WatchTarget>,

        /// Exit when this string appears in event stream
        #[arg(long)]
        until: Option<String>,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Refresh (update) garden-moss or garden-rake binary on stone
    #[command(
        long_about = "Update garden-moss or garden-rake binary on a stone (development use).\n\n\
        Examples:\n  \
        garden-rake refresh garden-moss --from ./target/release/garden-moss\n  \
        garden-rake refresh rake --from ./dist/linux-x64/garden-rake\n\n\
        The binary will be validated for architecture compatibility before installation.\n\
        Garden-Moss will automatically restart after update."
    )]
    Refresh {
        /// Component to refresh: "moss" or "rake"
        component: String,

        /// Path to binary file
        #[arg(long)]
        from: std::path::PathBuf,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Reconcile moss registry with existing containers (adopt now)
    #[command(
        long_about = "Force moss to reconcile its registry with existing zen-offering containers.\n\n\
        This is useful after a moss restart/update, or if containers were created externally.\n\n\
        Examples:\n  \
        garden-rake reconcile                         # Adopt any missing containers\n  \
        garden-rake reconcile --drop-invalid          # Also remove invalid zen-offering-* containers\n  \
        garden-rake reconcile --at http://stone-01:7185"
    )]
    Reconcile {
        /// Remove zen-offering-* containers that don't map to a known template
        #[arg(long)]
        drop_invalid: bool,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Manage offering templates
    Template {
        #[command(subcommand)]
        command: TemplateCommands,
    },

    /// Run guided workflows (scaffolded - not yet implemented)
    #[command(
        long_about = "Run guided workflows for common operations.\n\n\
        This command is scaffolded but not yet implemented.\n\n\
        Future ceremonies may include:\n  \
        - garden-rake ceremony bootstrap      # First-time setup wizard\n  \
        - garden-rake ceremony migrate        # Service migration workflow\n  \
        - garden-rake ceremony backup         # Guided backup configuration"
    )]
    Ceremony {
        /// Ceremony name to run
        name: Option<String>,
    },

    /// Manage tending state (which stone rake commands target)
    #[command(
        long_about = "Manage which stone garden-rake commands target.\n\n\
        Examples:\n  \
        garden-rake tend                      # Show current tending state\n  \
        garden-rake tend this                 # Tend to localhost\n  \
        garden-rake tend auto                 # Auto-discover and set\n  \
        garden-rake tend http://192.168.1.108:7185  # Set explicit endpoint\n  \
        garden-rake tend --clear              # Stop tending\n\n\
        Tending state is cached for 90 seconds and automatically refreshed."
    )]
    Tend {
        /// Target: 'this', 'local', 'auto', or explicit endpoint URL
        target: Option<String>,

        /// Clear tending state
        #[arg(long)]
        clear: bool,
    },

    /// Manage pond security (normative syntax)
    #[command(
        long_about = "Manage pond security for multi-stone trust.\n\n\
        Examples:\n  \
        garden-rake pond init                 # Initialize pond (place keystone)\n  \
        garden-rake pond status               # Show pond status\n  \
        garden-rake pond invite               # Generate invitation code\n  \
        garden-rake pond join <code>          # Join pond with code\n  \
        garden-rake pond remove               # Remove pond from stone\n  \
        garden-rake pond untrust stone-02     # Remove stone from pond\n\n\
        Note: Pond security implementation pending (Phase 3b)."
    )]
    Pond {
        #[command(subcommand)]
        action: PondAction,
        
        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Remove a stone from the pond (zen syntax)
    #[command(
        long_about = "Remove a stone from the pond (zen syntax for 'pond untrust').\n\n\
        Example:\n  \
        garden-rake lift stone stone-02\n\n\
        Note: Pond security implementation pending (Phase 3b)."
    )]
    Lift {
        /// Target type: 'keystone' or 'stone'
        target_type: String,
        
        /// Stone name (required if target_type is 'stone')
        stone_name: Option<String>,
        
        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Control stone console output (zen syntax)
    #[command(
        long_about = "Control stone console output verbosity.\n\n\
        Examples:\n  \
        garden-rake make stone sing              # Verbose output temporarily (30min timeout)\n  \
        garden-rake make stone sing forever      # Verbose output permanently\n  \
        garden-rake make stone quiet             # Reset to default (informative)\n  \
        garden-rake make stone silent            # No console output\n\n\
        Modes:\n  \
        silent       - No console output (systemd/service use)\n  \
        minimal      - Critical events only\n  \
        informative  - Major lifecycle events (default)\n  \
        verbose      - Full debug output (sing mode)"
    )]
    Make {
        /// Target: 'stone'
        target: String,

        #[command(subcommand)]
        action: MakeAction,

        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Install moss as a system service (zen syntax)
    #[command(
        long_about = "Install moss as a Windows system service (zen: take-root).\n\n\
        Examples:\n  \
        garden-rake take-root                     # Install service on tended stone\n  \
        garden-rake take-root at windows-01       # Install on specific stone\n\n\
        The stone will install itself as a system service and start automatically.\n\
        Requires administrator privileges on the target Windows machine.\n\n\
        To uninstall: sc delete ZenGardenMoss"
    )]
    TakeRoot {
        /// Target stone (positional zen syntax: "at stone-name")
        at: Option<String>,
        
        /// Explicit stone name (follows "at" in zen syntax)
        stone: Option<String>,
    },

    /// Install moss as a system service (normative syntax)
    #[command(
        name = "install-service",
        long_about = "Install moss as a Windows system service (normative: install-service).\n\n\
        Examples:\n  \
        garden-rake install-service               # Install service on tended stone\n  \
        garden-rake install-service --at windows-01  # Install on specific stone\n\n\
        The stone will install itself as a system service and start automatically.\n\
        Requires administrator privileges on the target Windows machine.\n\n\
        To uninstall: sc delete ZenGardenMoss"
    )]
    InstallService {
        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },

    /// Wake a stone via Wake-on-LAN
    #[command(
        long_about = "Send a Wake-on-LAN magic packet to wake a sleeping stone.\n\n\
        Examples:\n  \
        garden-rake rouse oak                     # Wake stone named 'oak'\n  \
        garden-rake rouse oak at cedar            # Send WoL from 'cedar' to wake 'oak'\n\n\
        The stone must have been previously discovered (MAC address cached).\n\
        Wake-on-LAN requires BIOS/UEFI and NIC support on the target machine."
    )]
    Rouse {
        /// Stone name to wake
        stone: String,

        /// Moss endpoint to send WoL from (omit to use tended stone)
        #[arg(long)]
        at: Option<String>,
    },

    /// Shut down a stone (power off)
    #[command(
        long_about = "Power off the target stone machine.\n\n\
        Examples:\n  \
        garden-rake slumber                       # Shut down tended stone\n  \
        garden-rake slumber oak                   # Shut down stone 'oak' (by name)\n  \
        garden-rake slumber --at oak              # Same as above\n  \
        garden-rake slumber --at http://10.0.0.5:7185  # By URL\n\n\
        Accepts: stone name, stone ID, or full URL.\n\
        The stone's MAC address is preserved in topology cache for Wake-on-LAN.\n\
        Use 'garden-rake rouse <stone>' to wake it later."
    )]
    Slumber {
        /// Stone name, stone ID, or endpoint URL (omit to use tended stone)
        stone: Option<String>,

        /// Stone name, stone ID, or endpoint URL (alternative to positional)
        #[arg(long)]
        at: Option<String>,
    },

    /// Reboot a stone
    #[command(
        long_about = "Restart the target stone machine.\n\n\
        Examples:\n  \
        garden-rake stir                          # Reboot tended stone\n  \
        garden-rake stir oak                      # Reboot stone 'oak' (by name)\n  \
        garden-rake stir --at oak                 # Same as above\n  \
        garden-rake stir --at http://10.0.0.5:7185  # By URL\n\n\
        Accepts: stone name, stone ID, or full URL."
    )]
    Stir {
        /// Stone name, stone ID, or endpoint URL (omit to use tended stone)
        stone: Option<String>,

        /// Stone name, stone ID, or endpoint URL (alternative to positional)
        #[arg(long)]
        at: Option<String>,
    },

    /// Browse command directory (interactive command reference)
    #[command(
        long_about = "Browse the command directory with descriptions and examples.\n\n\
        Examples:\n  \
        garden-rake commands                    # Show all commands by category\n  \
        garden-rake commands take-root          # Show detailed command info\n  \
        garden-rake commands --category system  # Filter by category\n  \
        garden-rake commands --zen              # Show only zen syntax\n  \
        garden-rake commands --normative        # Show only normative syntax"
    )]
    BrowseCommands {
        /// Specific command name to show details for
        name: Option<String>,

        /// Filter by command category
        #[arg(long)]
        category: Option<String>,

        /// Show only zen syntax
        #[arg(long)]
        zen: bool,

        /// Show only normative syntax
        #[arg(long)]
        normative: bool,
    },
}

#[derive(Debug, Subcommand)]
enum MakeAction {
    /// Set stone to verbose mode (sing)
    Sing {
        /// Make verbose mode permanent (no timeout)
        #[arg(long = "forever", short = 'f')]
        forever: bool,
    },
    /// Set stone to default/informative mode (quiet)
    Quiet,
    /// Set stone to silent mode (no output)
    Silent,
    /// Set stone to minimal mode (critical only)
    Minimal,
}

#[derive(Debug, Subcommand)]
enum OfferAction {
    /// Show offering details and compatibility decision
    Info,
}

#[derive(Debug, Subcommand)]
enum PondAction {
    /// Initialize pond security (place keystone)
    Init {
        /// Passphrase for encrypting pond certificate
        #[arg(long)]
        passphrase: Option<String>,
    },
    /// Show pond status
    Status,
    /// Generate invitation code
    Invite,
    /// Join pond with invitation code
    Join {
        /// TOTP invitation code
        code: String,
    },
    /// Remove pond from this stone
    Remove,
    /// Remove a stone from the pond
    Untrust {
        /// Stone name to remove
        stone_name: String,
    },
}

#[derive(Debug, Subcommand)]
enum LocateTarget {
    /// Locate adoptable containers (strays - running containers not managed by Zen Garden)
    Strays,
}

#[derive(Debug, Subcommand)]
enum TemplateCommands {
    /// List available offering templates
    List {
        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },
    
    /// Show template YAML content
    Show {
        /// Template name
        name: String,
        
        /// Moss endpoint (omit to auto-discover)
        #[arg(long)]
        at: Option<String>,
    },
}

#[derive(Debug, Subcommand)]
enum WatchTarget {
    /// Watch offering logs
    Offering {
        /// Offering name
        name: String,
        
        /// Subcommand (logs)
        #[command(subcommand)]
        mode: WatchOfferingMode,
    },
    /// Watch stone logs
    Stone {
        /// Stone name
        name: String,
        
        /// Subcommand (logs)
        #[command(subcommand)]
        mode: WatchStoneMode,
    },
}

#[derive(Debug, Subcommand)]
enum WatchOfferingMode {
    /// Stream logs in real-time
    Logs {
        /// Show timestamps
        #[arg(long)]
        timestamps: bool,
    },
}

#[derive(Debug, Subcommand)]
enum WatchStoneMode {
    /// Stream logs from all offerings
    Logs {
        /// Show timestamps
        #[arg(long)]
        timestamps: bool,
    },
}

/// Count verbosity flags from raw args (before clap parsing)
/// Supports: -v, -vv, -vvv, -vvvv, --verbose (counted per occurrence)
fn count_verbosity(args: &[String]) -> u8 {
    let mut count = 0u8;
    for arg in args {
        if arg == "--verbose" {
            count = count.saturating_add(1);
        } else if arg.starts_with("-") && !arg.starts_with("--") {
            // Count 'v' characters in short flags (e.g., -v, -vv, -vvv)
            // But skip if it's a value like -v123
            let flag_chars: String = arg.chars().skip(1).take_while(|c| c.is_alphabetic()).collect();
            count = count.saturating_add(flag_chars.matches('v').count() as u8);
        }
    }
    count
}

/// Convert zen syntax to normative args for Clap
fn normalize_zen_to_clap(parsed: &parser::ParsedCommand) -> anyhow::Result<Vec<String>> {
    let mut args = Vec::new();

    // Map zen verbs to Commands
    // Commands that have same zen/normative name pass through directly
    match parsed.verb.as_str() {
        // === SERVICE LIFECYCLE ===
        "offer" => {
            args.push("offer".to_string());
            args.extend(parsed.args.clone());
        }
        "rest" => {
            args.push("rest".to_string());
            args.extend(parsed.args.clone());
        }
        "wake" => {
            args.push("wake".to_string());
            args.extend(parsed.args.clone());
        }
        "nourish" => {
            // nourish (zen) → upgrade (clap command)
            args.push("upgrade".to_string());
            args.extend(parsed.args.clone());
        }
        "remove" => {
            // remove (zen) = soft delete, container becomes stray
            args.push("remove".to_string());
            args.extend(parsed.args.clone());
        }
        "uproot" => {
            // uproot (zen) = hard delete, destroy container
            args.push("uproot".to_string());
            args.extend(parsed.args.clone());
        }

        // === ADOPTION ===
        "adopt" => {
            args.push("adopt".to_string());
            args.extend(parsed.args.clone());
        }
        "release" => {
            // release (zen) = release adopted service from management
            // NOT the same as remove (which is soft delete)
            args.push("release".to_string());
            args.extend(parsed.args.clone());
        }
        "find" => {
            // Find is now for service discovery, not strays
            args.push("find".to_string());
            args.extend(parsed.args.clone());
            // Handle wishfully flag
            if parsed.keywords.wishfully {
                args.push("--wishful".to_string());
            }
        }
        "locate" => {
            // Locate is for strays (adoption domain)
            args.push("locate".to_string());
            args.extend(parsed.args.clone());
        }
        "adopted" => {
            args.push("adopted".to_string());
            args.extend(parsed.args.clone());
        }
        "borrowed" => {
            args.push("borrowed".to_string());
            args.extend(parsed.args.clone());
        }
        "borrow" => {
            args.push("borrow".to_string());
            args.extend(parsed.args.clone());
        }
        "return" => {
            args.push("return".to_string());
            args.extend(parsed.args.clone());
        }

        // === DISCOVERY ===
        "observe" => {
            args.push("observe".to_string());
            args.extend(parsed.args.clone());
        }
        "watch" => {
            args.push("watch".to_string());
            args.extend(parsed.args.clone());
        }
        "list" => {
            args.push("list".to_string());
            args.extend(parsed.args.clone());
        }
        "status" => {
            args.push("status".to_string());
            args.extend(parsed.args.clone());
        }
        "touch" => {
            // touch = inspect (deep inspection) - legacy alias for status
            args.push("status".to_string());
            args.extend(parsed.args.clone());
        }
        "explore" => {
            // explore = list offerings
            args.push("offer".to_string());
        }
        "garden" => {
            // garden = observe all
            args.push("observe".to_string());
        }

        // === MANAGEMENT ===
        "tend" => {
            args.push("tend".to_string());
            args.extend(parsed.args.clone());
        }
        "reconcile" => {
            args.push("reconcile".to_string());
            args.extend(parsed.args.clone());
        }
        "refresh" => {
            args.push("refresh".to_string());
            args.extend(parsed.args.clone());
        }

        // === POND ===
        "place" => {
            args.push("place".to_string());
            args.extend(parsed.args.clone());
        }
        "lift" => {
            // lift = remove stone from pond or remove keystone
            args.push("lift".to_string());
            args.extend(parsed.args.clone());
        }
        "invite" => {
            args.push("invite".to_string());
            args.extend(parsed.args.clone());
        }

        // === SYSTEM ===
        "make" => {
            args.push("make".to_string());
            args.extend(parsed.args.clone());
        }
        "take-root" => {
            args.push("take-root".to_string());
            args.extend(parsed.args.clone());
        }

        // === STONE ADMIN (power management) ===
        "rouse" => {
            args.push("rouse".to_string());
            args.extend(parsed.args.clone());
        }
        "slumber" => {
            args.push("slumber".to_string());
            args.extend(parsed.args.clone());
        }
        "stir" => {
            args.push("stir".to_string());
            args.extend(parsed.args.clone());
        }

        // === ADMIN ===
        "template" => {
            args.push("template".to_string());
            args.extend(parsed.args.clone());
        }
        "ceremony" => {
            args.push("ceremony".to_string());
            args.extend(parsed.args.clone());
        }
        _ => {
            return Err(anyhow::anyhow!("Unknown zen verb: {}", parsed.verb));
        }
    }

    // Add --on flag if on/at keyword was used (--at is also accepted for legacy support)
    if let Some(stone) = &parsed.keywords.on_stone {
        args.push("--at".to_string());
        args.push(stone.clone());
    }
    
    // Handle "somewhere" keyword for intelligent placement
    // This triggers placement recommendation instead of direct install
    if parsed.keywords.somewhere {
        // Determine mode based on "quietly" modifier
        let mode = if parsed.keywords.quietly { "auto" } else { "interactive" };
        args.push("--placement-mode".to_string());
        args.push(mode.to_string());
    }

    // Add --from flag if from keyword was used (for borrow command)
    if let Some(url) = &parsed.keywords.from_url {
        args.push("--from".to_string());
        args.push(url.clone());
    }

    // Note: quietly is handled via quiet_mode in main, not passed to Clap
    // Note: until is handled by the watch command itself

    Ok(args)
}

// Windows debug builds need larger stack for async/clap combination
#[cfg(all(windows, debug_assertions))]
fn main() -> anyhow::Result<()> {
    // Spawn with 4MB stack to avoid stack overflow in debug builds
    std::thread::Builder::new()
        .stack_size(4 * 1024 * 1024)
        .spawn(|| {
            tokio::runtime::Builder::new_multi_thread()
                .enable_all()
                .build()
                .unwrap()
                .block_on(async_main())
        })?
        .join()
        .map_err(|_| anyhow::anyhow!("Thread panic"))?
}

#[cfg(not(all(windows, debug_assertions)))]
#[tokio::main]
async fn main() -> anyhow::Result<()> {
    async_main().await
}

async fn async_main() -> anyhow::Result<()> {
    // Validate command manifest in debug builds
    #[cfg(debug_assertions)]
    command_manifest::validate_manifest();

    // Pre-parse for zen syntax (before Clap)
    let raw_args: Vec<String> = std::env::args().skip(1).collect();

    // Pre-parse verbosity level from raw args (before tracing init)
    // Supports: -v, -vv, -vvv, --verbose (multiple times)
    let verbosity = count_verbosity(&raw_args);

    // Initialize tracing with appropriate level
    // Priority: CLI flag > RUST_LOG env var > default (warn)
    let env_filter = if verbosity > 0 {
        let level = match verbosity {
            1 => "info",
            2 => "debug",
            _ => "trace",
        };
        // Set filter for garden crates only to avoid noise from dependencies
        EnvFilter::new(format!("garden_rake={},garden_common={},garden_moss={}", level, level, level))
    } else {
        // Default: use RUST_LOG env var or warn level
        EnvFilter::try_from_default_env()
            .unwrap_or_else(|_| EnvFilter::new("warn"))
    };

    tracing_subscriber::fmt()
        .with_env_filter(env_filter)
        .init();
    
    // Check for help query syntax: command? or ?command
    if !raw_args.is_empty() {
        let first_arg = &raw_args[0];
        
        // Handle: garden-rake ?command
        if first_arg.starts_with('?') {
            let cmd_name = first_arg.trim_start_matches('?');
            if !cmd_name.is_empty() {
                use command_manifest::MANIFEST;
                if let Some(cmd) = MANIFEST.get(cmd_name) {
                    display_command_detail(cmd, false, false);
                    return Ok(());
                } else {
                    eprintln!("Unknown command: {}", cmd_name);
                    std::process::exit(1);
                }
            }
        }
        
        // Handle: garden-rake command?
        if first_arg.ends_with('?') {
            let cmd_name = first_arg.trim_end_matches('?');
            if !cmd_name.is_empty() {
                use command_manifest::MANIFEST;
                if let Some(cmd) = MANIFEST.get(cmd_name) {
                    display_command_detail(cmd, false, false);
                    return Ok(());
                } else {
                    eprintln!("Unknown command: {}", cmd_name);
                    std::process::exit(1);
                }
            }
        }
    }
    
    let (cli, parsed_keywords) = if !raw_args.is_empty() {
        match parser::parse_args(raw_args.clone()) {
            Ok(parsed) if parsed.style == parser::CommandStyle::Zen => {
                // Convert zen to normative args for Clap
                let normalized = normalize_zen_to_clap(&parsed)?;
                let cli = Cli::parse_from(std::iter::once("garden-rake".to_string()).chain(normalized));
                (cli, Some(parsed.keywords))
            }
            Ok(_) => {
                // Normative style, use Clap normally
                (Cli::parse(), None)
            }
            Err(e) => {
                eprintln!("Error: {}", e);
                std::process::exit(1);
            }
        }
    } else {
        (Cli::parse(), None)
    };

    // Determine if quiet mode is active
    let quiet_mode = cli.quiet
        || parsed_keywords.as_ref().map(|k| k.quietly).unwrap_or(false)
        || std::env::var("GARDEN_QUIET").is_ok();

    // Determine if fresh mode is active (--fresh flag or zen "fresh" keyword)
    let fresh_mode = cli.fresh
        || parsed_keywords.as_ref().map(|k| k.fresh).unwrap_or(false);

    // Note: fresh_mode is passed to Moss API to request fresh topology scan.
    // It does NOT clear tending state - Rake stays connected to the same stone.

    // Create pooled HTTP client with connection reuse (hot cache architecture)
    // Configuration optimized for long-running commands (watch/observe):
    // - pool_idle_timeout: 90 seconds (matches stone cache TTL)
    // - pool_max_idle_per_host: 10 (handle multiple concurrent operations)
    // - tcp_keepalive: 60 seconds (prevent connection drops during streams)
    // - timeout: 30 seconds (default per-request timeout, overridable)
    // 
    // This eliminates repeated TCP handshakes and TLS negotiations,
    // reducing latency for sequential requests in watch/observe loops.
    let mut client_builder = reqwest::Client::builder()
        .pool_max_idle_per_host(10)
        .pool_idle_timeout(Duration::from_secs(90))
        .tcp_keepalive(Duration::from_secs(60))
        // .http2_prior_knowledge()  // Disabled: causes connection issues on Windows
        .timeout(Duration::from_secs(30));

    // Add X-Quiet header if quiet mode is active
    if quiet_mode {
        let mut headers = reqwest::header::HeaderMap::new();
        headers.insert("X-Quiet", "true".parse().unwrap());
        client_builder = client_builder.default_headers(headers);
    }

    let client = client_builder.build()?;
    let term = ui::TerminalInfo::detect();

    match cli.command {
        None => {
            // No command provided - show command directory
            display_all_commands(false, false);
            return Ok(());
        }
        
        Some(command) => match command {
        Commands::Status { at } => {
            let cmd = commands::discovery::StatusCommand::new(quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Offer { offering, action, at, prefer, anywhere_on_fail, placement_mode } => {
            // Handle intelligent placement with "somewhere" keyword or --placement-mode
            if let Some(mode) = placement_mode {
                if let Some(name) = offering {
                    let is_quiet = matches!(mode, PlacementMode::Auto) || quiet_mode;
                    let cmd = commands::offering::OfferCommand::placement_recommend(name.to_string(), is_quiet);
                    dispatch::dispatch_local(&cmd, &client, quiet_mode, fresh_mode, cli.verbose).await?;
                } else {
                    anyhow::bail!("Usage: garden-rake offer <offering> --placement-mode <interactive|auto>");
                }
                return Ok(());
            }
            
            // Handle --at anywhere (query across all stones)
            if at.as_deref() == Some("anywhere") {
                match (offering.as_deref(), action) {
                    (Some("refresh"), None) => {
                        anyhow::bail!("'offer refresh' requires a specific stone (remove --at anywhere)");
                    }
                    (Some(q), None) => {
                        let cmd = commands::offering::OfferCommand::query_anywhere(q.to_string(), prefer, quiet_mode);
                        dispatch::dispatch_local(&cmd, &client, quiet_mode, fresh_mode, cli.verbose).await?;
                    }
                    _ => {
                        anyhow::bail!("Usage with --at anywhere: garden-rake offer <query> --at anywhere [--prefer <token>]");
                    }
                }
                return Ok(());
            }

            // Determine the action to take
            let cmd = match (offering.as_deref(), action) {
                (None, None) => {
                    // List all offerings
                    commands::offering::OfferCommand::list(quiet_mode)
                }
                (Some("refresh"), None) => {
                    // Refresh offerings index
                    commands::offering::OfferCommand::refresh(quiet_mode)
                }
                (Some(name), Some(OfferAction::Info)) => {
                    // Show offering info
                    commands::offering::OfferCommand::info(name.to_string(), quiet_mode)
                }
                (Some(name), None) => {
                    // Could be install or query - need to check if known offering
                    // First resolve endpoint to check
                    let endpoint = dispatch::resolve_endpoint(&client, at.clone(), Some(&*GLOBAL_CACHE)).await?;
                    let is_known = commands::offering::OfferCommand::is_known_offering(&client, &endpoint, name).await;

                    if name != "refresh" && !is_known {
                        // Treat as query
                        let cmd = commands::offering::OfferCommand::query(name.to_string(), prefer.clone(), quiet_mode);
                        let ctx = garden_rake::CommandContext::with_endpoint(
                            client.clone(),
                            endpoint.clone(),
                            None,
                            quiet_mode,
                            false,
                            cli.verbose,
                        );
                        cmd.execute(&ctx).await?;
                        return Ok(());
                    }

                    // Install the offering
                    commands::offering::OfferCommand::install(name.to_string(), prefer, anywhere_on_fail, quiet_mode)
                }
                (None, Some(_)) => {
                    anyhow::bail!("Usage: garden-rake offer <offering> info");
                }
            };

            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::List { at } => {
            let cmd = commands::discovery::ListCommand::new(quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Remove { service, at, force } => {
            let cmd = commands::lifecycle::RemoveCommand::new(service, force, quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Uproot { service, at, force } => {
            let cmd = commands::lifecycle::UprootCommand::new(service, force, quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Adopt { container, at } => {
            let cmd = commands::adoption::AdoptCommand::new(container, quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Release { service, at } => {
            let cmd = commands::adoption::ReleaseCommand::new(service, quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Locate { target, at } => {
            match target {
                LocateTarget::Strays => {
                    let cmd = commands::adoption::LocateStraysCommand::new(quiet_mode);
                    dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
                }
            }
        }

        Commands::Find { query, format, wishful, at } => {
            let output_format = commands::discovery::FindOutputFormat::from_str(&format);
            let wishfully = wishful || parsed_keywords.as_ref().map(|k| k.wishfully).unwrap_or(false);
            let cmd = commands::discovery::FindCommand::new(
                query,
                output_format,
                quiet_mode,
                fresh_mode,
                wishfully,
            );
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Adopted { at } => {
            let cmd = commands::discovery::AdoptedCommand::new(quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Borrowed { at } => {
            let cmd = commands::discovery::BorrowedCommand::new(quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Borrow { name, from, at } => {
            let url_str = from.ok_or_else(|| anyhow::anyhow!(
                "Missing URL. Use: garden-rake borrow {} from <url>", name
            ))?;
            let cmd = commands::adoption::BorrowCommand::new(name, url_str, quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Return { name, at } => {
            let cmd = commands::adoption::ReturnCommand::new(name, quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Upgrade { service, all, at } => {
            let cmd = commands::lifecycle::UpgradeCommand::new(service, all, quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Rest { service, at } => {
            let cmd = commands::lifecycle::RestCommand::new(service, quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Wake { service, at } => {
            let cmd = commands::lifecycle::WakeCommand::new(service, quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Place {
            target,
            code,
            passphrase,
            at,
        } => {
            match commands::management::PlaceCommand::from_args(target, code, passphrase, quiet_mode) {
                Ok(cmd) => {
                    dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
                }
                Err(e) => {
                    eprintln!("{}{} {}", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), e);
                }
            }
        }

        Commands::Invite { at } => {
            let cmd = commands::management::InviteCommand::new(quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Observe { stone, offering } => {
            let cmd = commands::discovery::ObserveCommand::new(stone, offering, quiet_mode);
            dispatch::dispatch_local(&cmd, &client, quiet_mode, fresh_mode, cli.verbose).await?;
        }

        Commands::Watch { target, until, at } => {
            let cmd = match target {
                Some(WatchTarget::Offering { name, mode }) => {
                    let WatchOfferingMode::Logs { timestamps } = mode;
                    commands::discovery::WatchCommand::offering_logs(name, timestamps, quiet_mode)
                }
                Some(WatchTarget::Stone { name, mode }) => {
                    let WatchStoneMode::Logs { timestamps } = mode;
                    commands::discovery::WatchCommand::stone_logs(name, timestamps, quiet_mode)
                }
                None => {
                    commands::discovery::WatchCommand::events(until, quiet_mode)
                }
            };
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Template { command } => {
            use commands::local::TemplateAction;
            let (action, at) = match command {
                TemplateCommands::List { at } => (TemplateAction::List, at),
                TemplateCommands::Show { name, at } => (TemplateAction::Show { name }, at),
            };
            let cmd = commands::local::TemplateCommand::new(action, quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Ceremony { name } => {
            let cmd = commands::local::CeremonyCommand::new(name, quiet_mode);
            dispatch::dispatch_local(&cmd, &client, quiet_mode, fresh_mode, cli.verbose).await?;
        }

        Commands::Tend { target, clear } => {
            let verbose = cli.verbose > 0;
            let cmd = commands::management::TendCommand::new(target, clear, verbose);
            dispatch::dispatch_local(&cmd, &client, quiet_mode, fresh_mode, cli.verbose).await?;
        }

        Commands::Pond { action, at } => {
            use commands::management::PondActionType;
            let action_type = match action {
                PondAction::Init { passphrase } => PondActionType::Init { passphrase },
                PondAction::Status => PondActionType::Status,
                PondAction::Invite => PondActionType::Invite,
                PondAction::Join { code } => PondActionType::Join { code },
                PondAction::Remove => PondActionType::Remove,
                PondAction::Untrust { stone_name } => PondActionType::Untrust { stone_name },
            };
            let cmd = commands::management::PondCommand::new(action_type, quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Lift { target_type, stone_name, at } => {
            use commands::management::LiftTarget;
            let target = match target_type.as_str() {
                "keystone" => LiftTarget::Keystone,
                "stone" => {
                    if stone_name.is_none() {
                        eprintln!("{}{} Error: stone name required for 'lift stone'", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color));
                        eprintln!("{}Example: garden-rake lift stone stone-02", " ".repeat(ui::constants::DEFAULT_INDENT));
                        return Ok(());
                    }
                    LiftTarget::Stone { name: stone_name.unwrap() }
                }
                _ => {
                    eprintln!("{}{} Invalid target: '{}'. Use 'keystone' or 'stone'", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), target_type);
                    return Ok(());
                }
            };
            let cmd = commands::management::LiftCommand::new(target, quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Make { target, action, at } => {
            if target != "stone" {
                eprintln!("{}{} Invalid target: '{}'. Use 'stone'", " ".repeat(ui::constants::DEFAULT_INDENT), ui::status_indicator("error", term.supports_color), target);
                eprintln!("{}Example: garden-rake make stone sing", " ".repeat(ui::constants::DEFAULT_INDENT));
                return Ok(());
            }
            use commands::management::MakeActionType;
            let action_type = match action {
                MakeAction::Sing { forever } => MakeActionType::Sing { forever },
                MakeAction::Quiet => MakeActionType::Quiet,
                MakeAction::Silent => MakeActionType::Silent,
                MakeAction::Minimal => MakeActionType::Minimal,
            };
            let cmd = commands::management::MakeCommand::new(action_type, quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::TakeRoot { at: at_keyword, stone } => {
            // Zen syntax: "garden-rake take-root at windows-01"
            // at_keyword is Some("at"), stone is Some("windows-01")
            let target = if at_keyword.as_deref() == Some("at") {
                stone.clone()
            } else {
                // If at_keyword is not "at", treat it as the stone name (backward compat)
                at_keyword.clone()
            };
            let cmd = commands::admin::InstallServiceCommand::take_root(quiet_mode);
            dispatch::dispatch(&cmd, &client, target, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::InstallService { at } => {
            let cmd = commands::admin::InstallServiceCommand::install_service(quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Rouse { stone, at } => {
            let cmd = commands::admin::RouseCommand::new(stone, quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Slumber { stone, at } => {
            // Merge: positional stone takes precedence, then --at
            let target = stone.or(at);
            let cmd = commands::admin::SlumberCommand::new(quiet_mode);
            dispatch::dispatch(&cmd, &client, target, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::Stir { stone, at } => {
            // Merge: positional stone takes precedence, then --at
            let target = stone.or(at);
            let cmd = commands::admin::StirCommand::new(quiet_mode);
            dispatch::dispatch(&cmd, &client, target, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }

        Commands::BrowseCommands { name, category, zen, normative } => {
            let cmd = commands::local::BrowseCommand::new(name, category, zen, normative);
            dispatch::dispatch_local(&cmd, &client, quiet_mode, fresh_mode, cli.verbose).await?;
        }

        Commands::Refresh { component, from, at } => {
            let endpoint = dispatch::resolve_endpoint(&client, at, Some(&*GLOBAL_CACHE)).await?;
            println!("Refreshing {}...", component);
            refresh_component(&client, &endpoint, &component, &from).await?;
        }

        Commands::Reconcile { drop_invalid, at } => {
            let cmd = commands::management::ReconcileCommand::new(drop_invalid, quiet_mode);
            dispatch::dispatch(&cmd, &client, at, quiet_mode, fresh_mode, cli.verbose, Some(&*GLOBAL_CACHE)).await?;
        }
        }
    }

    Ok(())
}

async fn refresh_component(
    client: &reqwest::Client,
    endpoint: &str,
    component: &str,
    binary_path: &std::path::Path,
) -> anyhow::Result<()> {
    use anyhow::{bail, Context};
    
    // Normalize component name
    let normalized_component = match component.to_lowercase().as_str() {
        "moss" => "moss",
        "rake" | "garden-rake" => garden_common::names::RAKE_BINARY,
        _ => bail!("Unknown component '{}'. Use 'moss' or 'rake'", component),
    };
    
    // Read binary file
    println!("📤 Reading binary file...");
    let binary_data = std::fs::read(binary_path)
        .context(format!("Failed to read binary file: {}", binary_path.display()))?;
    
    let size_mb = binary_data.len() as f64 / 1024.0 / 1024.0;
    println!("   Size: {:.2} MB", size_mb);
    
    // Basic validation: check for ELF header
    if binary_data.len() < 4 || &binary_data[0..4] != b"\x7fELF" {
        bail!("Not a valid ELF binary. Expected Linux executable.");
    }
    println!("   Format: ELF ✓");
    
    // Encode to base64
    println!("📦 Encoding binary...");
    let encoded = base64::engine::general_purpose::STANDARD.encode(&binary_data);
    
    // Send to moss
    println!("🚀 Uploading to stone...");
    let url = format!("{}/api/v1/system/refresh", endpoint.trim_end_matches('/'));
    let response = client
        .post(&url)
        .json(&serde_json::json!({
            "component": normalized_component,
            "binary_data": encoded,
        }))
        .timeout(Duration::from_secs(30))
        .send()
        .await
        .context("Failed to send refresh request")?;
    
    let status = response.status();
    
    // Get response body as text first to see what we got
    let body_text = response.text().await
        .context("Failed to read response body")?;
    
    // Try to parse as JSON
    let body: serde_json::Value = match serde_json::from_str(&body_text) {
        Ok(json) => json,
        Err(e) => {
            println!("✗ Invalid JSON response");
            println!("   Status: {}", status);
            println!("   Response body: {}", body_text.chars().take(500).collect::<String>());
            bail!("Failed to parse JSON response: {}", e);
        }
    };
    
    if !status.is_success() {
        println!("✗ Refresh failed");
        println!("   Status: {}", status);
        if let Some(error) = body.get("error") {
            println!("   Error: {}", error);
        }
        if let Some(message) = body.get("message") {
            println!("   Message: {}", message);
        }
        bail!("Refresh request failed with status {}", status);
    }
    
    // Success
    println!("✅ {} refreshed successfully", normalized_component);
    
    if let Some(arch) = body.get("architecture").and_then(|v| v.as_str()) {
        println!("   Architecture: {}", arch);
    }
    
    if normalized_component == "moss" {
        println!("⏳ Moss is restarting...");
        println!("   (This may take a few seconds)");
        
        // Wait a moment for moss to restart
        tokio::time::sleep(Duration::from_secs(3)).await;
        
        // Try to ping moss
        let health_url = format!("{}/health", endpoint.trim_end_matches('/'));
        for attempt in 1..=5 {
            tokio::time::sleep(Duration::from_secs(1)).await;
            match client.get(&health_url).timeout(Duration::from_secs(2)).send().await {
                Ok(resp) if resp.status().is_success() => {
                    println!("✅ Moss is back online");
                    return Ok(());
                }
                _ => {
                    if attempt < 5 {
                        print!(".");
                        std::io::Write::flush(&mut std::io::stdout()).ok();
                    }
                }
            }
        }
        
        println!("\n⚠️  Moss did not respond after restart (this may be normal)");
        println!("   Check garden-moss status: systemctl status garden-moss.service");
    }
    
    Ok(())
}

// Display functions extracted to commands/help.rs
use commands::help::{display_all_commands, display_command_detail};
