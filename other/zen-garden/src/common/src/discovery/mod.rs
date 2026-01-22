//! Centralized stone discovery logic
//!
//! Priority chain for resolving stone endpoints:
//! 1. Explicit --at stone-name (passed as parameter)
//! 2. GARDEN_STONE environment variable
//! 3. Tended stone from config.json
//! 4. UDP broadcast discovery (first responder)

pub mod resolver;
pub mod udp;

pub use resolver::DiscoveryResolver;
pub use udp::UdpDiscovery;
