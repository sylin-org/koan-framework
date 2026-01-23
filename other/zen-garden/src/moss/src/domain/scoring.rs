//! Resource scoring algorithms for placement recommendations
//!
//! Pure functions for scoring stones based on resource availability and characteristics.
//! All functions are deterministic and side-effect free for easy testing.

use garden_common::DiskType;

/// Score memory headroom (0-20 points)
///
/// Linear scale based on percentage of free memory.
/// More free memory = better score.
///
/// # Examples
/// ```ignore
/// assert_eq!(score_memory_headroom(16384, 32768), 10); // 50% free
/// assert_eq!(score_memory_headroom(24576, 32768), 15); // 75% free
/// assert_eq!(score_memory_headroom(0, 32768), 0);      // 0% free
/// ```
pub fn score_memory_headroom(free_mb: u64, total_mb: u64) -> i32 {
    if total_mb == 0 {
        return 0;
    }
    
    let percent_free = (free_mb as f64 / total_mb as f64) * 100.0;
    let score = (20.0 * (percent_free / 100.0)).round() as i32;
    score.clamp(0, 20)
}

/// Score CPU availability (0-20 points)
///
/// Inverse scale based on CPU load percentage.
/// Lower load = better score.
///
/// # Examples
/// ```ignore
/// assert_eq!(score_cpu_availability(0), 20);   // 0% load
/// assert_eq!(score_cpu_availability(50), 10);  // 50% load
/// assert_eq!(score_cpu_availability(100), 0);  // 100% load
/// ```
pub fn score_cpu_availability(load_percent: u8) -> i32 {
    let load = load_percent.min(100) as i32;
    20 - (load / 5)
}

/// Score storage capacity (0-15 points)
///
/// Tiered scoring based on available storage.
/// More free storage = better score.
///
/// # Tiers
/// - <50 GB: 0 points
/// - 50-99 GB: 5 points
/// - 100-199 GB: 10 points
/// - 200+ GB: 15 points
///
/// # Examples
/// ```ignore
/// assert_eq!(score_storage_capacity(25), 0);   // <50 GB
/// assert_eq!(score_storage_capacity(75), 5);   // 50-99 GB
/// assert_eq!(score_storage_capacity(150), 10); // 100-199 GB
/// assert_eq!(score_storage_capacity(500), 15); // 200+ GB
/// ```
pub fn score_storage_capacity(free_gb: u64) -> i32 {
    match free_gb {
        0..=49 => 0,
        50..=99 => 5,
        100..=199 => 10,
        _ => 15,
    }
}

/// Score storage hardware type (0-12 points)
///
/// Scores based on storage performance characteristics.
/// Faster storage = better score.
///
/// # Storage Types
/// - NVMe: 12 points (fastest)
/// - SSD: 10 points (fast)
/// - HDD: 5 points (traditional)
/// - Unknown: 0 points (undetected)
///
/// # Examples
/// ```ignore
/// assert_eq!(score_storage_type(&DiskType::NVMe), 12);
/// assert_eq!(score_storage_type(&DiskType::SSD), 10);
/// assert_eq!(score_storage_type(&DiskType::HDD), 5);
/// assert_eq!(score_storage_type(&DiskType::Unknown), 0);
/// ```
pub fn score_storage_type(storage_type: &DiskType) -> i32 {
    match storage_type {
        DiskType::NVMe => 12,
        DiskType::SSD => 10,
        DiskType::HDD => 5,
        DiskType::Unknown => 0,
    }
}

/// Calculate service distribution penalty (-N points)
///
/// Encourages spreading services across stones.
/// More services = larger penalty.
///
/// Penalty: -3 points per existing service
///
/// # Examples
/// ```ignore
/// assert_eq!(calculate_distribution_penalty(0), 0);   // No services
/// assert_eq!(calculate_distribution_penalty(3), -9);  // 3 services
/// assert_eq!(calculate_distribution_penalty(5), -15); // 5 services
/// ```
pub fn calculate_distribution_penalty(service_count: usize) -> i32 {
    -(service_count as i32 * 3)
}

/// Calculate compatibility penalty/filter
///
/// Returns score penalty based on compatibility decision.
///
/// # Scores
/// - Compatible: 0 points (no penalty)
/// - Fallback: -15 points (emulation penalty)
/// - Incompatible: -999 points (effectively filtered)
///
/// # Examples
/// ```ignore
/// assert_eq!(calculate_compatibility_penalty(&CompatibilityDecision::Pass), 0);
/// // Warning returns -50 (proceed with caution)
/// // Fallback returns -15
/// // Fail returns -999
/// ```
pub fn calculate_compatibility_penalty(decision: &crate::domain::compatibility::CompatibilityDecision) -> i32 {
    match decision {
        crate::domain::compatibility::CompatibilityDecision::Pass => 0,
        crate::domain::compatibility::CompatibilityDecision::Warning { .. } => -50, // Significant penalty but still viable
        crate::domain::compatibility::CompatibilityDecision::Fallback { .. } => -15,
        crate::domain::compatibility::CompatibilityDecision::Fail { .. } => -999,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_memory_scoring() {
        assert_eq!(score_memory_headroom(16384, 32768), 10); // 50% free
        assert_eq!(score_memory_headroom(24576, 32768), 15); // 75% free
        assert_eq!(score_memory_headroom(32768, 32768), 20); // 100% free
        assert_eq!(score_memory_headroom(0, 32768), 0);      // 0% free
        
        // Edge case: zero total
        assert_eq!(score_memory_headroom(1000, 0), 0);
    }

    #[test]
    fn test_cpu_scoring() {
        assert_eq!(score_cpu_availability(0), 20);   // 0% load
        assert_eq!(score_cpu_availability(10), 18);  // 10% load
        assert_eq!(score_cpu_availability(25), 15);  // 25% load
        assert_eq!(score_cpu_availability(50), 10);  // 50% load
        assert_eq!(score_cpu_availability(75), 5);   // 75% load
        assert_eq!(score_cpu_availability(100), 0);  // 100% load
        
        // Edge case: over 100%
        assert_eq!(score_cpu_availability(150), 0);
    }

    #[test]
    fn test_storage_capacity_scoring() {
        assert_eq!(score_storage_capacity(0), 0);    // Empty
        assert_eq!(score_storage_capacity(25), 0);   // <50 GB
        assert_eq!(score_storage_capacity(49), 0);   // Just under 50
        assert_eq!(score_storage_capacity(50), 5);   // 50 GB
        assert_eq!(score_storage_capacity(75), 5);   // 50-99 GB
        assert_eq!(score_storage_capacity(99), 5);   // Just under 100
        assert_eq!(score_storage_capacity(100), 10); // 100 GB
        assert_eq!(score_storage_capacity(150), 10); // 100-199 GB
        assert_eq!(score_storage_capacity(199), 10); // Just under 200
        assert_eq!(score_storage_capacity(200), 15); // 200 GB
        assert_eq!(score_storage_capacity(500), 15); // 200+ GB
        assert_eq!(score_storage_capacity(1000), 15); // Large capacity
    }

    #[test]
    fn test_storage_type_scoring() {
        assert_eq!(score_storage_type(&DiskType::NVMe), 12);
        assert_eq!(score_storage_type(&DiskType::SSD), 10);
        assert_eq!(score_storage_type(&DiskType::HDD), 5);
        assert_eq!(score_storage_type(&DiskType::Unknown), 0);
    }

    #[test]
    fn test_distribution_penalty() {
        assert_eq!(calculate_distribution_penalty(0), 0);   // No services
        assert_eq!(calculate_distribution_penalty(1), -3);  // 1 service
        assert_eq!(calculate_distribution_penalty(3), -9);  // 3 services
        assert_eq!(calculate_distribution_penalty(5), -15); // 5 services
        assert_eq!(calculate_distribution_penalty(10), -30); // Heavy load
    }

    #[test]
    fn test_compatibility_penalty() {
        use crate::domain::compatibility::CompatibilityDecision;
        
        assert_eq!(calculate_compatibility_penalty(&CompatibilityDecision::Pass), 0);
        assert_eq!(
            calculate_compatibility_penalty(&CompatibilityDecision::Fallback {
                image: "fallback".to_string(),
                reason: "test".to_string()
            }),
            -15
        );
        assert_eq!(
            calculate_compatibility_penalty(&CompatibilityDecision::Fail {
                reason: "incompatible".to_string(),
                suggestion: None
            }),
            -999
        );
    }

    #[test]
    fn test_combined_scoring_example() {
        // Simulate a well-resourced stone
        let memory = score_memory_headroom(24576, 32768); // 75% free = 15
        let cpu = score_cpu_availability(12); // 12% load = 18
        let storage_cap = score_storage_capacity(450); // 450 GB = 15
        let storage_hw = score_storage_type(&DiskType::NVMe); // 12
        let distribution = calculate_distribution_penalty(3); // -9
        let tended_bonus = 3;
        
        let total = memory + cpu + storage_cap + storage_hw + distribution + tended_bonus;
        assert_eq!(total, 54); // Strong candidate
    }
}
