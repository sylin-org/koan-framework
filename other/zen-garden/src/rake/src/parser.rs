/// Zen parser for positional keyword extraction
///
/// Parses command-line arguments to detect zen syntax and extract positional keywords
/// before they reach Clap. Supports:
/// - `on <stone>` / `at <stone>` - target stone (on is preferred, at is legacy alias)
/// - `from <url>` - source URL for borrow command
/// - `quietly` - suppress non-essential output
/// - `fresh` - clear cache and force fresh discovery
/// - `until <condition>` - stream termination condition

use anyhow::{Result, anyhow};

#[derive(Debug, Clone, PartialEq)]
pub enum CommandStyle {
    Zen,
    Normative,
}

#[derive(Debug, Clone, Default)]
pub struct ParsedKeywords {
    pub on_stone: Option<String>,    // `on <stone>` or `at <stone>` (legacy)
    pub from_url: Option<String>,    // `from <url>` for borrow
    pub quietly: bool,
    pub fresh: bool,                 // clear cache and force fresh discovery
    pub until_condition: Option<String>,
    pub somewhere: bool,             // intelligent placement
}


#[derive(Debug, Clone)]
pub struct ParsedCommand {
    pub style: CommandStyle,
    pub verb: String,
    pub args: Vec<String>,
    pub keywords: ParsedKeywords,
}

/// Parse raw args to detect zen vs normative and extract positional keywords
pub fn parse_args(args: Vec<String>) -> Result<ParsedCommand> {
    if args.is_empty() {
        return Err(anyhow!("No command provided"));
    }

    let first_arg = &args[0];
    
    // Detect style based on first argument
    let style = if is_zen_verb(first_arg) {
        CommandStyle::Zen
    } else if first_arg.starts_with("--") || first_arg.starts_with("-") {
        CommandStyle::Normative
    } else if is_normative_verb(first_arg) {
        CommandStyle::Normative
    } else {
        // Unknown verb, let Clap handle the error
        return Err(anyhow!("Unknown command: {}", first_arg));
    };

    // Extract keywords and filter out from args
    let (keywords, filtered_args) = extract_keywords(&args[1..], &style)?;

    // Validate: no mixing of zen positional keywords with normative flags
    if style == CommandStyle::Normative && has_zen_keywords(&args) {
        return Err(anyhow!(
            "Cannot mix normative syntax with zen positional keywords. Use either:\n  \
             Zen:       {} {} quietly\n  \
             Normative: {} {} --quiet",
            first_arg, filtered_args.join(" "), first_arg, filtered_args.join(" ")
        ));
    }

    Ok(ParsedCommand {
        style,
        verb: first_arg.clone(),
        args: filtered_args,
        keywords,
    })
}

/// Check if a verb is a zen verb
fn is_zen_verb(verb: &str) -> bool {
    matches!(
        verb,
        // Service lifecycle (zen)
        "offer" | "rest" | "wake" | "nourish" | "remove" | "uproot" |
        // Adoption (zen)
        "adopt" | "release" | "find" | "adopted" | "borrowed" |
        // External services (zen)
        "borrow" | "return" |
        // Observation (zen)
        "observe" | "watch" | "list" | "status" |
        // Context (zen)
        "tend" |
        // Pond (zen)
        "place" | "lift" | "invite" |
        // Admin (zen)
        "make" | "refresh" | "reconcile" | "template" | "ceremony" |
        // Installation (zen alias)
        "take-root" |
        // Discovery/aliases (zen)
        "explore" | "touch" | "garden"
    )
}

/// Check if a verb is a normative verb (resource-first pattern)
fn is_normative_verb(verb: &str) -> bool {
    matches!(
        verb,
        // Resource commands (normative)
        "services" | "offerings" | "stones" | "adoption" | "templates" |
        "ceremonies" | "console" | "context" | "pond" | "events" | "jobs" |
        // Admin/utility (normative)
        "help" | "browse-commands" |
        // Installation (normative)
        "install-service"
    )
}

/// Check if args contain zen positional keywords
fn has_zen_keywords(args: &[String]) -> bool {
    args.iter().any(|arg| {
        matches!(arg.as_str(), "on" | "at" | "from" | "quietly" | "fresh" | "until" | "somewhere")
    })
}

/// Extract positional keywords from args
fn extract_keywords(args: &[String], style: &CommandStyle) -> Result<(ParsedKeywords, Vec<String>)> {
    let mut keywords = ParsedKeywords::default();
    let mut filtered_args = Vec::new();
    let mut i = 0;

    while i < args.len() {
        let arg = &args[i];

        match arg.as_str() {
            // "on" is the preferred keyword, "at" is legacy alias
            "on" | "at" if *style == CommandStyle::Zen => {
                // Next arg is stone name
                i += 1;
                if i >= args.len() {
                    return Err(anyhow!("'{}' keyword requires stone name", arg));
                }
                keywords.on_stone = Some(args[i].clone());
            }
            // "from" for borrow command
            "from" if *style == CommandStyle::Zen => {
                // Next arg is URL
                i += 1;
                if i >= args.len() {
                    return Err(anyhow!("'from' keyword requires URL"));
                }
                keywords.from_url = Some(args[i].clone());
            }
            "quietly" if *style == CommandStyle::Zen => {
                keywords.quietly = true;
            }
            "somewhere" if *style == CommandStyle::Zen => {
                keywords.somewhere = true;
            }
            "fresh" if *style == CommandStyle::Zen => {
                keywords.fresh = true;
            }
            "until" if *style == CommandStyle::Zen => {
                // Next arg is condition
                i += 1;
                if i >= args.len() {
                    return Err(anyhow!("'until' keyword requires condition"));
                }
                keywords.until_condition = Some(args[i].clone());
            }
            _ => {
                // Keep non-keyword args
                filtered_args.push(arg.clone());
            }
        }

        i += 1;
    }

    Ok((keywords, filtered_args))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_zen_offer_with_on() {
        let args = vec![
            "offer".to_string(),
            "mongodb".to_string(),
            "on".to_string(),
            "stone-02".to_string(),
        ];
        let parsed = parse_args(args).unwrap();
        assert_eq!(parsed.style, CommandStyle::Zen);
        assert_eq!(parsed.verb, "offer");
        assert_eq!(parsed.args, vec!["mongodb"]);
        assert_eq!(parsed.keywords.on_stone, Some("stone-02".to_string()));
    }

    #[test]
    fn test_zen_offer_with_at_legacy() {
        // "at" is legacy alias for "on"
        let args = vec![
            "offer".to_string(),
            "mongodb".to_string(),
            "at".to_string(),
            "stone-02".to_string(),
        ];
        let parsed = parse_args(args).unwrap();
        assert_eq!(parsed.keywords.on_stone, Some("stone-02".to_string()));
    }

    #[test]
    fn test_zen_borrow_with_from() {
        let args = vec![
            "borrow".to_string(),
            "redis".to_string(),
            "from".to_string(),
            "redis://cache:6379".to_string(),
        ];
        let parsed = parse_args(args).unwrap();
        assert_eq!(parsed.style, CommandStyle::Zen);
        assert_eq!(parsed.verb, "borrow");
        assert_eq!(parsed.args, vec!["redis"]);
        assert_eq!(parsed.keywords.from_url, Some("redis://cache:6379".to_string()));
    }

    #[test]
    fn test_zen_quietly() {
        let args = vec![
            "observe".to_string(),
            "all".to_string(),
            "quietly".to_string(),
        ];
        let parsed = parse_args(args).unwrap();
        assert!(parsed.keywords.quietly);
        assert_eq!(parsed.args, vec!["all"]);
    }

    #[test]
    fn test_zen_until() {
        let args = vec![
            "watch".to_string(),
            "mongodb".to_string(),
            "until".to_string(),
            "ready".to_string(),
        ];
        let parsed = parse_args(args).unwrap();
        assert_eq!(parsed.keywords.until_condition, Some("ready".to_string()));
    }

    #[test]
    fn test_normative_services() {
        let args = vec![
            "services".to_string(),
            "list".to_string(),
        ];
        let parsed = parse_args(args).unwrap();
        assert_eq!(parsed.style, CommandStyle::Normative);
    }

    #[test]
    fn test_zen_list() {
        // "list" should now be a zen verb
        let args = vec![
            "list".to_string(),
        ];
        let parsed = parse_args(args).unwrap();
        assert_eq!(parsed.style, CommandStyle::Zen);
    }

    #[test]
    fn test_zen_uproot() {
        let args = vec![
            "uproot".to_string(),
            "mongodb".to_string(),
        ];
        let parsed = parse_args(args).unwrap();
        assert_eq!(parsed.style, CommandStyle::Zen);
        assert_eq!(parsed.verb, "uproot");
    }

    #[test]
    fn test_zen_adopt() {
        let args = vec![
            "adopt".to_string(),
            "my-container".to_string(),
        ];
        let parsed = parse_args(args).unwrap();
        assert_eq!(parsed.style, CommandStyle::Zen);
        assert_eq!(parsed.verb, "adopt");
    }

    #[test]
    fn test_mixing_rejected() {
        let args = vec![
            "services".to_string(),
            "list".to_string(),
            "quietly".to_string(),
        ];
        let result = parse_args(args);
        assert!(result.is_err());
    }

    #[test]
    fn test_zen_nourish() {
        let args = vec![
            "nourish".to_string(),
            "mongodb".to_string(),
        ];
        let parsed = parse_args(args).unwrap();
        assert_eq!(parsed.style, CommandStyle::Zen);
        assert_eq!(parsed.verb, "nourish");
        assert_eq!(parsed.args, vec!["mongodb"]);
    }

    #[test]
    fn test_zen_touch_alias() {
        let args = vec![
            "touch".to_string(),
        ];
        let parsed = parse_args(args).unwrap();
        assert_eq!(parsed.style, CommandStyle::Zen);
        assert_eq!(parsed.verb, "touch");
    }

    #[test]
    fn test_zen_garden_alias() {
        let args = vec![
            "garden".to_string(),
        ];
        let parsed = parse_args(args).unwrap();
        assert_eq!(parsed.style, CommandStyle::Zen);
        assert_eq!(parsed.verb, "garden");
    }

    #[test]
    fn test_zen_template() {
        let args = vec![
            "template".to_string(),
            "list".to_string(),
        ];
        let parsed = parse_args(args).unwrap();
        assert_eq!(parsed.style, CommandStyle::Zen);
        assert_eq!(parsed.verb, "template");
        assert_eq!(parsed.args, vec!["list"]);
    }

    #[test]
    fn test_zen_explore_alias() {
        let args = vec![
            "explore".to_string(),
        ];
        let parsed = parse_args(args).unwrap();
        assert_eq!(parsed.style, CommandStyle::Zen);
        assert_eq!(parsed.verb, "explore");
    }

    #[test]
    fn test_zen_fresh() {
        let args = vec![
            "observe".to_string(),
            "fresh".to_string(),
        ];
        let parsed = parse_args(args).unwrap();
        assert!(parsed.keywords.fresh);
        assert_eq!(parsed.args, Vec::<String>::new());
    }

    #[test]
    fn test_zen_fresh_with_target() {
        let args = vec![
            "list".to_string(),
            "fresh".to_string(),
        ];
        let parsed = parse_args(args).unwrap();
        assert!(parsed.keywords.fresh);
        assert_eq!(parsed.style, CommandStyle::Zen);
    }
}
