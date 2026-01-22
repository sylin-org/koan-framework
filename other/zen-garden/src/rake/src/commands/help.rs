//! Command help and directory display functions
//!
//! Provides colored, formatted output for:
//! - Command catalog (`garden-rake` with no args)
//! - Command details (`garden-rake <command>?`)
//! - Category listings (`garden-rake commands <category>`)

use crate::command_manifest::{self, CommandCategory, CommandDef, MANIFEST};
use crate::ui;
use garden_common::CliFormatter;

/// Display detailed information for a specific command
pub fn display_command_detail(cmd: &CommandDef, zen_only: bool, normative_only: bool) {
    let _term = ui::TerminalInfo::detect();
    let indent = " ".repeat(ui::constants::DEFAULT_INDENT);
    let fmt = CliFormatter::new();

    // Title
    println!();
    println!("{}{}", indent, fmt.title(&cmd.zen_name.to_uppercase()));
    if let Some(norm) = cmd.normative_name {
        println!("{}{}", indent, fmt.hint(&format!("(Normative: {})", norm)));
    }
    println!("{}{}", indent, fmt.divider(&"─".repeat(60)));
    println!();

    // Description
    println!("{}{}", indent, cmd.description);
    println!();

    // Category and remote capability
    println!("{}{} {}", indent, fmt.label("Category:"), fmt.value(cmd.category.as_str()));
    println!("{}{} {}", indent, fmt.label("Remote Capable:"), fmt.value(if cmd.remote_capable { "Yes" } else { "No" }));
    println!();

    // Long description
    println!("{}{}", indent, fmt.description(&cmd.long_description.replace('\n', &format!("\n{}", indent))));
    println!();

    // Parameters
    if !cmd.params.is_empty() {
        println!("{}{}", indent, fmt.group("PARAMETERS"));
        for param in &cmd.params {
            let required = if param.required { fmt.label(" (required)") } else { String::new() };
            if !normative_only {
                println!("{}  {} {}{}", indent, fmt.label("Zen:"), fmt.example(param.zen_syntax), required);
            }
            if let Some(norm_syntax) = param.normative_syntax {
                if !zen_only {
                    println!("{}  {} {}{}", indent, fmt.label("Normative:"), fmt.example(norm_syntax), required);
                }
            }
            println!("{}    {}", indent, fmt.description(param.description));
            println!();
        }
    }

    // Examples
    if !cmd.examples.is_empty() {
        if !normative_only && cmd.examples.iter().any(|e| e.zen_syntax.is_some()) {
            println!("{}{}", indent, fmt.group("EXAMPLES (Zen Syntax)"));
            for example in &cmd.examples {
                if let Some(zen_syntax) = example.zen_syntax {
                    println!("{}  {}", indent, fmt.example(zen_syntax));
                    println!("{}    → {}", indent, fmt.description(example.description));
                    println!();
                }
            }
        }

        if !zen_only && cmd.examples.iter().any(|e| e.normative_syntax.is_some()) {
            println!("{}{}", indent, fmt.group("EXAMPLES (Normative Syntax)"));
            for example in &cmd.examples {
                if let Some(norm_syntax) = example.normative_syntax {
                    println!("{}  {}", indent, fmt.example(norm_syntax));
                    println!("{}    → {}", indent, fmt.description(example.description));
                    println!();
                }
            }
        }
    }

    // See also
    if !cmd.see_also.is_empty() {
        println!("{}{} {}", indent, fmt.label("See also:"), fmt.command(&cmd.see_also.join(", ")));
        println!();
    }
}

/// Display commands in a specific category
pub fn display_command_category(category: &CommandCategory, commands: &[&CommandDef], zen_only: bool, normative_only: bool) {
    let indent = " ".repeat(ui::constants::DEFAULT_INDENT);
    let fmt = CliFormatter::new();

    println!();
    println!("{}{}", indent, fmt.title(&format!("{} COMMANDS", category.as_str().to_uppercase())));
    println!("{}{}", indent, fmt.divider(&"═".repeat(60)));
    println!();

    for cmd in commands {
        // Command name(s)
        if !normative_only {
            println!("{}  {}", indent, fmt.command(cmd.zen_name));
        }
        if let Some(norm) = cmd.normative_name {
            if !zen_only {
                println!("{}  {} {}", indent, fmt.command(norm), fmt.hint("(normative)"));
            }
        }
        println!("{}    {}", indent, fmt.description(cmd.description));
        println!();
    }

    println!("{}{}", indent, fmt.hint("Use 'garden-rake commands <name>' for detailed information"));
    println!();
}

/// Display all commands grouped by category (command catalog)
pub fn display_all_commands(_zen_only: bool, normative_only: bool) {
    let indent = " ".repeat(ui::constants::DEFAULT_INDENT);
    let fmt = CliFormatter::new();

    let command_count = MANIFEST.all().len();

    println!();
    println!("{}{}", indent, fmt.title("GARDEN-RAKE"));
    println!("{}{}", indent, fmt.divider(&"─".repeat(47)));
    println!("{}{} commands available", indent, command_count);
    println!();

    // Category-based sections (no ESSENTIALS to avoid duplication)
    let categories = [
        (command_manifest::CommandCategory::Discovery, "DISCOVERY"),
        (command_manifest::CommandCategory::Lifecycle, "SERVICES"),
        (command_manifest::CommandCategory::Adoption, "ADOPTION"),
        (command_manifest::CommandCategory::Management, "MANAGEMENT"),
        (command_manifest::CommandCategory::System, "SYSTEM"),
        (command_manifest::CommandCategory::Pond, "POND (Multi-Stone Security)"),
    ];

    for (category, display_name) in categories {
        let commands = MANIFEST.by_category(&category);
        if !commands.is_empty() {
            println!("{}{}", indent, fmt.group(display_name));
            for cmd in commands {
                if !normative_only {
                    println!("{}    {:<20} {}", indent, fmt.command(cmd.zen_name), fmt.description(cmd.description));
                }
                // Normative variants hidden by default to reduce clutter
            }
            println!();
        }
    }

    // Footer
    println!("{}{}", indent, fmt.divider(&"─".repeat(47)));
    println!("{}{}", indent, fmt.hint("For detailed examples:   garden-rake <command>?"));
    println!("{}{}", indent, fmt.hint("Full directory view:     garden-rake commands"));
    println!();
}
