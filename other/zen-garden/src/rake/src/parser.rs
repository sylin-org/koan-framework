/// Zen parser for positional keyword extraction
/// 
/// Parses command-line arguments to detect zen syntax and extract positional keywords
/// before they reach Clap. Supports: `at <stone>`, `quietly`, `until <condition>`

use anyhow::{Result, anyhow};

#[derive(Debug, Clone, PartialEq)]
pub enum CommandStyle {
    Zen,
    Normative,
}

#[derive(Debug, Clone, Default)]
pub struct ParsedKeywords {
    pub at_stone: Option<String>,
    pub quietly: bool,
    pub until_condition: Option<String>,
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
        "offer" | "rest" | "wake" | "nourish" | "release" |
        "observe" | "watch" | "touch" | "tend" | "place" |
        "lift" | "invite" | "explore" | "garden" | "make"
    )
}

/// Check if a verb is a normative verb
fn is_normative_verb(verb: &str) -> bool {
    matches!(
        verb,
        "services" | "status" | "logs" | "inspect" | "list" | "remove" | "upgrade" | 
        "refresh" | "reconcile" | "template" | "help" |
        "pond" | "context" | "topology"
    )
}

/// Check if args contain zen positional keywords
fn has_zen_keywords(args: &[String]) -> bool {
    args.iter().any(|arg| {
        matches!(arg.as_str(), "at" | "quietly" | "until")
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
            "at" if *style == CommandStyle::Zen => {
                // Next arg is stone name
                i += 1;
                if i >= args.len() {
                    return Err(anyhow!("'at' keyword requires stone name"));
                }
                keywords.at_stone = Some(args[i].clone());
            }
            "quietly" if *style == CommandStyle::Zen => {
                keywords.quietly = true;
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
    fn test_zen_offer_with_at() {
        let args = vec![
            "offer".to_string(),
            "mongodb".to_string(),
            "at".to_string(),
            "stone-02".to_string(),
        ];
        let parsed = parse_args(args).unwrap();
        assert_eq!(parsed.style, CommandStyle::Zen);
        assert_eq!(parsed.verb, "offer");
        assert_eq!(parsed.args, vec!["mongodb"]);
        assert_eq!(parsed.keywords.at_stone, Some("stone-02".to_string()));
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
    fn test_mixing_rejected() {
        let args = vec![
            "services".to_string(),
            "list".to_string(),
            "quietly".to_string(),
        ];
        let result = parse_args(args);
        assert!(result.is_err());
    }
}
