/// Command manifest system for Zen Garden Rake
/// 
/// This module provides a declarative way to define commands with compile-time validation
/// that ensures every clap command has a corresponding manifest entry.
///
/// Philosophy: Single source of truth - commands are defined once in the manifest,
/// and both the CLI parser and metadata are generated from it.

use once_cell::sync::Lazy;
use std::collections::HashMap;

/// Command name constants - single source of truth for command identifiers.
/// Use these constants instead of string literals when referencing commands.
#[allow(dead_code)]
pub mod cmd {
    // Discovery
    pub const OBSERVE: &str = "observe";
    pub const WATCH: &str = "watch";
    pub const LIST: &str = "list";
    pub const STATUS: &str = "status";

    // Lifecycle
    pub const OFFER: &str = "offer";
    pub const REST: &str = "rest";
    pub const WAKE: &str = "wake";
    pub const REMOVE: &str = "remove";
    pub const UPROOT: &str = "uproot";
    pub const NOURISH: &str = "nourish";

    // Adoption
    pub const ADOPT: &str = "adopt";
    pub const RELEASE: &str = "release";
    pub const FIND: &str = "find";
    pub const ADOPTED: &str = "adopted";
    pub const BORROWED: &str = "borrowed";
    pub const BORROW: &str = "borrow";
    pub const RETURN: &str = "return";

    // Management
    pub const TEND: &str = "tend";
    pub const RECONCILE: &str = "reconcile";
    pub const REFRESH: &str = "refresh";

    // System
    pub const TAKE_ROOT: &str = "take-root";
    pub const MAKE: &str = "make";

    // Pond
    pub const PLACE: &str = "place";
    pub const INVITE: &str = "invite";
    pub const LIFT: &str = "lift";

    // Scaffolded
    pub const CEREMONY: &str = "ceremony";
    pub const TEMPLATE: &str = "template";
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum CommandCategory {
    /// Discovery commands: explore, observe, watch, list, status
    Discovery,
    /// Lifecycle commands: offer, rest, wake, nourish, remove, uproot
    Lifecycle,
    /// Adoption commands: adopt, release, find, adopted, borrowed, borrow, return
    Adoption,
    /// Management commands: reconcile, refresh, tend
    Management,
    /// System commands: take-root, make
    System,
    /// Pond security commands: place, invite, lift
    Pond,
}

impl CommandCategory {
    pub fn as_str(&self) -> &'static str {
        match self {
            Self::Discovery => "Discovery",
            Self::Lifecycle => "Lifecycle",
            Self::Adoption => "Adoption",
            Self::Management => "Management",
            Self::System => "System",
            Self::Pond => "Pond",
        }
    }
}

#[derive(Debug, Clone)]
pub struct CommandExample {
    pub description: &'static str,
    pub zen_syntax: Option<&'static str>,
    pub normative_syntax: Option<&'static str>,
}

#[derive(Debug, Clone)]
#[allow(dead_code)]
pub struct CommandParam {
    pub name: &'static str,
    pub zen_syntax: &'static str,
    pub normative_syntax: Option<&'static str>,
    pub description: &'static str,
    pub required: bool,
}

#[derive(Debug, Clone)]
pub struct CommandDef {
    /// Primary command name (used for lookup)
    pub name: &'static str,
    /// Zen command name (e.g., "take-root")
    pub zen_name: &'static str,
    /// Normative command name (e.g., "install-service"), if different from zen
    pub normative_name: Option<&'static str>,
    /// Command category for grouping
    pub category: CommandCategory,
    /// Short description (one line)
    pub description: &'static str,
    /// Long description (multiple paragraphs)
    pub long_description: &'static str,
    /// Whether command supports --at/at for remote execution
    pub remote_capable: bool,
    /// Command parameters
    pub params: Vec<CommandParam>,
    /// Usage examples
    pub examples: Vec<CommandExample>,
    /// Related commands
    pub see_also: Vec<&'static str>,
}

pub struct CommandManifest {
    commands: HashMap<&'static str, CommandDef>,
}

impl CommandManifest {
    pub fn new() -> Self {
        Self {
            commands: HashMap::new(),
        }
    }

    pub fn add(&mut self, cmd: CommandDef) {
        self.commands.insert(cmd.name, cmd);
    }

    pub fn get(&self, name: &str) -> Option<&CommandDef> {
        self.commands.get(name)
    }

    pub fn by_category(&self, category: &CommandCategory) -> Vec<&CommandDef> {
        self.commands
            .values()
            .filter(|cmd| &cmd.category == category)
            .collect()
    }

    pub fn all(&self) -> Vec<&CommandDef> {
        self.commands.values().collect()
    }
}

/// Global command manifest - initialized at program start
pub static MANIFEST: Lazy<CommandManifest> = Lazy::new(|| {
    let mut manifest = CommandManifest::new();

    // === DISCOVERY COMMANDS ===
    
    manifest.add(CommandDef {
        name: "observe",
        zen_name: "observe",
        normative_name: None,
        category: CommandCategory::Discovery,
        description: "View garden state snapshot",
        long_description: "Observe garden state with optional filtering.\n\n\
            Shows current state of all stones and their offerings in a formatted table.\n\
            Provides snapshot view of the entire garden or filtered by stone/offering.",
        remote_capable: false,
        params: vec![
            CommandParam {
                name: "stone",
                zen_syntax: "<stone>",
                normative_syntax: None,
                description: "Filter by specific stone name",
                required: false,
            },
            CommandParam {
                name: "offering",
                zen_syntax: "--offering <name>",
                normative_syntax: None,
                description: "Filter by offering name (comma-separated)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Observe all stones in garden",
                zen_syntax: Some("garden-rake observe"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Observe specific stone with all offerings",
                zen_syntax: Some("garden-rake observe stone-01"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Filter by specific offerings across all stones",
                zen_syntax: Some("garden-rake observe --offering mongodb,redis"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Observe stone with offering filter",
                zen_syntax: Some("garden-rake observe stone-01 --offering mongodb"),
                normative_syntax: None,
            },
        ],
        see_also: vec!["watch", "list"],
    });

    manifest.add(CommandDef {
        name: "watch",
        zen_name: "watch",
        normative_name: None,
        category: CommandCategory::Discovery,
        description: "Stream real-time events from stone",
        long_description: "Stream real-time events from moss operations.\n\n\
            Watch provides live updates on container lifecycle, offering installations, and system events.\n\
            Can monitor general events or specific offering logs.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "at",
                zen_syntax: "at <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
            CommandParam {
                name: "until",
                zen_syntax: "until <condition>",
                normative_syntax: Some("--until <condition>"),
                description: "Exit when string appears in event stream",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Watch all events from tended stone",
                zen_syntax: Some("garden-rake watch"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Watch specific stone until completion",
                zen_syntax: Some("garden-rake watch at stone-01 until 'completed'"),
                normative_syntax: Some("garden-rake watch --at stone-01 --until 'completed'"),
            },
            CommandExample {
                description: "Watch with explicit endpoint",
                zen_syntax: Some("garden-rake watch at http://192.168.1.108:7185"),
                normative_syntax: Some("garden-rake watch --at http://192.168.1.108:7185"),
            },
            CommandExample {
                description: "Watch offering logs",
                zen_syntax: Some("garden-rake watch offering mongodb logs"),
                normative_syntax: None,
            },
        ],
        see_also: vec!["observe", "make"],
    });

    manifest.add(CommandDef {
        name: "list",
        zen_name: "list",
        normative_name: None,
        category: CommandCategory::Discovery,
        description: "List services on stone",
        long_description: "List all services (offerings) currently running on a stone.\n\n\
            Shows service names, status, ports, and basic health information.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "at",
                zen_syntax: "at <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "List services on tended stone",
                zen_syntax: Some("garden-rake list"),
                normative_syntax: None,
            },
            CommandExample {
                description: "List services on specific stone",
                zen_syntax: Some("garden-rake list at stone-01"),
                normative_syntax: Some("garden-rake list --at stone-01"),
            },
        ],
        see_also: vec!["observe", "status"],
    });

    // === LIFECYCLE COMMANDS ===
    
    manifest.add(CommandDef {
        name: "offer",
        zen_name: "offer",
        normative_name: None,
        category: CommandCategory::Lifecycle,
        description: "Install or list offerings",
        long_description: "Manage offerings (services) - list available offerings or install specific ones.\n\n\
            Offerings are validated container templates. Installation includes compatibility checks,\n\
            hardware requirements validation, and automatic fallback recommendations.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "offering",
                zen_syntax: "<offering>",
                normative_syntax: None,
                description: "Offering name (omit to list all)",
                required: false,
            },
            CommandParam {
                name: "at",
                zen_syntax: "at <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
            CommandParam {
                name: "prefer",
                zen_syntax: "--prefer <hardware>",
                normative_syntax: None,
                description: "Bias recommendations (e.g., ssd, nvme)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "List all available offerings by category",
                zen_syntax: Some("garden-rake offer"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Install offering on tended stone",
                zen_syntax: Some("garden-rake offer mongodb"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Install on specific stone with hardware preference",
                zen_syntax: Some("garden-rake offer mongodb at stone-01 --prefer ssd"),
                normative_syntax: Some("garden-rake offer mongodb --at stone-01 --prefer ssd"),
            },
            CommandExample {
                description: "Show offering details and compatibility",
                zen_syntax: Some("garden-rake offer mongodb info"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Install with automatic fallback to any stone",
                zen_syntax: Some("garden-rake offer mongodb --anywhere-on-fail"),
                normative_syntax: None,
            },
        ],
        see_also: vec!["release", "list"],
    });

    manifest.add(CommandDef {
        name: "rest",
        zen_name: "rest",
        normative_name: None,
        category: CommandCategory::Lifecycle,
        description: "Stop a service (rest mode)",
        long_description: "Stop a running service without removing it.\n\n\
            Service enters rest mode and can be woken later with all data preserved.\n\
            Container is stopped but not removed.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "service",
                zen_syntax: "<service>",
                normative_syntax: None,
                description: "Service name to stop",
                required: true,
            },
            CommandParam {
                name: "at",
                zen_syntax: "at <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Put service to rest on tended stone",
                zen_syntax: Some("garden-rake rest mongodb"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Put service to rest on specific stone",
                zen_syntax: Some("garden-rake rest mongodb at stone-01"),
                normative_syntax: Some("garden-rake rest mongodb --at stone-01"),
            },
        ],
        see_also: vec!["wake", "release"],
    });

    manifest.add(CommandDef {
        name: "wake",
        zen_name: "wake",
        normative_name: None,
        category: CommandCategory::Lifecycle,
        description: "Start a service (wake from rest)",
        long_description: "Start a service that is in rest mode.\n\n\
            Service resumes with all previous data and configuration intact.\n\
            Container is started from stopped state.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "service",
                zen_syntax: "<service>",
                normative_syntax: None,
                description: "Service name to start",
                required: true,
            },
            CommandParam {
                name: "at",
                zen_syntax: "at <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Wake service on tended stone",
                zen_syntax: Some("garden-rake wake mongodb"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Wake service on specific stone",
                zen_syntax: Some("garden-rake wake mongodb at stone-01"),
                normative_syntax: Some("garden-rake wake mongodb --at stone-01"),
            },
        ],
        see_also: vec!["rest", "offer"],
    });

    manifest.add(CommandDef {
        name: "remove",
        zen_name: "remove",
        normative_name: Some("services delete"),
        category: CommandCategory::Lifecycle,
        description: "Remove service from registry (soft delete)",
        long_description: "Remove a service from moss registry without destroying the container.\n\n\
            The container becomes a 'stray' - still running but unmanaged.\n\
            Use 'uproot' for hard delete (destroy container and data).",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "service",
                zen_syntax: "<service>",
                normative_syntax: None,
                description: "Service name to remove from registry",
                required: true,
            },
            CommandParam {
                name: "at",
                zen_syntax: "on <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Remove service from registry (container preserved)",
                zen_syntax: Some("garden-rake remove mongodb"),
                normative_syntax: Some("garden-rake services delete mongodb"),
            },
            CommandExample {
                description: "Remove service on specific stone",
                zen_syntax: Some("garden-rake remove mongodb on stone-01"),
                normative_syntax: Some("garden-rake services delete mongodb --at stone-01"),
            },
        ],
        see_also: vec!["uproot", "adopt", "find"],
    });

    manifest.add(CommandDef {
        name: "uproot",
        zen_name: "uproot",
        normative_name: Some("services destroy"),
        category: CommandCategory::Lifecycle,
        description: "Destroy service completely (hard delete)",
        long_description: "Permanently destroy a service including container and data.\n\n\
            This is irreversible - container and volumes are deleted.\n\
            Use --force to skip confirmation prompt.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "service",
                zen_syntax: "<service>",
                normative_syntax: None,
                description: "Service name to destroy",
                required: true,
            },
            CommandParam {
                name: "force",
                zen_syntax: "--force",
                normative_syntax: None,
                description: "Skip confirmation prompt",
                required: false,
            },
            CommandParam {
                name: "at",
                zen_syntax: "on <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Destroy service with confirmation",
                zen_syntax: Some("garden-rake uproot mongodb"),
                normative_syntax: Some("garden-rake services destroy mongodb"),
            },
            CommandExample {
                description: "Destroy service without confirmation",
                zen_syntax: Some("garden-rake uproot mongodb --force"),
                normative_syntax: Some("garden-rake services destroy mongodb --force"),
            },
            CommandExample {
                description: "Destroy service on specific stone",
                zen_syntax: Some("garden-rake uproot mongodb on stone-01"),
                normative_syntax: Some("garden-rake services destroy mongodb --at stone-01"),
            },
        ],
        see_also: vec!["remove", "rest"],
    });

    manifest.add(CommandDef {
        name: "nourish",
        zen_name: "nourish",
        normative_name: Some("services upgrade"),
        category: CommandCategory::Lifecycle,
        description: "Upgrade service to latest version",
        long_description: "Upgrade one or all services to their latest versions.\n\n\
            Pulls latest container images and recreates services with data preserved.\n\
            Use --all to upgrade all services on stone.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "service",
                zen_syntax: "<service>",
                normative_syntax: None,
                description: "Service name (omit with --all)",
                required: false,
            },
            CommandParam {
                name: "all",
                zen_syntax: "--all",
                normative_syntax: None,
                description: "Upgrade all services on stone",
                required: false,
            },
            CommandParam {
                name: "at",
                zen_syntax: "at <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Upgrade specific service",
                zen_syntax: Some("garden-rake nourish mongodb"),
                normative_syntax: Some("garden-rake services upgrade mongodb"),
            },
            CommandExample {
                description: "Upgrade all services on stone",
                zen_syntax: Some("garden-rake nourish --all"),
                normative_syntax: Some("garden-rake services upgrade --all"),
            },
            CommandExample {
                description: "Upgrade service on specific stone",
                zen_syntax: Some("garden-rake nourish mongodb at stone-01"),
                normative_syntax: Some("garden-rake services upgrade mongodb --at stone-01"),
            },
        ],
        see_also: vec!["offer", "reconcile"],
    });

    // === ADOPTION COMMANDS ===

    manifest.add(CommandDef {
        name: "adopt",
        zen_name: "adopt",
        normative_name: Some("adoption claim"),
        category: CommandCategory::Adoption,
        description: "Adopt a stray container or detected service",
        long_description: "Claim an existing container or detected service into moss management.\n\n\
            Strays are containers that exist but aren't in moss registry.\n\
            Adopted services are external services (not containers) that moss monitors.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "target",
                zen_syntax: "<container|offering>",
                normative_syntax: None,
                description: "Container name or offering name to claim",
                required: true,
            },
            CommandParam {
                name: "at",
                zen_syntax: "on <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Adopt a stray container",
                zen_syntax: Some("garden-rake adopt my-mongodb"),
                normative_syntax: Some("garden-rake adoption claim my-mongodb"),
            },
            CommandExample {
                description: "Adopt offering on specific stone",
                zen_syntax: Some("garden-rake adopt mongodb on stone-01"),
                normative_syntax: Some("garden-rake adoption claim mongodb --at stone-01"),
            },
        ],
        see_also: vec!["release", "find", "adopted"],
    });

    manifest.add(CommandDef {
        name: "release",
        zen_name: "release",
        normative_name: Some("adoption release"),
        category: CommandCategory::Adoption,
        description: "Release an adopted service from management",
        long_description: "Release an adopted service from moss management.\n\n\
            The service continues running but is no longer monitored by moss.\n\
            Does not affect borrowed services - use 'return' for those.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "service",
                zen_syntax: "<service>",
                normative_syntax: None,
                description: "Adopted service name to release",
                required: true,
            },
            CommandParam {
                name: "at",
                zen_syntax: "on <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Release adopted service",
                zen_syntax: Some("garden-rake release mongodb"),
                normative_syntax: Some("garden-rake adoption release mongodb"),
            },
            CommandExample {
                description: "Release on specific stone",
                zen_syntax: Some("garden-rake release mongodb on stone-01"),
                normative_syntax: Some("garden-rake adoption release mongodb --at stone-01"),
            },
        ],
        see_also: vec!["adopt", "adopted"],
    });

    manifest.add(CommandDef {
        name: "find",
        zen_name: "find",
        normative_name: Some("adoption find"),
        category: CommandCategory::Adoption,
        description: "Find adoptable services",
        long_description: "Find services available for adoption.\n\n\
            'find strays' - Find containers not in moss registry (strays).\n\
            Future: 'find services' - Detect external services that could be adopted.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "target",
                zen_syntax: "<strays>",
                normative_syntax: None,
                description: "'strays' to find unmanaged containers",
                required: true,
            },
            CommandParam {
                name: "at",
                zen_syntax: "on <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Find stray containers",
                zen_syntax: Some("garden-rake find strays"),
                normative_syntax: Some("garden-rake adoption find strays"),
            },
            CommandExample {
                description: "Find strays on specific stone",
                zen_syntax: Some("garden-rake find strays on stone-01"),
                normative_syntax: Some("garden-rake adoption find strays --at stone-01"),
            },
        ],
        see_also: vec!["adopt", "adopted"],
    });

    manifest.add(CommandDef {
        name: "adopted",
        zen_name: "adopted",
        normative_name: Some("adoption list"),
        category: CommandCategory::Adoption,
        description: "List adopted services",
        long_description: "List all services currently adopted (external services under moss management).\n\n\
            Adopted services are not containers - they're external services moss monitors.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "at",
                zen_syntax: "on <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "List adopted services",
                zen_syntax: Some("garden-rake adopted"),
                normative_syntax: Some("garden-rake adoption list"),
            },
            CommandExample {
                description: "List adopted on specific stone",
                zen_syntax: Some("garden-rake adopted on stone-01"),
                normative_syntax: Some("garden-rake adoption list --at stone-01"),
            },
        ],
        see_also: vec!["adopt", "release", "borrowed"],
    });

    manifest.add(CommandDef {
        name: "borrowed",
        zen_name: "borrowed",
        normative_name: Some("adoption list-borrowed"),
        category: CommandCategory::Adoption,
        description: "List borrowed (external) services",
        long_description: "List all borrowed services (external network services registered for reference).\n\n\
            Borrowed services are external services not managed by this stone,\n\
            but registered for service discovery and reference.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "at",
                zen_syntax: "on <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "List borrowed services",
                zen_syntax: Some("garden-rake borrowed"),
                normative_syntax: Some("garden-rake adoption list-borrowed"),
            },
            CommandExample {
                description: "List borrowed on specific stone",
                zen_syntax: Some("garden-rake borrowed on stone-01"),
                normative_syntax: Some("garden-rake adoption list-borrowed --at stone-01"),
            },
        ],
        see_also: vec!["borrow", "return", "adopted"],
    });

    manifest.add(CommandDef {
        name: "borrow",
        zen_name: "borrow",
        normative_name: Some("adoption borrow"),
        category: CommandCategory::Adoption,
        description: "Register an external service",
        long_description: "Register an external (borrowed) service for reference and discovery.\n\n\
            Borrowed services are external network services not managed by this stone.\n\
            They're registered so other services can discover and connect to them.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "name",
                zen_syntax: "<name>",
                normative_syntax: None,
                description: "Name for this borrowed service",
                required: true,
            },
            CommandParam {
                name: "from",
                zen_syntax: "from <url>",
                normative_syntax: Some("--url <url>"),
                description: "URL/connection string for the external service",
                required: true,
            },
            CommandParam {
                name: "at",
                zen_syntax: "on <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Borrow external Redis",
                zen_syntax: Some("garden-rake borrow redis from redis://cache.corp:6379"),
                normative_syntax: Some("garden-rake adoption borrow redis --url redis://cache.corp:6379"),
            },
            CommandExample {
                description: "Borrow external PostgreSQL",
                zen_syntax: Some("garden-rake borrow prod-db from postgres://db.corp:5432/main"),
                normative_syntax: Some("garden-rake adoption borrow prod-db --url postgres://db.corp:5432/main"),
            },
            CommandExample {
                description: "Borrow on specific stone",
                zen_syntax: Some("garden-rake borrow redis from redis://cache:6379 on stone-01"),
                normative_syntax: Some("garden-rake adoption borrow redis --url redis://cache:6379 --at stone-01"),
            },
        ],
        see_also: vec!["return", "borrowed"],
    });

    manifest.add(CommandDef {
        name: "return",
        zen_name: "return",
        normative_name: Some("adoption unborrow"),
        category: CommandCategory::Adoption,
        description: "Unregister a borrowed service",
        long_description: "Unregister a borrowed service (doesn't affect the external service).\n\n\
            Removes the service from moss's borrowed registry.\n\
            The external service continues running unaffected.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "name",
                zen_syntax: "<name>",
                normative_syntax: None,
                description: "Name of the borrowed service to return",
                required: true,
            },
            CommandParam {
                name: "at",
                zen_syntax: "on <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Return (unregister) borrowed service",
                zen_syntax: Some("garden-rake return redis"),
                normative_syntax: Some("garden-rake adoption unborrow redis"),
            },
            CommandExample {
                description: "Return on specific stone",
                zen_syntax: Some("garden-rake return redis on stone-01"),
                normative_syntax: Some("garden-rake adoption unborrow redis --at stone-01"),
            },
        ],
        see_also: vec!["borrow", "borrowed"],
    });

    manifest.add(CommandDef {
        name: "status",
        zen_name: "status",
        normative_name: Some("services status"),
        category: CommandCategory::Discovery,
        description: "Show service status",
        long_description: "Show detailed status of a specific service.\n\n\
            Includes health, ports, resource usage, and recent events.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "service",
                zen_syntax: "<service>",
                normative_syntax: None,
                description: "Service name to query",
                required: true,
            },
            CommandParam {
                name: "at",
                zen_syntax: "on <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Show MongoDB status",
                zen_syntax: Some("garden-rake status mongodb"),
                normative_syntax: Some("garden-rake services status mongodb"),
            },
            CommandExample {
                description: "Show status on specific stone",
                zen_syntax: Some("garden-rake status mongodb on stone-01"),
                normative_syntax: Some("garden-rake services status mongodb --at stone-01"),
            },
        ],
        see_also: vec!["list", "observe"],
    });

    // === MANAGEMENT COMMANDS ===
    
    manifest.add(CommandDef {
        name: "tend",
        zen_name: "tend",
        normative_name: None,
        category: CommandCategory::Management,
        description: "Set which stone rake commands target",
        long_description: "Manage which stone garden-rake commands target.\n\n\
            Tending establishes a context that persists for 90 seconds and affects all subsequent commands.\n\
            Commands with --at/at will override the tended context temporarily.",
        remote_capable: false,
        params: vec![
            CommandParam {
                name: "target",
                zen_syntax: "<target>",
                normative_syntax: None,
                description: "'this', 'local', 'auto', or explicit endpoint URL",
                required: false,
            },
            CommandParam {
                name: "clear",
                zen_syntax: "--clear",
                normative_syntax: None,
                description: "Clear tending state",
                required: false,
            },
            CommandParam {
                name: "verbose",
                zen_syntax: "-v / --verbose",
                normative_syntax: None,
                description: "Show verbose tending information",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Show current tending state",
                zen_syntax: Some("garden-rake tend"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Tend to localhost",
                zen_syntax: Some("garden-rake tend this"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Auto-discover and tend to nearest stone",
                zen_syntax: Some("garden-rake tend auto"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Tend to explicit endpoint",
                zen_syntax: Some("garden-rake tend http://192.168.1.108:7185"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Stop tending (clear state)",
                zen_syntax: Some("garden-rake tend --clear"),
                normative_syntax: None,
            },
        ],
        see_also: vec!["observe", "watch"],
    });

    manifest.add(CommandDef {
        name: "reconcile",
        zen_name: "reconcile",
        normative_name: None,
        category: CommandCategory::Management,
        description: "Adopt existing containers",
        long_description: "Force moss to reconcile its registry with existing zen-offering containers.\n\n\
            Useful after moss restart/update, or if containers were created externally.\n\
            Can optionally remove invalid zen-offering-* containers.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "drop-invalid",
                zen_syntax: "--drop-invalid",
                normative_syntax: None,
                description: "Remove invalid zen-offering-* containers",
                required: false,
            },
            CommandParam {
                name: "at",
                zen_syntax: "at <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Adopt any missing containers",
                zen_syntax: Some("garden-rake reconcile"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Reconcile and remove invalid containers",
                zen_syntax: Some("garden-rake reconcile --drop-invalid"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Reconcile specific stone",
                zen_syntax: Some("garden-rake reconcile at stone-01"),
                normative_syntax: Some("garden-rake reconcile --at stone-01"),
            },
        ],
        see_also: vec!["nourish", "refresh"],
    });

    manifest.add(CommandDef {
        name: "refresh",
        zen_name: "refresh",
        normative_name: None,
        category: CommandCategory::Management,
        description: "Update moss or rake binary",
        long_description: "Update garden-moss or garden-rake binary on a stone (development use).\n\n\
            Binary is validated for architecture compatibility before installation.\n\
            Garden-Moss automatically restarts after update.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "component",
                zen_syntax: "<component>",
                normative_syntax: None,
                description: "'moss' or 'rake'",
                required: true,
            },
            CommandParam {
                name: "from",
                zen_syntax: "--from <path>",
                normative_syntax: None,
                description: "Path to binary file",
                required: true,
            },
            CommandParam {
                name: "at",
                zen_syntax: "at <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Update moss binary",
                zen_syntax: Some("garden-rake refresh moss --from ./target/release/garden-moss"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Update rake binary",
                zen_syntax: Some("garden-rake refresh rake --from ./dist/linux-x64/garden-rake"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Update moss on specific stone",
                zen_syntax: Some("garden-rake refresh moss --from ./garden-moss at stone-01"),
                normative_syntax: Some("garden-rake refresh moss --from ./garden-moss --at stone-01"),
            },
        ],
        see_also: vec!["reconcile"],
    });

    // === SYSTEM COMMANDS ===
    
    manifest.add(CommandDef {
        name: "take-root",
        zen_name: "take-root",
        normative_name: Some("install-service"),
        category: CommandCategory::System,
        description: "Install moss as a system service",
        long_description: "Install moss as a Windows system service (zen: take-root).\n\n\
            The stone will install itself as a system service and start automatically.\n\
            Requires administrator privileges on the target Windows machine.\n\
            If running from removable media (USB), automatically copies to C:\\ProgramData\\ZenGarden.\n\n\
            To uninstall: sc delete ZenGardenMoss",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "at",
                zen_syntax: "at <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Install service on tended stone",
                zen_syntax: Some("garden-rake take-root"),
                normative_syntax: Some("garden-rake install-service"),
            },
            CommandExample {
                description: "Install service on specific stone",
                zen_syntax: Some("garden-rake take-root at windows-01"),
                normative_syntax: Some("garden-rake install-service --at windows-01"),
            },
            CommandExample {
                description: "Local installation (on Windows machine running moss)",
                zen_syntax: Some("garden-moss take-root"),
                normative_syntax: Some("garden-moss install-service"),
            },
            CommandExample {
                description: "Verify service installation",
                zen_syntax: Some("sc query ZenGardenMoss"),
                normative_syntax: None,
            },
        ],
        see_also: vec!["lift", "make"],
    });

    manifest.add(CommandDef {
        name: "make",
        zen_name: "make",
        normative_name: None,
        category: CommandCategory::System,
        description: "Control stone console output",
        long_description: "Control stone console output verbosity.\n\n\
            Modes:\n\
            silent       - No console output (systemd/service use)\n\
            minimal      - Critical events only\n\
            informative  - Major lifecycle events (default)\n\
            verbose      - Full debug output (sing mode)",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "target",
                zen_syntax: "<target>",
                normative_syntax: None,
                description: "'stone'",
                required: true,
            },
            CommandParam {
                name: "action",
                zen_syntax: "<action>",
                normative_syntax: None,
                description: "'sing', 'quiet', 'silent'",
                required: true,
            },
            CommandParam {
                name: "duration",
                zen_syntax: "<duration>",
                normative_syntax: None,
                description: "'forever' or omit for 30min timeout",
                required: false,
            },
            CommandParam {
                name: "at",
                zen_syntax: "at <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Enable verbose output (30min timeout)",
                zen_syntax: Some("garden-rake make stone sing"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Enable verbose output permanently",
                zen_syntax: Some("garden-rake make stone sing forever"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Reset to default (informative)",
                zen_syntax: Some("garden-rake make stone quiet"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Disable console output",
                zen_syntax: Some("garden-rake make stone silent"),
                normative_syntax: None,
            },
            CommandExample {
                description: "Control specific stone output",
                zen_syntax: Some("garden-rake make stone sing at stone-01"),
                normative_syntax: Some("garden-rake make stone sing --at stone-01"),
            },
        ],
        see_also: vec!["watch", "take-root"],
    });

    // === POND COMMANDS ===
    
    manifest.add(CommandDef {
        name: "place",
        zen_name: "place",
        normative_name: Some("pond init / pond join"),
        category: CommandCategory::Pond,
        description: "Initialize pond or join pond",
        long_description: "Initialize pond (place keystone) or join existing pond (place stone).\n\n\
            Pond security enables multi-stone trust relationships with encrypted certificates.\n\
            Phase 3b feature - implementation pending.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "target",
                zen_syntax: "<target>",
                normative_syntax: None,
                description: "'keystone' or 'stone'",
                required: true,
            },
            CommandParam {
                name: "code",
                zen_syntax: "--code <code>",
                normative_syntax: None,
                description: "Invitation code (required for 'stone')",
                required: false,
            },
            CommandParam {
                name: "passphrase",
                zen_syntax: "--passphrase <pass>",
                normative_syntax: None,
                description: "Encrypt pond certificate (keystone only)",
                required: false,
            },
            CommandParam {
                name: "at",
                zen_syntax: "at <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Initialize pond (place keystone)",
                zen_syntax: Some("garden-rake place keystone"),
                normative_syntax: Some("garden-rake pond init"),
            },
            CommandExample {
                description: "Initialize with passphrase",
                zen_syntax: Some("garden-rake place keystone --passphrase mypass"),
                normative_syntax: Some("garden-rake pond init --passphrase mypass"),
            },
            CommandExample {
                description: "Join pond (place stone)",
                zen_syntax: Some("garden-rake place stone --code ABC123"),
                normative_syntax: Some("garden-rake pond join ABC123"),
            },
            CommandExample {
                description: "Join pond on specific stone",
                zen_syntax: Some("garden-rake place stone --code ABC123 at stone-02"),
                normative_syntax: Some("garden-rake pond join ABC123 --at stone-02"),
            },
        ],
        see_also: vec!["invite", "lift"],
    });

    manifest.add(CommandDef {
        name: "invite",
        zen_name: "invite",
        normative_name: Some("pond invite"),
        category: CommandCategory::Pond,
        description: "Generate pond invitation code",
        long_description: "Generate pond invitation code for adding stones to pond.\n\n\
            Invitation codes expire after 24 hours or first use.\n\
            Phase 3b feature - implementation pending.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "at",
                zen_syntax: "at <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Generate invitation code",
                zen_syntax: Some("garden-rake invite"),
                normative_syntax: Some("garden-rake pond invite"),
            },
            CommandExample {
                description: "Generate code from specific keystone",
                zen_syntax: Some("garden-rake invite at stone-01"),
                normative_syntax: Some("garden-rake pond invite --at stone-01"),
            },
        ],
        see_also: vec!["place", "lift"],
    });

    manifest.add(CommandDef {
        name: "lift",
        zen_name: "lift",
        normative_name: Some("pond untrust / pond remove"),
        category: CommandCategory::Pond,
        description: "Remove stone from pond",
        long_description: "Remove a stone from pond or remove entire pond from stone.\n\n\
            Can remove specific stone (untrust) or remove keystone (destroy pond).\n\
            Phase 3b feature - implementation pending.",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "target_type",
                zen_syntax: "<type>",
                normative_syntax: None,
                description: "'keystone' or 'stone'",
                required: true,
            },
            CommandParam {
                name: "stone_name",
                zen_syntax: "<stone>",
                normative_syntax: None,
                description: "Stone name (required if type is 'stone')",
                required: false,
            },
            CommandParam {
                name: "at",
                zen_syntax: "at <stone>",
                normative_syntax: Some("--at <stone>"),
                description: "Target stone (omit to use tended stone)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Remove specific stone from pond",
                zen_syntax: Some("garden-rake lift stone stone-02"),
                normative_syntax: Some("garden-rake pond untrust stone-02"),
            },
            CommandExample {
                description: "Remove pond from stone (leave pond)",
                zen_syntax: Some("garden-rake lift keystone"),
                normative_syntax: Some("garden-rake pond remove"),
            },
            CommandExample {
                description: "Untrust stone from specific keystone",
                zen_syntax: Some("garden-rake lift stone stone-02 at stone-01"),
                normative_syntax: Some("garden-rake pond untrust stone-02 --at stone-01"),
            },
        ],
        see_also: vec!["place", "invite"],
    });

    // === SCAFFOLDED COMMANDS ===
    // These commands are recognized but output placeholder messages until fully implemented

    manifest.add(CommandDef {
        name: cmd::CEREMONY,
        zen_name: "ceremony",
        normative_name: None,
        category: CommandCategory::Management,
        description: "Run guided workflows (coming soon)",
        long_description: "Ceremony provides guided workflows for common multi-step operations.\n\n\
            Scaffolded - implementation pending. Will include:\n\
            - ceremony bootstrap: First-time setup wizard\n\
            - ceremony migrate: Service migration workflow\n\
            - ceremony backup: Guided backup configuration",
        remote_capable: false,
        params: vec![
            CommandParam {
                name: "workflow",
                zen_syntax: "<workflow>",
                normative_syntax: None,
                description: "Workflow name (bootstrap, migrate, backup)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "Run bootstrap ceremony",
                zen_syntax: Some("garden-rake ceremony bootstrap"),
                normative_syntax: None,
            },
        ],
        see_also: vec!["offer", "tend"],
    });

    manifest.add(CommandDef {
        name: cmd::TEMPLATE,
        zen_name: "template",
        normative_name: None,
        category: CommandCategory::Management,
        description: "Manage offering templates (coming soon)",
        long_description: "Template operations for custom offering definitions.\n\n\
            Scaffolded - implementation pending. Will include:\n\
            - template list: List available templates\n\
            - template show: Display template details\n\
            - template create: Create custom template",
        remote_capable: true,
        params: vec![
            CommandParam {
                name: "action",
                zen_syntax: "<action>",
                normative_syntax: None,
                description: "Action (list, show, create)",
                required: false,
            },
        ],
        examples: vec![
            CommandExample {
                description: "List templates",
                zen_syntax: Some("garden-rake template list"),
                normative_syntax: None,
            },
        ],
        see_also: vec!["offer"],
    });

    manifest
});

/// Validate that the manifest contains expected commands
/// This is called at startup in debug builds to catch inconsistencies
#[cfg(debug_assertions)]
pub fn validate_manifest() {
    let expected_commands = vec![
        // Discovery
        "observe", "watch", "list", "status",
        // Lifecycle
        "offer", "rest", "wake", "remove", "uproot", "nourish",
        // Adoption
        "adopt", "release", "find", "adopted", "borrowed", "borrow", "return",
        // Management
        "tend", "reconcile", "refresh", "ceremony", "template",
        // System
        "take-root", "make",
        // Pond
        "place", "invite", "lift",
    ];

    for cmd_name in expected_commands {
        assert!(
            MANIFEST.get(cmd_name).is_some(),
            "Command '{}' missing from manifest",
            cmd_name
        );
    }

    println!("✓ Command manifest validated: {} commands registered", MANIFEST.all().len());
}
