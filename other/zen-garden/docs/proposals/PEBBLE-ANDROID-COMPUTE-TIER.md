# Proposal: Android "Pebble" Compute Tier

**Status**: Proposed  
**Date**: 2026-01-17  
**Target Audience**: Makers, hobbyists, e-waste reduction enthusiasts  
**Complexity**: Experimental, community-supported

## Executive Summary

Introduce a third compute tier called "Pebbles" to enable repurposing old Android smartphones as sensor-rich edge compute nodes. Unlike Stones (general-purpose compute with Docker), Pebbles are specialized for sensor workloads leveraging unique smartphone hardware (GPS, cameras, accelerometers, etc.).

**Key differentiator**: Access to sensor suite unavailable in traditional SBC platforms.

---

## Problem Statement

1. **E-waste opportunity**: Billions of old smartphones exist globally, particularly in regions with limited access to traditional compute hardware
2. **Unique capabilities**: Smartphones contain sensors (GPS, cameras, motion, NFC) that standard SBCs lack
3. **Maker community**: Enthusiasts want unconventional compute platforms for experimental/DIY projects
4. **Power efficiency**: Smartphones designed for 3-5W operation with built-in battery backup

**Current limitation**: Zen Garden architecture assumes systemd, Docker, and standard Linux - incompatible with Android.

---

## Proposal: Pebble Architecture

### Design Principles

1. **Pebbles are NOT Stones** - Different node type with distinct capabilities and limitations
2. **Pull-based architecture** - Pebbles poll Lantern for work (no push deployment)
3. **Sensor-first workloads** - Leverage GPS, camera, accelerometer, magnetometer, etc.
4. **Disposable by design** - Assume 1-3 year lifespan due to battery degradation
5. **Experimental tier** - Community-supported, no production SLA

### Architecture Overview

```
Lantern (Registry & Orchestration)
├── Stones (x86/ARM64 SBC)
│   ├── Docker, systemd, moss daemon
│   └── Run: databases, stateful services, core offerings
│
└── Pebbles (Android, rooted/PostmarketOS)
    ├── Termux + sshd, no systemd
    ├── pebble-agent (foreground Android service)
    └── Run: sensor offerings, edge ML, IoT gateways, light workers
```

### Technical Stack

**Platform requirements:**
- PostmarketOS (preferred - true Linux) OR LineageOS (rooted, wider device support)
- Termux for GNU userland
- Minimum: Android 5.0+, 4GB RAM, 32GB storage

**Pebble Agent:**
- Rust binary compiled for `aarch64-linux-android`
- Runs as Android foreground service (via Termux)
- HTTP client polls Lantern for work assignments
- Sensor access via Android NDK/JNI
- Job executor for native ARM binaries or scripts (Python, Node.js)
- Health monitor (battery temp, charge level, storage)

**No Docker, no systemd:**
- Lightweight process manager (`supervisord` in Termux)
- Jobs run as native ARM binaries or interpreted scripts
- Offerings packaged as Termux-compatible artifacts

---

## Killer Features: Sensor Suite

Every smartphone includes hardware no Raspberry Pi has:

| Sensor | Use Cases |
|--------|-----------|
| **GPS/GNSS** | Location tracking, geofencing, asset monitoring |
| **Camera** | Visual ML inference, QR scanning, motion-triggered recording |
| **Accelerometer + Gyro** | Motion detection, orientation sensing, vibration monitoring |
| **Magnetometer** | Compass heading, directional awareness |
| **Microphone** | Audio monitoring, noise level detection, voice commands |
| **Barometer** | Weather station, altitude tracking |
| **Ambient light** | Daylight sensing, presence detection |
| **NFC** | Contactless authentication, payment gateway |
| **Proximity** | Physical presence detection |

---

## Example Sensor Offerings

### 1. Location Tracker
```yaml
offering: location-tracker
type: sensor
platform: pebble
requirements:
  sensors: [gps]
  permissions: [ACCESS_FINE_LOCATION]
binary: /data/data/com.termux/files/usr/bin/location-daemon
ports:
  native: 8080
environment:
  LOCATION_UPDATE_INTERVAL: 300  # 5 minutes
```

### 2. Motion Detector
```yaml
offering: motion-detector
type: sensor
platform: pebble
requirements:
  sensors: [accelerometer, gyroscope]
binary: /data/data/com.termux/files/usr/bin/motion-daemon
event_stream: true  # Sends events to Lantern on motion
```

### 3. Visual QR Scanner
```yaml
offering: visual-qr-scanner
type: sensor
platform: pebble
requirements:
  sensors: [camera]
  permissions: [CAMERA]
binary: /data/data/com.termux/files/usr/bin/qr-scanner
```

---

## Comparison: Stones vs Pebbles

| Feature | Stone (SBC) | Pebble (Android) |
|---------|-------------|------------------|
| **Compute** | Better (x86/ARM64, 4-8 cores) | Good enough (ARM, 4-8 cores) |
| **Memory** | Better (8-32GB) | Limited (4-6GB) |
| **Docker** | ✅ Full support | ❌ Proot only (slow) |
| **systemd** | ✅ Yes | ❌ Android init |
| **Remote mgmt** | ✅ SSH, standard Linux | ⚠️ SSH via Termux, quirky |
| **Power** | 15-30W | 3-5W ⭐ |
| **UPS** | ❌ External required | ✅ Built-in battery ⭐ |
| **Sensors** | ❌ None | ✅ GPS, camera, motion, NFC, etc. ⭐⭐⭐ |
| **Networking** | ✅ Ethernet + WiFi | ⚠️ WiFi only (or LTE) |
| **24/7 use** | ✅ Designed for it | ⚠️ Thermal concerns, battery wear |
| **Cost** | $30-60 (new SBC) | $10-40 (used phone) |
| **Setup** | Easy (Debian image) | Hard (root, Termux, config) ⚠️ |
| **Lifespan** | 5-10 years | 1-3 years (battery death) ⚠️ |

---

## Use Cases (Maker/Enthusiast Focus)

### 1. Personal Mesh Location Network
- Deploy 3-5 old phones around home/property
- Each reports GPS + WiFi SSIDs to Lantern
- Triangulate device position using WiFi proximity
- DIY "Find My" network without Apple/Google

### 2. Perimeter Motion Detection
- Phones mounted at entry points
- Accelerometer detects vibration (door opening)
- Camera captures photo on motion event
- Sends alert to home automation system

### 3. DIY Weather Station Mesh
- Phones with barometer sensors
- Report pressure, temperature, altitude
- Aggregate data in Lantern for micro-climate monitoring

### 4. Edge ML Inference Nodes
- TensorFlow Lite runs natively on Android
- Object detection, face recognition locally
- Process on-device, send metadata to Lantern (privacy-preserving)

### 5. Offline NFC Authentication Gateway
- Phone with NFC reads keycards/tags
- Grants access to other stone services
- Useful for maker spaces, home labs

### 6. Vehicle Telematics Logger
- Phone with GPS + accelerometer in car
- Logs trips, speed, harsh braking events
- Syncs to Lantern when on home WiFi

---

## When to Use Pebbles vs Stones

**Use Pebbles for:**
- ✅ Sensor-dependent workloads (location, motion, vision)
- ✅ Ultra-low-power edge nodes with battery backup
- ✅ Disposable/temporary deployments
- ✅ Learning/hobby projects
- ✅ Regions with abundant phone e-waste, limited SBC access

**Use Stones for:**
- ✅ Databases, stateful services
- ✅ Docker-based offerings
- ✅ Production-critical workloads
- ✅ Long-term infrastructure (5+ years)
- ✅ High-performance compute

---

## Safety & Operational Considerations

### Battery Management
⚠️ **Critical safety concern**: Phones not designed for 24/7 charging
- Use charger with 5V/1A output (slow charging = less heat)
- Install Advanced Charging Controller (AccA) to limit charge to 80%
- Monitor temperature: should stay under 40°C
- Mount in ventilated area (vertical stand near fan)
- **Stop immediately** if battery shows signs of swelling

### Thermal Management
- Phones designed for intermittent use, not server workloads
- Potential CPU throttling under sustained load
- Consider passive cooling (heat sink stickers)
- Avoid direct sunlight or enclosed spaces

### Lifespan Expectations
- Battery: 1-2 years before significant degradation
- Device: 2-3 years typical operational life
- Plan for replacement, not long-term infrastructure

---

## Implementation Roadmap

### Milestone 1: Proof of Concept (2-4 weeks)
- [ ] Compile `pebble-agent` for `aarch64-linux-android`
- [ ] Test on LineageOS device with Termux
- [ ] Register pebble with Lantern (manual process)
- [ ] Run single sensor offering (GPS tracker)

### Milestone 2: Sensor Abstraction Layer (4-6 weeks)
- [ ] Rust wrapper for Android sensor APIs (JNI/NDK)
- [ ] Test all common sensors (GPS, accelerometer, camera, microphone)
- [ ] Create offering template for sensor-based jobs

### Milestone 3: Enthusiast Documentation (2-3 weeks)
- [ ] Write setup guide for PostmarketOS + LineageOS
- [ ] Document recommended devices (community-tested)
- [ ] Create troubleshooting FAQ
- [ ] Build 3-5 sample sensor offerings

### Milestone 4: Lantern Integration (3-4 weeks)
- [ ] Extend Lantern to recognize "pebble" node type
- [ ] Pull-based work dispatch (pebbles poll for jobs)
- [ ] Sensor data ingestion API
- [ ] Health monitoring (battery temp, charge level alerts)

### Milestone 5: Community Launch (ongoing)
- [ ] Publish docs under `/docs/guides/pebbles/` (marked `[EXPERIMENTAL]`)
- [ ] Create GitHub Discussions for device compatibility matrix
- [ ] Release "Pebble Starter Kit" with sample offerings
- [ ] Tag as `experimental`, `community-supported`

---

## Documentation Structure

**New section:** `/docs/guides/pebbles/`

```
pebbles/
├── index.md                 # Overview: what are pebbles?
├── android-setup.md         # Setup guide (PostmarketOS, LineageOS)
├── pebble-agent.md          # Agent architecture, compilation
├── sensor-offerings.md      # How to create sensor offerings
├── device-compatibility.md  # Community device matrix
├── safety.md                # Battery safety, thermal management
├── troubleshooting.md       # Common issues
└── use-cases.md             # Inspiring projects
```

**Tag conventions:**
- `[EXPERIMENTAL]` in all pebble documentation
- `[COMMUNITY-SUPPORTED]` - no official support SLA
- Clear warnings about battery safety, thermal limits
- Not recommended for production infrastructure

---

## Recommended Devices (Community Testing Needed)

**Best candidates:**
- **OnePlus 6/6T**: PostmarketOS support, USB-C, 6-8GB RAM, active dev community
- **Google Pixel 3a**: LineageOS support, guaranteed updates, 4GB RAM, widely available
- **Moto G series**: Budget-friendly, LineageOS support, abundant on secondhand market

**Criteria:**
- Unlockable bootloader OR already rooted
- 4GB+ RAM, 32GB+ storage
- USB-C preferred (better thermals)
- Active custom ROM community support
- Removable battery (rare) or good thermal design

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Battery swelling/fire | High | Battery charge limiting (80%), temp monitoring, safety guide |
| Security vulnerabilities | Medium | Require custom ROM with active updates, firewall, DMZ-only |
| Setup complexity | Medium | Comprehensive docs, community device matrix, pre-tested configs |
| Inconsistent hardware | Medium | Focus on 3-5 well-tested models, community compatibility reports |
| Short lifespan | Low | Set expectations (1-3 years), position as disposable tier |

---

## Open Questions

1. Should pebbles self-register with Lantern, or require manual provisioning?
2. What's the minimum Android version to support? (Propose: 8.0+)
3. How to handle sensor permissions at scale? (Need automated consent flow)
4. Should we support unrooted phones with limited capabilities?
5. Network architecture: Should pebbles be DMZ-only, or trusted network allowed?

---

## Success Metrics (Post-Launch)

- **Adoption**: 50+ community members deploy pebbles in first 6 months
- **Device matrix**: Community tests 20+ different phone models
- **Offerings**: 10+ sensor-based offerings created and shared
- **Safety**: Zero reported battery/thermal incidents with proper safety guidance
- **Innovation**: At least 3 novel use cases not anticipated in this proposal

---

## Conclusion

**Recommendation**: Proceed as experimental, maker-tier feature.

**Rationale:**
- Unique sensor capabilities unlock use cases impossible with traditional SBCs
- Addresses e-waste problem, especially valuable in resource-constrained regions
- Low development risk (separate from stone architecture)
- High educational/community engagement value
- Clear positioning prevents production misuse

**Next step**: Milestone 1 - Build proof of concept with single device running GPS tracker offering.

---

**Related documents:**
- `/docs/architecture/principles.md` - Core architectural principles
- `/docs/proposals/` - Other experimental proposals
- Future: `/docs/guides/pebbles/` - User-facing documentation (post-approval)
