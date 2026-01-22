use super::*;
use crate::commands::offering::{
    TaxonomyDictionary, OfferingEntry, OfferingCompatibility,
    normalize_tokens, token_matches_category, offering_relevance_score, stone_prefer_score
};

fn dict(pairs: &[(&str, &str)]) -> TaxonomyDictionary {
    let mut map = std::collections::HashMap::new();
    for (k, v) in pairs {
        map.insert((*k).to_string(), (*v).to_string());
    }
    TaxonomyDictionary { map }
}

fn caps_with_disk_type(disk_type: &str) -> HardwareCapabilities {
    HardwareCapabilities {
        stone_id: None,
        stone_name: "stone-01".to_string(),
        hardware: garden_common::HardwareInventory {
            cpu: garden_common::CpuCapabilities {
                model: None,
                cores: 4,
                threads: None,
                architecture: "x86_64".to_string(),
                features: None,
            },
            memory: garden_common::MemoryCapabilities { total_mb: 8 * 1024 },
            gpus: vec![],
            disk: Some(garden_common::DiskCapabilities {
                total_gb: 128,
                disk_type: Some(disk_type.to_string()),
            }),
            storage: vec![],
            os_version: None,
            kernel_version: None,
            swap_mb: None,
            ai_capabilities: None,
        },
        runtime: None,
        detection_status: garden_common::DetectionStatus::Complete,
    }
}

fn offering(
    name: &str,
    category: &str,
    description: &str,
    tags: &[&str],
) -> OfferingEntry {
    OfferingEntry {
        name: name.to_string(),
        category: category.to_string(),
        description: description.to_string(),
        tags: tags.iter().map(|t| (*t).to_string()).collect(),
        image: "repo/image:latest".to_string(),
        compatibility: OfferingCompatibility {
            decision: "pass".to_string(),
            reason: None,
            original_image: None,
            fallback_image: None,
            suggestion: None,
        },
    }
}

#[test]
fn normalize_tokens_splits_lowercases_and_maps() {
    let d = dict(&[("db", "database"), ("doc", "document")]);

    let out = normalize_tokens("DB, doc\n", &d);
    assert_eq!(out, vec!["database".to_string(), "document".to_string()]);

    let out2 = normalize_tokens("NoSQL", &d);
    assert_eq!(out2, vec!["nosql".to_string()]);
}

#[test]
fn token_matches_category_database_is_broad() {
    assert!(token_matches_category("database", "data"));
    assert!(token_matches_category("database", "cache"));
    assert!(token_matches_category("database", "search"));
    assert!(token_matches_category("database", "vector"));

    assert!(!token_matches_category("database", "messaging"));
}

#[test]
fn offering_relevance_score_prefers_category_and_tags() {
    let o = offering(
        "mongodb",
        "data",
        "Document database",
        &["database", "document", "nosql"],
    );

    let tokens = vec!["database".to_string(), "document".to_string()];
    let score = offering_relevance_score(&tokens, &o);

    // database => category(+10) + tag(+6) + desc(+1)
    // document  => tag(+6) + desc(+1)
    assert_eq!(score, 24);
}

#[test]
fn stone_prefer_score_biases_nvme_and_ssd() {
    let caps = caps_with_disk_type("NVMe");

    assert_eq!(stone_prefer_score(&["ssd".to_string()], Some(&caps)), 10);
    assert_eq!(stone_prefer_score(&["nvme".to_string()], Some(&caps)), 12);
    assert_eq!(stone_prefer_score(&["hdd".to_string()], Some(&caps)), 0);
}

#[test]
fn clap_parses_offer_prefer_and_anywhere_on_fail() {
    let cli = Cli::parse_from([
        "garden-rake",
        "offer",
        "database,document",
        "--prefer",
        "ssd,nvme",
        "--anywhere-on-fail",
    ]);

    match cli.command {
        Some(Commands::Offer {
            offering,
            prefer,
            anywhere_on_fail,
            ..
        }) => {
            assert_eq!(offering.as_deref(), Some("database,document"));
            assert_eq!(prefer, vec!["ssd".to_string(), "nvme".to_string()]);
            assert!(anywhere_on_fail);
        }
        _ => panic!("expected Commands::Offer"),
    }
}

#[test]
fn clap_parses_offer_info_subcommand() {
    let cli = Cli::parse_from(["garden-rake", "offer", "mongodb", "info"]);

    match cli.command {
        Some(Commands::Offer {
            offering,
            action,
            ..
        }) => {
            assert_eq!(offering.as_deref(), Some("mongodb"));
            assert!(matches!(action, Some(OfferAction::Info)));
        }
        _ => panic!("expected Commands::Offer"),
    }
}
