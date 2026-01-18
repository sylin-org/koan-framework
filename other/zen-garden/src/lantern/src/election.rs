use anyhow::Result;
use std::net::UdpSocket;
use std::time::Duration;
use tokio::sync::RwLock;
use std::sync::Arc;

#[derive(Debug, Clone, Copy, PartialEq)]
pub enum ElectionState {
    Dormant,
    Candidate,
    Active,
}

#[allow(dead_code)]
pub struct ElectionManager {
    lantern_name: String,
    udp_port: u16,
    state: ElectionState,
    active_endpoint: Option<String>,
}

impl ElectionManager {
    pub fn new(lantern_name: String, udp_port: u16) -> Self {
        Self {
            lantern_name,
            udp_port,
            state: ElectionState::Dormant,
            active_endpoint: None,
        }
    }

    pub fn state(&self) -> ElectionState {
        self.state
    }

    pub fn active_endpoint(&self) -> Option<String> {
        self.active_endpoint.clone()
    }

    pub fn set_state(&mut self, new_state: ElectionState) {
        tracing::info!(
            lantern = %self.lantern_name,
            old = ?self.state,
            new = ?new_state,
            "Election state transition"
        );
        self.state = new_state;
    }

    pub fn calculate_election_delay(&self, announcement_id: &str) -> u64 {
        let lan_ip = local_ip_address::local_ip()
            .map(|ip| ip.to_string())
            .unwrap_or_else(|_| "127.0.0.1".to_string());

        let input = format!("{}{}{}", self.lantern_name, lan_ip, announcement_id);
        let hash = blake3::hash(input.as_bytes());
        let delay_ms = (hash.as_bytes()[0] as u64) * 10;

        tracing::debug!(
            lantern = %self.lantern_name,
            announcement_id = %announcement_id,
            delay_ms = delay_ms,
            "Calculated election delay"
        );

        delay_ms
    }
}

#[allow(dead_code)]
pub async fn run_election_loop(
    manager: Arc<RwLock<ElectionManager>>,
    lantern_name: String,
) -> Result<()> {
    let udp_port = {
        let mgr = manager.read().await;
        mgr.udp_port
    };

    let socket = UdpSocket::bind(format!("0.0.0.0:{}", udp_port))?;
    socket.set_read_timeout(Some(Duration::from_millis(500)))?;
    socket.set_nonblocking(false)?;

    tracing::info!(port = udp_port, "UDP election listener started");

    let mut buf = [0u8; 1024];
    let mut last_announcement = std::time::Instant::now();
    let mut last_active_announcement = std::time::Instant::now();

    loop {
        match socket.recv_from(&mut buf) {
            Ok((len, addr)) => {
                let msg = String::from_utf8_lossy(&buf[..len]);
                if msg.starts_with("LANTERN_ANNOUNCEMENT") {
                    tracing::debug!(?addr, msg = %msg, "Received LANTERN_ANNOUNCEMENT");
                    last_announcement = std::time::Instant::now();

                    let mut mgr = manager.write().await;
                    if mgr.state() == ElectionState::Candidate {
                        tracing::info!("Suppressed by another Lantern announcement");
                        mgr.set_state(ElectionState::Dormant);
                    }
                }
            }
            Err(ref e) if e.kind() == std::io::ErrorKind::WouldBlock || e.kind() == std::io::ErrorKind::TimedOut => {
                // Normal timeout, check election state
            }
            Err(e) => {
                tracing::debug!(error = ?e, "UDP recv error");
            }
        }

        // Check for announcement timeout (15 seconds)
        if last_announcement.elapsed() > Duration::from_secs(15) {
            let mut mgr = manager.write().await;
            if mgr.state() == ElectionState::Dormant {
                tracing::info!("No announcement for 15s, initiating election");
                mgr.set_state(ElectionState::Candidate);

                // Calculate delay
                let announcement_id = uuid::Uuid::now_v7().to_string();
                let delay_ms = mgr.calculate_election_delay(&announcement_id);

                drop(mgr);

                // Wait for election delay
                tokio::time::sleep(Duration::from_millis(delay_ms)).await;

                // Check if still candidate (not suppressed)
                let mut mgr = manager.write().await;
                if mgr.state() == ElectionState::Candidate {
                    tracing::info!("Election won, promoting to Active");
                    mgr.set_state(ElectionState::Active);
                    last_active_announcement = std::time::Instant::now();
                }
            }
        }

        // Active Lanterns announce every 10s
        let mgr = manager.read().await;
        if mgr.state() == ElectionState::Active && last_active_announcement.elapsed() > Duration::from_secs(10) {
            let msg = format!("LANTERN_ANNOUNCEMENT:{}", lantern_name);
            let broadcast_addr = format!("255.255.255.255:{}", udp_port);
            match socket.send_to(msg.as_bytes(), &broadcast_addr) {
                Ok(_) => {
                    tracing::debug!("Sent LANTERN_ANNOUNCEMENT");
                    drop(mgr);
                    last_active_announcement = std::time::Instant::now();
                    last_announcement = std::time::Instant::now();
                }
                Err(e) => {
                    tracing::warn!(error = ?e, "Failed to broadcast announcement");
                }
            }
        }

        tokio::time::sleep(Duration::from_millis(100)).await;
    }
}
