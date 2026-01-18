#[cfg(not(target_os = "windows"))]
pub fn announce_moss(stone_name: &str, port: u16) -> anyhow::Result<mdns_sd::ServiceDaemon> {
    use mdns_sd::{ServiceDaemon, ServiceInfo};

    let mdns = ServiceDaemon::new()?;

    let service_type = "_moss._tcp.local.";
    let instance_name = stone_name;
    let host_name = format!("{}.local.", stone_name);

    let service = ServiceInfo::new(
        service_type,
        instance_name,
        &host_name,
        "0.0.0.0",
        port,
        None,
    )?;

    mdns.register(service)?;
    tracing::info!(instance = %instance_name, "mDNS announcement registered");

    Ok(mdns)
}

#[cfg(target_os = "windows")]
pub fn announce_moss(_stone_name: &str, _port: u16) -> anyhow::Result<()> {
    tracing::debug!("mDNS not available on Windows, skipping");
    Ok(())
}
