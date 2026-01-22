use std::time::Duration;
use garden_common::LanternTopology;

/// Trait for stone cache operations
pub trait CachedStoneOps: Send + Sync {
	fn get(&self, stone_name: &str) -> Option<CachedStoneInfo>;
	fn insert(&self, stone_name: String, endpoint: String, capabilities: garden_common::HardwareCapabilities);
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
	
	// Normalize the requested name (remove .local suffix if present)
	let requested_name = stone_name.trim_end_matches(".local");
	
	// 1) Check cache first (90s TTL)
	if let Some(cache) = cache {
		if let Some(cached) = cache.get(requested_name) {
			// Verify it's still reachable
			if probe_moss_health(client, &cached.endpoint).await {
				return Ok(cached.endpoint);
			}
			// If not reachable, continue to other methods
			tracing::debug!(stone = %requested_name, "Cached endpoint unreachable, trying other methods");
		}
	}
	
	// 2) Try mDNS-style hostname: stone-01.local:7185
	let mdns_host = if stone_name.ends_with(".local") {
		stone_name.to_string()
	} else {
		format!("{}.local", stone_name)
	};
	let mdns_endpoint = format!(
		"http://{}:{}",
		mdns_host,
		garden_common::ports::MOSS_HTTP
	);
	if probe_moss_health(client, &mdns_endpoint).await {
		return Ok(mdns_endpoint);
	}

	// 3) UDP Discovery - find stone by name on the network
	let mut discovered_responses = Vec::new();
	let _stone_count = crate::discovery::discover_all_moss_stream(
		Duration::from_secs(3),
		|response, _instant| {
			discovered_responses.push(response);
		},
	);
	
	// Check each discovered stone's name and cache it
	for response in discovered_responses {
		let endpoint = response.stone_endpoint.trim_end_matches('/').to_string();
		let caps_url = format!("{}/capabilities", endpoint);
		if let Ok(resp) = client.get(&caps_url).timeout(Duration::from_secs(2)).send().await {
			if let Ok(api_response) = resp.json::<GardenApiResponse<HardwareCapabilities>>().await {
				let stone_name_from_api = &api_response.data.stone_name;
				// Cache this stone for future lookups
				if let Some(cache) = cache {
					cache.insert(stone_name_from_api.clone(), endpoint.clone(), api_response.data.clone());
				}
				
				if stone_name_from_api == requested_name {
					return Ok(endpoint);
				}
			}
		}
	}

	// 4) Lantern fallback (cross-subnet / Windows-friendly).
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
					if let Some(stone) = topology.stones.iter().find(|s| s.name == requested_name) {
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
		"Could not resolve stone name '{}' to a moss endpoint.\n\n\
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
