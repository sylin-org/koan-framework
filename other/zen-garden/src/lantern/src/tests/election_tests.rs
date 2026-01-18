use crate::{ElectionManager, ElectionState};

#[test]
fn test_election_manager_initial_state() {
    let manager = ElectionManager::new("test-lantern".to_string(), 7187);
    assert_eq!(manager.state(), ElectionState::Dormant);
    assert_eq!(manager.active_endpoint(), None);
}

#[test]
fn test_election_manager_state_transition() {
    let mut manager = ElectionManager::new("test-lantern".to_string(), 7187);

    // Dormant -> Candidate
    manager.set_state(ElectionState::Candidate);
    assert_eq!(manager.state(), ElectionState::Candidate);

    // Candidate -> Active
    manager.set_state(ElectionState::Active);
    assert_eq!(manager.state(), ElectionState::Active);

    // Active -> Dormant (suppressed by another)
    manager.set_state(ElectionState::Dormant);
    assert_eq!(manager.state(), ElectionState::Dormant);
}

#[test]
fn test_election_delay_calculation() {
    let manager = ElectionManager::new("test-lantern-1".to_string(), 7187);
    let announcement_id = "test-announcement-id";

    let delay1 = manager.calculate_election_delay(announcement_id);

    // Delay should be between 0 and 2550ms (255 * 10)
    assert!(delay1 <= 2550);

    // Same inputs should produce same delay (deterministic)
    let delay2 = manager.calculate_election_delay(announcement_id);
    assert_eq!(delay1, delay2);
}

#[test]
fn test_election_delay_varies_by_lantern_name() {
    let manager1 = ElectionManager::new("lantern-1".to_string(), 7187);
    let manager2 = ElectionManager::new("lantern-2".to_string(), 7187);
    let announcement_id = "same-announcement";

    let delay1 = manager1.calculate_election_delay(announcement_id);
    let delay2 = manager2.calculate_election_delay(announcement_id);

    // Different lantern names should produce different delays
    // (with high probability - BLAKE3 hash collision is extremely unlikely)
    assert_ne!(delay1, delay2);
}

#[test]
fn test_election_delay_varies_by_announcement_id() {
    let manager = ElectionManager::new("test-lantern".to_string(), 7187);

    let delay1 = manager.calculate_election_delay("announcement-1");
    let delay2 = manager.calculate_election_delay("announcement-2");

    // Different announcement IDs should produce different delays
    assert_ne!(delay1, delay2);
}

#[test]
fn test_election_delay_distribution() {
    let manager = ElectionManager::new("test-lantern".to_string(), 7187);
    let mut delays = Vec::new();

    // Generate 100 delays with different announcement IDs
    for i in 0..100 {
        let delay = manager.calculate_election_delay(&format!("announcement-{}", i));
        delays.push(delay);
    }

    // Check that we have a reasonable distribution
    // All delays should be within 0-2550ms range
    assert!(delays.iter().all(|&d| d <= 2550));

    // We should have variety (not all the same)
    let min = delays.iter().min().unwrap();
    let max = delays.iter().max().unwrap();
    assert!(max - min > 100, "Delays should have reasonable spread");

    // Average should be around 1275ms (midpoint of 0-2550)
    let avg: u64 = delays.iter().sum::<u64>() / delays.len() as u64;
    assert!(
        avg > 800 && avg < 1700,
        "Average delay {} should be reasonably centered",
        avg
    );
}
