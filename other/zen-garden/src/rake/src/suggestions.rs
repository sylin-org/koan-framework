/// Self-teaching suggestions for garden-rake CLI
///
/// Suggestions are derived from command_manifest.rs `see_also` field,
/// ensuring single source of truth for command relationships.

use crate::command_manifest::MANIFEST;

/// Get suggestions for a command based on its manifest entry
pub fn get_suggestions(command_name: &str) -> Vec<String> {
    let cmd = match MANIFEST.get(command_name) {
        Some(c) => c,
        None => return vec![],
    };

    cmd.see_also
        .iter()
        .filter_map(|related| {
            MANIFEST.get(related).map(|related_cmd| {
                format!("{:<16} {}", related_cmd.zen_name, related_cmd.description)
            })
        })
        .collect()
}

/// Format and print suggestions (unless quiet mode)
pub fn print_suggestions(command_name: &str, quiet_mode: bool) {
    if quiet_mode {
        return;
    }

    let suggestions = get_suggestions(command_name);
    if suggestions.is_empty() {
        return;
    }

    let indent = crate::ui::constants::DEFAULT_INDENT;
    println!();
    println!("{}Related commands:", " ".repeat(indent));
    for suggestion in suggestions.iter().take(3) {
        println!("{}  {}", " ".repeat(indent), suggestion);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::command_manifest::cmd;

    #[test]
    fn test_suggestions_from_manifest() {
        let suggestions = get_suggestions(cmd::LIST);
        // list's see_also is ["observe", "status"] in manifest
        assert!(!suggestions.is_empty());
        assert!(suggestions.iter().any(|s| s.contains("observe")));
    }

    #[test]
    fn test_unknown_command_returns_empty() {
        let suggestions = get_suggestions("nonexistent");
        assert!(suggestions.is_empty());
    }
}
