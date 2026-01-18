use std::time::Duration;

use garden_common::LanternTopology;

/// Resolve a user-supplied stone target into a moss HTTP endpoint.
///
/// Accepted forms:
/// - Full URL: `http://<host>:7185` / `https://...`
/// - Host-ish: `<host>:7185`, `<host>.local`, `<ip>:7185`
/// - Stone name: `stone-01` (resolved via `.local` probe, then Lantern fallback)
pub async fn resolve_target_endpoint(client: &reqwest::Client, target: &str) -> anyhow::Result<String> {
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
	resolve_stone_name_to_endpoint(client, trimmed).await
}

async fn resolve_stone_name_to_endpoint(client: &reqwest::Client, stone_name: &str) -> anyhow::Result<String> {
	// 1) Try mDNS-style hostname first: stone-01.local:7185
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

	// 2) Lantern fallback (cross-subnet / Windows-friendly).
	if let Some(lantern) = crate::discovery::discover_lantern() {
		let url = format!("{}/api/stones", lantern.trim_end_matches('/'));
		match client
			.get(&url)
			.timeout(Duration::from_secs(3))
			.send()
			.await
		{
			Ok(resp) if resp.status().is_success() => {
				if let Ok(topology) = resp.json::<LanternTopology>().await {
					let requested = stone_name.trim_end_matches(".local");
					if let Some(stone) = topology.stones.iter().find(|s| s.name == requested) {
						return Ok(stone.endpoint.trim_end_matches('/').to_string());
					}
				}
			}
			Ok(resp) => {
				tracing::warn!(status = ?resp.status(), "Lantern returned non-success for /api/stones");
			}
			Err(e) => {
				tracing::warn!(error = ?e, "Failed to query Lantern for stone name resolution");
			}
		}
	}

	Err(anyhow::anyhow!(
		"Could not resolve stone name '{}' to a moss endpoint.\n\n\
		Try one of:\n\
		  • --at http://<ip>:7185\n\
		  • --at {}:7185\n\
		  • garden-rake observe (to see discovered endpoints)",
		stone_name,
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
