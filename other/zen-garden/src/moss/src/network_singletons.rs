/// Centralized network service singleton helpers
/// 
/// This module provides reusable patterns for network services that need:
/// - Single listener instance per port (prevents "address already in use" errors)
/// - SO_REUSEADDR socket option for Windows compatibility
/// - Async tokio socket operations
/// 
/// ## Usage Example
/// 
/// ```rust
/// // Start singleton UDP listener with broadcast channel
/// let receiver = discovery::ensure_udp_listener(
///     "stone-name".to_string(),
///     "http://localhost:7185".to_string(),
/// ).await?;
/// 
/// // Subscribe to discovery events
/// let mut rx = receiver;
/// tokio::spawn(async move {
///     while let Ok(event) = rx.recv().await {
///         // Process discovery events
///     }
/// });
/// ```

use anyhow::Result;
use socket2::{Domain, Protocol, Socket, Type};
use tokio::net::UdpSocket;

/// Create UDP socket with SO_REUSEADDR for port reuse
/// 
/// This prevents "Only one usage of each socket address..." errors on Windows
/// when restarting services that bind to the same port. The singleton pattern
/// ensures only one listener binds to the port, so SO_REUSEADDR is sufficient
/// for our use case across all platforms.
/// 
/// ## Arguments
/// - `addr`: Socket address (e.g., "0.0.0.0:7186")
pub async fn create_reusable_udp_socket(addr: &str) -> Result<UdpSocket> {
    let socket_addr: std::net::SocketAddr = addr.parse()?;
    let domain = if socket_addr.is_ipv4() {
        Domain::IPV4
    } else {
        Domain::IPV6
    };
    
    let socket = Socket::new(domain, Type::DGRAM, Some(Protocol::UDP))?;
    
    // Enable SO_REUSEADDR for port reuse
    // This prevents "address already in use" errors on Windows when restarting
    socket.set_reuse_address(true)?;
    
    // Windows: Disable WSAECONNRESET (error 10054) from ICMP port unreachable
    // MUST be called before bind() and on the raw socket2 Socket
    #[cfg(windows)]
    {
        use std::os::windows::io::AsRawSocket;
        const SIO_UDP_CONNRESET: u32 = 0x9800000C;
        let mut bytes_returned: u32 = 0;
        let enable: u32 = 0; // Disable the behavior
        unsafe {
            let sock = socket.as_raw_socket() as usize;
            let result = windows_sys::Win32::Networking::WinSock::WSAIoctl(
                sock,
                SIO_UDP_CONNRESET,
                &enable as *const _ as *const _,
                std::mem::size_of::<u32>() as u32,
                std::ptr::null_mut(),
                0,
                &mut bytes_returned as *mut _,
                std::ptr::null_mut(),
                None,
            );
            if result != 0 {
                let error = std::io::Error::last_os_error();
                tracing::error!(?error, "Failed to disable SIO_UDP_CONNRESET - UDP may be unstable");
            } else {
                tracing::debug!("SIO_UDP_CONNRESET disabled successfully");
            }
        }
    }
    
    socket.bind(&socket_addr.into())?;
    socket.set_nonblocking(true)?;
    
    // Convert to tokio UdpSocket
    let std_socket: std::net::UdpSocket = socket.into();
    Ok(UdpSocket::from_std(std_socket)?)
}

/// Create TCP listener socket with SO_REUSEADDR for port reuse
/// 
/// Similar to UDP version but for TCP sockets.
/// 
/// ## Arguments
/// - `addr`: Socket address (e.g., "0.0.0.0:7185")
#[allow(dead_code)]
pub async fn create_reusable_tcp_listener(addr: &str) -> Result<tokio::net::TcpListener> {
    let socket_addr: std::net::SocketAddr = addr.parse()?;
    let domain = if socket_addr.is_ipv4() {
        Domain::IPV4
    } else {
        Domain::IPV6
    };
    
    let socket = Socket::new(domain, Type::STREAM, Some(Protocol::TCP))?;
    
    // Enable SO_REUSEADDR for port reuse
    socket.set_reuse_address(true)?;
    
    socket.bind(&socket_addr.into())?;
    socket.listen(128)?; // backlog
    socket.set_nonblocking(true)?;
    
    // Convert to tokio TcpListener
    let std_listener: std::net::TcpListener = socket.into();
    Ok(tokio::net::TcpListener::from_std(std_listener)?)
}
