use std::time::Duration;
use garden_common::LanternTopology;

/// Trait for stone cache operations
pub trait CachedStoneOps: Send + Sync {
	fn get(&self, stone_name: &str) -> Option<CachedStoneInfo>;
	/// Insert a stone into cache using stone_id (when available) or stone_name as key
	fn insert(&self, endpoint: String, capabilities: garden_common::HardwareCapabilities);
}

#[derive(Clone)]
pub struct CachedStoneInfo {
	pub endpoint: String,
}

/// Resolve a user-supplied stone target into a moss HTTP endpoint.
///
/// Accepted forms:
/// - Full URL: `http://<host>:7185` / `https://...`
/// - Host-ish: `<host>:7185`, `<host>.local`, `<ip>:7185`
/// - Stone name: `stone-01` (resolved via `.local` probe, then Lantern fallback)
pub async fn resolve_target_endpoint(client: &reqwest::Client, target: &str, cache: Option<&dyn CachedStoneOps>) -> anyhow::Result<String> {
	let trimmed = target.trim().trim_end_matches('/');
	if trimmed.is_empty() {
		return Err(anyhow::anyhow!("target value cannot be empty"));
	}

	// If already a URL with a scheme, accept as-is.
	if trimmed.starts_with("http://") || trimmed.starts_with("https://") {
		return Ok(trimmed.to_string());
	}

	// If it's host:port or hostname.local:port, normalize to http://...
	if trimmed.contains(':') {
		return Ok(format!("http://{}", trimmed));
	}

	// If it's a host/IP without a port, default to moss's HTTP port.
	// Examples: "10.0.0.5" -> http://10.0.0.5:7185, "stone-01.local" -> http://stone-01.local:7185
	if trimmed.contains('.') {
		return Ok(format!(
			"http://{}:{}",
			trimmed,
			garden_common::ports::MOSS_HTTP
		));
	}

	// Otherwise treat as a bare stone name.
	resolve_stone_name_to_endpoint(client, trimmed, cache).await
}

async fn resolve_stone_name_to_endpoint(client: &reqwest::Client, stone_name: &str, cache: Option<&dyn CachedStoneOps>) -> anyhow::Result<String> {
	use garden_common::{GardenApiResponse, HardwareCapabilities};

	// Normalize the requested identifier (remove .local suffix if present)
	// This could be a stone_name OR a stone_id
	let requested = stone_name.trim_end_matches(".local");
	let requested_lower = requested.to_lowercase();

	// 1) Check cache first - try both name and id (case-insensitive)
	if let Some(cache) = cache {
		// Try exact match first, then lowercase
		if let Some(cached) = cache.get(requested).or_else(|| cache.get(&requested_lower)) {
			if probe_moss_health(client, &cached.endpoint).await {
				return Ok(cached.endpoint);
			}
			tracing::debug!(stone = %requested, "Cached endpoint unreachable, trying other methods");
		}
	}

	// 2) Try mDNS-style hostname: stone-01.local:7185 (use lowercase for mDNS)
	let mdns_host = format!("{}.local", requested_lower);
	let mdns_endpoint = format!(
		"http://{}:{}",
		mdns_host,
		garden_common::ports::MOSS_HTTP
	);
	if probe_moss_health(client, &mdns_endpoint).await {
		return Ok(mdns_endpoint);
	}

	// 3) UDP Discovery - find stone by name OR id on the network (case-insensitive)
	let mut discovered_responses = Vec::new();
	let _stone_count = crate::discovery::discover_all_moss_stream(
		Duration::from_secs(3),
		|response, _instant| {
			discovered_responses.push(response);
		},
	);

	// Check each discovered stone's name AND id (case-insensitive)
	for response in discovered_responses {
		let endpoint = response.stone_endpoint.trim_end_matches('/').to_string();
		let caps_url = format!("{}/capabilities", endpoint);
		if let Ok(resp) = client.get(&caps_url).timeout(Duration::from_secs(2)).send().await {
			if let Ok(api_response) = resp.json::<GardenApiResponse<HardwareCapabilities>>().await {
				let caps = &api_response.data;

				// Cache this stone for future lookups
				if let Some(cache) = cache {
					cache.insert(endpoint.clone(), caps.clone());
				}

				// Match by name (case-insensitive)
				if caps.stone_name.eq_ignore_ascii_case(requested) {
					return Ok(endpoint);
				}

				// Match by stone_id (case-insensitive)
				if let Some(ref stone_id) = caps.stone_id {
					if stone_id.eq_ignore_ascii_case(requested) {
						return Ok(endpoint);
					}
				}
			}
		}
	}

	// 4) Lantern fallback (cross-subnet / Windows-friendly)
	crate::discovery::discover_lantern_background();
	if let Some(lantern) = crate::discovery::get_cached_lantern() {
		let url = format!("{}/api/v1/stones", lantern.trim_end_matches('/'));
		match client
			.get(&url)
			.timeout(Duration::from_secs(3))
			.send()
			.await
		{
			Ok(resp) if resp.status().is_success() => {
				if let Ok(topology) = resp.json::<LanternTopology>().await {
					// Match by name or stone_id (case-insensitive)
					if let Some(stone) = topology.stones.iter().find(|s| {
						s.name.eq_ignore_ascii_case(requested) ||
						s.stone_id.as_ref().map(|id| id.eq_ignore_ascii_case(requested)).unwrap_or(false)
					}) {
						return Ok(stone.endpoint.trim_end_matches('/').to_string());
					}
				}
			}
			Ok(resp) => {
				tracing::warn!(status = ?resp.status(), "Lantern returned non-success for /api/v1/stones");
			}
			Err(e) => {
				tracing::warn!(error = ?e, "Failed to query Lantern for stone name resolution");
			}
		}
	}

	Err(anyhow::anyhow!(
		"Could not resolve '{}' to a moss endpoint.\n\n\
		Try one of:\n\
		  • garden-rake tend auto (auto-discover)\n\
		  • garden-rake observe (to see discovered endpoints)\n\
		  • garden-rake tend http://<ip>:7185",
		stone_name
	))
}

async fn probe_moss_health(client: &reqwest::Client, endpoint: &str) -> bool {
	let url = format!("{}/health", endpoint.trim_end_matches('/'));
	match client
		.get(&url)
		.timeout(Duration::from_millis(800))
		.send()
		.await
	{
		Ok(resp) => resp.status().is_success(),
		Err(_) => false,
	}
}
