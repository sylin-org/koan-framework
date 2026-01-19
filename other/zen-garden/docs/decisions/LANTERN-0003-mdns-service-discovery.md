# LANTERN-0003: mDNS Service Discovery with Optional Proxy

**Status**: Proposed  
**Date**: 2026-01-18  
**Deciders**: Workshop panel (Network, DX, UX, Semiotics, Container leads)

## Context

Users want frictionless access to containerized services deployed across stones. Typing `MediaX.zen-garden.local` in a browser is more intuitive than remembering `192.168.1.42:8080`. We need zero-config service discovery that works on local networks without DNS server setup.

**User Story:**
> "I deploy MediaX container to stone-02, type `MediaX.zen-garden.local` in my browser, and it just works."

**Design Goals:**
- Zero configuration for basic functionality
- Work across all stones on same LAN
- Degrade gracefully (direct IP:port always available)
- Scale to 10+ services without user confusion

## Decision

**Implement three-tier service discovery:**

### Tier 1: Direct Access (Zero Config, Always Works)
```bash
garden-rake offer MediaX --port 8080
# Output:
# ✓ MediaX deployed to stone-02
# Access: http://stone-02:8080 or http://192.168.1.42:8080
```

Users get working URL immediately. No setup required.

### Tier 2: mDNS Service Advertisement (Auto-enabled if Available)
Each stone advertises services via mDNS:
```
Service: MediaX._http._tcp.local
Host: stone-02.local
Port: 8080
TXT Records:
  garden=zen-garden
  stone=stone-02
  version=1.0.0
```

**Output enhancement:**
```bash
garden-rake offer MediaX --port 8080
# ✓ MediaX deployed to stone-02
# Access: http://stone-02.local:8080 (mDNS)
#      or http://192.168.1.42:8080 (direct IP)
```

### Tier 3: Friendly Proxy Names (Optional, Requires Cornerstone)
Cornerstone runs reverse proxy (Caddy/Traefik) that:
1. Discovers services via mDNS browsing
2. Verifies services with Moss API (prevents spoofing)
3. Exposes as: `http://MediaX.zen-garden.local`

**Architecture:**
```
Browser → MediaX.zen-garden.local
    ↓
Cornerstone Proxy (discovers via mDNS)
    ↓ (verifies with Moss API)
Stone-02:8080 (runs MediaX container)
```

**Output with proxy:**
```bash
garden-rake offer MediaX --port 8080
# ✓ MediaX deployed to stone-02
# Access: http://MediaX.zen-garden.local (friendly URL)
#      or http://stone-02.local:8080 (direct)
```

## Implementation Details

### mDNS Advertisement (Phase 1)
```rust
// In Moss daemon on each stone
use avahi_rs::ServicePublisher;

pub fn advertise_service(name: &str, port: u16) {
    let publisher = ServicePublisher::new()?;
    publisher.publish(
        name: name,
        service_type: "_http._tcp",
        port: port,
        txt_records: hashmap!{
            "garden" => "zen-garden",
            "stone" => hostname(),
        }
    )?;
}
```

### Discovery + Proxy (Phase 2)
```rust
// On Cornerstone
use avahi_rs::ServiceBrowser;

pub async fn sync_proxy_routes() {
    let browser = ServiceBrowser::new("_http._tcp.local")?;
    
    for service in browser.services() {
        // Filter by garden
        if service.txt.get("garden") != Some("zen-garden") {
            continue;
        }
        
        // Verify with Moss (prevent spoofing)
        let verified = moss_api::verify_service(&service.name).await?;
        
        if verified {
            caddy::add_route(
                hostname: format!("{}.zen-garden.local", service.name),
                backend: format!("{}:{}", service.host, service.port)
            );
        }
    }
}
```

### Fallback for Non-mDNS Clients
If mDNS unavailable (Windows without Bonjour), poll Moss API:
```rust
// Poll Lantern registry every 30s
let services = moss_api::list_services().await?;
for service in services {
    caddy::add_route(
        hostname: format!("{}.zen-garden.local", service.name),
        backend: format!("{}:{}", service.host, service.port)
    );
}
```

## Rationale

### Why mDNS?
- ✅ **Zero config**: No DNS server required
- ✅ **Standard protocol**: Same as AirPlay, Chromecast, Spotify Connect
- ✅ **Built-in OS support**: macOS, Linux, iOS (Windows needs Bonjour)
- ✅ **Self-healing**: Services update automatically when moved

### Why Proxy Layer?
- ✅ **Friendly names**: `MediaX.zen-garden.local` vs `stone-02.local:8080`
- ✅ **Consistent pattern**: All services at `*.zen-garden.local`
- ✅ **Security verification**: Prevents rogue services from advertising
- ✅ **Optional**: Works without proxy, direct access always available

### Why Three Tiers?
**Progressive enhancement philosophy:**
- New users get working URLs immediately (Tier 1)
- mDNS enables on supported systems automatically (Tier 2)
- Power users configure proxy for best experience (Tier 3)

No user is blocked. Each tier adds convenience without breaking previous.

## Alternatives Considered

### Option A: Custom Protocol Handler (`zen-garden://MediaX`)
**Pros:** Clean syntax, direct addressing  
**Cons:** Requires browser extension install, non-standard

**Verdict:** Offer as optional Phase 3 for power users

### Option B: Wildcard DNS (`*.zen-garden` → Cornerstone)
**Pros:** Most flexible, works remotely  
**Cons:** Requires DNS control (router config or domain ownership)

**Verdict:** Document as advanced setup, not default

### Option C: Hosts File Automation
**Pros:** Works everywhere  
**Cons:** Requires root, brittle (doesn't update on service moves)

**Verdict:** Rejected, too much ceremony

## Consequences

**Positive:**
- Zero-config service access on most OSes
- Familiar pattern (mDNS is widely deployed)
- Graceful degradation (always have IP:port fallback)
- No vendor lock-in (standard protocols)

**Negative:**
- mDNS doesn't cross subnet boundaries (link-local only)
- Windows users need Bonjour installed (or use direct IP)
- Proxy adds single point of failure (mitigated: direct access remains)

**Neutral:**
- Adds Avahi/Bonjour as system dependency (commonly pre-installed)
- Cornerstone becomes recommended (but not required)

## Success Metrics

- **Adoption**: 70%+ of services accessed via `.local` names (vs IP:port)
- **Support load**: <5% tickets about "can't find service" after mDNS enabled
- **Setup time**: Service accessible within 5 seconds of `garden-rake offer`

## References

- mDNS/DNS-SD RFC 6763: https://datatracker.ietf.org/doc/html/rfc6763
- Avahi documentation: https://avahi.org/
- XKCD on mDNS: "It just works (on local networks)"

## Related Decisions

- LANTERN-0001: Service Registry Architecture
- SECURITY-0002: Keystone rename (security artifact vs device type)

## Implementation Checklist

**Phase 1: mDNS Advertisement (MVP)**
- [ ] Integrate `avahi-rs` or `mdns` crate in Moss
- [ ] Advertise services on `garden-rake offer`
- [ ] Update CLI output to show `.local` URLs when mDNS available
- [ ] Document mDNS setup (Avahi on Linux, pre-installed macOS)

**Phase 2: Cornerstone Proxy (Enhanced)**
- [ ] Implement Caddy/Traefik dynamic config
- [ ] mDNS browsing + Moss API verification
- [ ] Auto-configure `*.zen-garden.local` routes
- [ ] Document proxy setup in guides

**Phase 3: Power User Features (Optional)**
- [ ] Browser extension for `zen-garden://` protocol
- [ ] Remote access via Tailscale MagicDNS integration
- [ ] Metrics dashboard showing service health

## Notes

**Workshop consensus:** Ship Tier 1 (direct IP) immediately, Tier 2 (mDNS) in next release, Tier 3 (proxy) as opt-in feature. Validate with real users before over-engineering.

**Key insight (Prof. Chen, Semantics):** "The fact that mDNS uses multicast means there's no single point of failure. If the Cornerstone goes down, users can still access `stone-02.local:8080` directly. The friendly name is a convenience, not a dependency. That's very Zen. The system degrades gracefully."
