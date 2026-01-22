# Phone Stones: Smartphones as Zen Garden Nodes

**Turn old smartphones into full-featured Stones with wired power, wired network, and real Linux.**

---

## Executive Summary

Modern smartphones contain significant compute resources that go to waste when devices are retired. A 2019-era flagship phone has comparable specs to purpose-built single-board computers:

| Specification | Google Pixel 3a | Raspberry Pi 5 | Dell Wyse 5070 |
|---------------|-----------------|----------------|----------------|
| CPU | Snapdragon 670 (8-core) | BCM2712 (4-core) | Celeron J4105 (4-core) |
| RAM | 4GB | 4-8GB | 4-8GB |
| Storage | 64GB | SD card | 16-64GB eMMC |
| Power draw | 3-5W | 5-15W | 10-20W |
| Built-in UPS | ✅ Battery | ❌ | ❌ |
| Sensors | GPS, camera, accelerometer | ❌ | ❌ |
| Cost (used) | $40-80 | $60-80 (new) | $60-100 |

With PostmarketOS providing real Linux (systemd, mainline kernel), and USB-C hubs providing wired Ethernet + power delivery, old phones become viable Stones—not a separate "Pebble" tier, but actual garden participants running Moss and Docker.

---

## Requirements

### Supported Devices

PostmarketOS maintains a [Community tier](https://postmarketos.org/install/) of well-supported devices with stable releases. These use close-to-mainline kernels and receive regular updates.

**Recommended devices (Community tier, v25.12 stable):**

| Device | RAM | Storage | Notes |
|--------|-----|---------|-------|
| **Google Pixel 3a** | 4GB | 64GB | Best documented, 53GB usable storage |
| **Google Pixel 3a XL** | 4GB | 64GB | Same as 3a, larger screen |
| **OnePlus 6** | 6-8GB | 64-256GB | More RAM, active community |
| **OnePlus 6T** | 6-8GB | 128-256GB | Same as OP6, in-display fingerprint |
| **Xiaomi Pocophone F1** | 6-8GB | 64-256GB | Budget flagship, good thermals |
| **Fairphone 4** | 6-8GB | 128-256GB | Designed for longevity |

**Acquisition sources:**
- eBay: $40-100 depending on condition
- Swappa: Verified condition, slightly higher prices
- Local classifieds: Best prices, verify bootloader unlockability
- Drawer: Check for forgotten devices

**Before purchasing, verify:**
1. Bootloader can be unlocked (carrier-locked devices may not allow this)
2. Device powers on and charges
3. Screen condition doesn't matter for headless operation
4. Battery holds some charge (will be limited to 80% anyway)

### USB-C Hub Requirements

You need a hub that provides both Gigabit Ethernet and Power Delivery through a single USB-C connection.

**Required features:**
- USB-C male connector (to phone)
- RJ-45 Gigabit Ethernet port (10/100/1000)
- USB-C Power Delivery passthrough (60W+ recommended)
- RTL8153 or RTL8153B chipset (best Linux compatibility)

**Tested products:**

| Product | Ethernet | PD | Price | Notes |
|---------|----------|-----|-------|-------|
| [uni USB-C to Ethernet + 100W PD](https://www.amazon.com/uni-Ethernet-Adapter-Charging-Gigabit/dp/B0C3GHBLB6) | 1Gbps | 100W | ~$20 | Minimal, just Ethernet + power |
| [Anker 655 USB-C Hub 8-in-1](https://www.anker.com/products/a8382) | 1Gbps | 100W | ~$70 | Additional USB-A ports, HDMI |
| [StarTech US1GC30PD](https://www.startech.com/en-us/networking-io/us1gc30pd) | 1Gbps | 60W | ~$45 | Industrial quality, 2-year warranty |
| [UGREEN Revodok 6-in-1](https://www.ugreen.com/products/ugreen-6-in-1-usb-c-hub) | 1Gbps | 100W | ~$35 | Good balance of features/price |

**Chipset compatibility:**
The RTL8153 chipset has native Linux kernel support (no additional drivers needed). Most USB-C Ethernet adapters use this chipset. Verify by checking product specifications or reviews mentioning Linux compatibility.

### Power Supply

Use a USB-C Power Delivery charger connected to the hub's PD input port.

**Requirements:**
- USB-C PD output (not just USB-A with adapter)
- 18W minimum (phone draws 3-5W, hub overhead)
- 30W+ recommended for headroom

**Suitable chargers:**
- Any USB-C laptop charger (45W-100W)
- Anker Nano series (20W-65W)
- Phone chargers with USB-C PD (18W+)

---

## Operating System

### PostmarketOS

PostmarketOS transforms smartphones into standard Linux systems with:
- **systemd** init system (required for Moss)
- **Close-to-mainline kernel** (5.19+ for Pixel 3a)
- **Alpine Linux base** (lightweight, ~6MB excluding kernel)
- **10-year support goal** for devices

**Installation overview:**

1. **Unlock bootloader** (device-specific, see PostmarketOS wiki)
2. **Download image** from [postmarketos.org/install](https://postmarketos.org/install/)
3. **Flash via fastboot:**
   ```bash
   # Enter fastboot mode (Power + Volume Down on most devices)
   fastboot flash boot postmarketos-edge-phosh-google-sargo-boot.img
   fastboot flash userdata postmarketos-edge-phosh-google-sargo.img
   ```
4. **First boot:** Default user `user`, password `147147`
5. **Enable SSH:**
   ```bash
   sudo rc-update add sshd
   sudo service sshd start
   ```

**Detailed instructions:** [PostmarketOS Installation Guide](https://wiki.postmarketos.org/wiki/Installation)

### Alternative: Mobian (Debian-based)

For users preferring Debian over Alpine Linux, Mobian provides a Debian-based mobile Linux:
- Weekly images available at [images.mobian.org](https://images.mobian.org/qcom/weekly/)
- Default user `mobian`, password `1234`
- Debian package ecosystem (apt instead of apk)

---

## Docker Configuration

Docker requires specific kernel features. PostmarketOS mainline kernels typically include these, but verification is recommended.

### Verify Kernel Compatibility

```bash
# Download Docker's kernel check script
wget https://raw.githubusercontent.com/moby/moby/master/contrib/check-config.sh
chmod +x check-config.sh
./check-config.sh
```

**Required features (must show "enabled"):**
- CONFIG_NAMESPACES
- CONFIG_CGROUPS
- CONFIG_CGROUP_CPUACCT
- CONFIG_CGROUP_DEVICE
- CONFIG_CGROUP_FREEZER
- CONFIG_CGROUP_SCHED
- CONFIG_MEMCG
- CONFIG_VETH
- CONFIG_BRIDGE

### Install Docker

**On PostmarketOS (Alpine):**
```bash
# Install Docker
sudo apk add docker docker-compose

# Add user to docker group
sudo addgroup $USER docker

# Enable cgroups service
sudo rc-update add cgroups
sudo service cgroups start

# Start Docker
sudo rc-update add docker
sudo service docker start
```

**Cgroup configuration (if needed):**

If Docker fails to start, try hybrid cgroup mode:
```bash
# Edit /etc/rc.conf
sudo nano /etc/rc.conf

# Find and set:
rc_cgroup_mode="hybrid"

# Reboot
sudo reboot
```

**iptables-legacy (if networking fails):**

Some kernels require legacy iptables:
```bash
# Switch to iptables-legacy
sudo apk add iptables
sudo ln -sf /sbin/iptables-legacy /sbin/iptables
sudo ln -sf /sbin/ip6tables-legacy /sbin/ip6tables

# Restart Docker
sudo service docker restart
```

### Docker Version Note

If the latest Docker version has issues, Docker v27.5.0 or earlier may work better with mobile kernels:
```bash
# Install specific version
sudo apk add docker=27.5.0-r0
```

---

## Battery Management

### The Problem

Smartphones weren't designed for 24/7 charging. Continuous charging causes:
- Battery swelling (fire risk)
- Reduced capacity
- Heat buildup
- Shortened device lifespan

### The Solution: Charge Limiting + Bypass

Modern devices support **bypass charging**: once the battery reaches a threshold (typically 80%), power flows directly to the device, bypassing the battery entirely.

**On stock Android (Pixel 8/9 series):**
- Settings → Battery → Charging Optimization → "Limit to 80%"
- Battery stays at 80%, device runs from wall power
- Google confirmed bypass charging works in this mode

**On PostmarketOS:**

Linux exposes battery charge control via sysfs. The exact path varies by device:

```bash
# Find your battery
ls /sys/class/power_supply/

# Common paths (check which exists on your device):
/sys/class/power_supply/battery/charge_control_end_threshold
/sys/class/power_supply/BAT0/charge_control_end_threshold
/sys/class/power_supply/bms/charge_control_end_threshold
```

**Set charge limit (if supported):**
```bash
# Set 80% charge limit
echo 80 | sudo tee /sys/class/power_supply/battery/charge_control_end_threshold
```

**Make persistent via systemd:**
```bash
# Create service file
sudo tee /etc/systemd/system/battery-limit.service << 'EOF'
[Unit]
Description=Set battery charge limit to 80%
After=multi-user.target

[Service]
Type=oneshot
ExecStart=/bin/sh -c 'echo 80 > /sys/class/power_supply/battery/charge_control_end_threshold'
RemainAfterExit=yes

[Install]
WantedBy=multi-user.target
EOF

# Enable service
sudo systemctl enable battery-limit.service
sudo systemctl start battery-limit.service
```

**If sysfs control unavailable:**

Use TLP or a timer-based charging approach:
```bash
# Install TLP (power management)
sudo apk add tlp

# Configure charge thresholds in /etc/tlp.conf
# (device-specific, may not work on all hardware)
```

### Safety Monitoring

Create a temperature monitoring script:

```bash
#!/bin/bash
# /usr/local/bin/thermal-monitor.sh

TEMP_PATH="/sys/class/thermal/thermal_zone0/temp"
MAX_TEMP=45000  # 45°C in millidegrees

while true; do
    TEMP=$(cat $TEMP_PATH)
    if [ "$TEMP" -gt "$MAX_TEMP" ]; then
        logger -p daemon.warning "Phone Stone thermal warning: ${TEMP}m°C"
        # Optional: reduce CPU frequency or stop services
    fi
    sleep 60
done
```

**Safety guidelines:**
- Mount device vertically for airflow
- Keep away from heat sources
- Monitor temperature weekly during first month
- **If battery swells: disconnect immediately, do not use**

---

## Network Configuration

### Wired Ethernet

USB-C Ethernet adapters appear as standard network interfaces:

```bash
# Check interface name
ip link show

# Typical names: eth0, enp0s*, usb0

# Configure via NetworkManager or /etc/network/interfaces
```

**Static IP (recommended for servers):**
```bash
# /etc/network/interfaces
auto eth0
iface eth0 inet static
    address 192.168.1.50
    netmask 255.255.255.0
    gateway 192.168.1.1
    dns-nameservers 192.168.1.1 8.8.8.8
```

### Performance Notes

USB Ethernet adapters on phones may show reduced throughput compared to native ports:

| Configuration | Typical Speed |
|---------------|---------------|
| Native Gigabit (desktop) | 940 Mbps |
| USB 3.0 Gigabit adapter (laptop) | 800-940 Mbps |
| USB-C Gigabit adapter (phone) | 75-300 Mbps |

The reduced speed is still adequate for most Stone workloads (databases, caches, APIs). For high-throughput storage (MinIO), prefer traditional hardware.

---

## Installing Moss

Once Docker is running, install Moss as you would on any Stone:

```bash
# Download Moss
curl -sSL https://zen-garden.dev/install-moss.sh | bash

# Or manual installation
wget https://github.com/zen-garden/moss/releases/latest/download/garden-moss-aarch64
chmod +x garden-moss-aarch64
sudo mv garden-moss-aarch64 /usr/local/bin/garden-moss

# Create systemd service
sudo tee /etc/systemd/system/garden-moss.service << 'EOF'
[Unit]
Description=Garden Moss Daemon
After=network.target docker.service
Requires=docker.service

[Service]
Type=simple
ExecStart=/usr/local/bin/garden-moss
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

# Enable and start
sudo systemctl enable garden-moss
sudo systemctl start garden-moss
```

**Verify operation:**
```bash
# Check Moss status
sudo systemctl status garden-moss

# Check mDNS announcement
avahi-browse -art | grep moss

# From another machine
garden-rake discover
```

---

## Physical Setup

### Recommended Configuration

```
Power outlet
    │
    ▼
┌─────────────────┐
│ USB-C PD        │
│ Charger (30W+)  │
└────────┬────────┘
         │ USB-C cable
         ▼
┌─────────────────┐
│ USB-C Hub       │◄──── Ethernet cable ────► Network switch
│ (Ethernet + PD) │
└────────┬────────┘
         │ USB-C to phone
         ▼
┌─────────────────┐
│ Phone           │ ◄── Mounted vertically
│ (PostmarketOS)  │     for airflow
└─────────────────┘
```

### Mounting Options

**Vertical stand (recommended):**
- 3D-printed phone stand
- Tablet/phone dock
- VESA-mounted phone holder

**Avoid:**
- Laying flat (heat accumulation)
- Enclosed spaces (no airflow)
- Direct sunlight
- Near heat sources

### Labeling

Label each Phone Stone with:
- Hostname (e.g., `pixel-stone-01`)
- IP address (if static)
- Service running (e.g., "Redis cache")
- Date commissioned

---

## Use Cases

### Ideal Workloads

Phone Stones excel at:

| Workload | Why It Works |
|----------|--------------|
| Redis cache | Low memory, low CPU, benefits from built-in UPS |
| Small MongoDB | 4GB RAM sufficient for <10GB databases |
| API gateway | Low latency, always-on with battery backup |
| MQTT broker | Lightweight, benefits from 24/7 uptime |
| DNS server | Low resource, high reliability needs |
| Monitoring agent | Prometheus node_exporter, always-on |

### Sensor-Enhanced Workloads

Phone Stones retain sensor access (device/driver dependent):

| Sensor | Potential Use |
|--------|---------------|
| GPS | Location-tagged data, geofencing triggers |
| Camera | QR code scanning, visual monitoring |
| Accelerometer | Vibration monitoring, motion detection |
| Barometer | Weather data collection |
| Microphone | Ambient noise monitoring |

**Note:** Sensor access in PostmarketOS varies by device. Check the device wiki page for supported hardware.

### Not Recommended

Avoid these workloads on Phone Stones:

| Workload | Reason |
|----------|--------|
| Large databases (>20GB) | Storage/RAM limitations |
| High-throughput storage | USB Ethernet bottleneck |
| Continuous heavy CPU | Thermal throttling |
| Critical production | Experimental platform |

---

## Troubleshooting

### Docker Won't Start

1. **Check kernel support:** Run `check-config.sh` script
2. **Try hybrid cgroups:** Set `rc_cgroup_mode="hybrid"` in `/etc/rc.conf`
3. **Switch to iptables-legacy:** See installation section
4. **Try older Docker version:** `sudo apk add docker=27.5.0-r0`

### Ethernet Not Working

1. **Check adapter connection:** LED should light up
2. **Verify interface exists:** `ip link show`
3. **Check dmesg:** `dmesg | grep -i eth`
4. **Try different adapter:** Some chipsets have compatibility issues

### Overheating

1. **Check temperature:** `cat /sys/class/thermal/thermal_zone0/temp`
2. **Improve airflow:** Mount vertically, add space around device
3. **Reduce load:** Migrate heavy services elsewhere
4. **Check battery charge:** Ensure not charging above 80%

### Battery Swelling

**STOP IMMEDIATELY** if you notice:
- Phone case bulging
- Screen lifting from frame
- Unusual heat when not under load

**Actions:**
1. Disconnect from power
2. Do not puncture battery
3. Place in fireproof container outdoors
4. Dispose at battery recycling facility

---

## Comparison: Phone Stone vs Traditional Hardware

| Aspect | Phone Stone | Wyse Thin Client | Raspberry Pi 5 |
|--------|-------------|------------------|----------------|
| **Acquisition** | $40-80 used | $60-100 used | $60-80 new |
| **Power draw** | 3-5W | 10-20W | 5-15W |
| **Built-in UPS** | ✅ Yes | ❌ No | ❌ No |
| **Built-in screen** | ✅ Yes | ❌ No | ❌ No |
| **Native Ethernet** | ❌ USB adapter | ✅ Yes | ✅ Yes |
| **Sensors** | ✅ GPS, camera, etc | ❌ None | ❌ None |
| **Docker support** | ⚠️ Requires setup | ✅ Standard | ✅ Standard |
| **Support lifespan** | ⚠️ Community | ✅ Stable | ✅ Stable |
| **Recommended for** | Learning, edge, backup | Production | Development |

---

## References

### PostmarketOS Resources
- [PostmarketOS Installation](https://wiki.postmarketos.org/wiki/Installation)
- [Pixel 3a Device Page](https://wiki.postmarketos.org/wiki/Google_Pixel_3a_(google-sargo))
- [Docker on PostmarketOS](https://wiki.postmarketos.org/wiki/Docker)
- [Supported Devices](https://postmarketos.org/install/)

### Hardware Documentation
- [RTL8153 Datasheet](https://www.olimex.com/Products/USB-Modules/Ethernet/USB-GIGABIT/resources/rtl8153.pdf) - USB Ethernet chipset
- [USB Power Delivery Specification](https://www.usb.org/document-library/usb-power-delivery)

### Battery Management
- [Linux Battery Charge Thresholds](https://linuxconfig.org/how-to-set-battery-charge-thresholds-on-linux)
- [Pixel Bypass Charging (Android Authority)](https://www.androidauthority.com/pixel-bypass-charging-3507373/)

### Zen Garden Documentation
- [Hardware Guide](zen-garden-guide-hardware.md) - Traditional Stone hardware
- [First Stone Setup](zen-garden-guide-first-stone.md) - Initial configuration
- [Moss Daemon Spec](zen-garden-spec-moss-daemon.md) - Moss architecture

---

## Appendix: Tested Configurations

### Google Pixel 3a + PostmarketOS v25.12

**Working:**
- PostmarketOS edge with Phosh UI
- SSH access
- USB-C Ethernet (RTL8153 adapters)
- WiFi
- Display (for debugging)
- Audio

**Requires additional setup:**
- Docker (cgroup configuration)
- Charge limiting (sysfs, device-specific)

**Not working/untested:**
- Titan M TPM
- Camera (partially supported)
- Cellular modem

### OnePlus 6 + PostmarketOS v25.12

**Working:**
- PostmarketOS edge with Phosh UI
- SSH access
- USB-C Ethernet
- WiFi
- 6-8GB RAM fully accessible

**Requires additional setup:**
- Docker
- Charge limiting

**Advantages over Pixel 3a:**
- More RAM (6-8GB vs 4GB)
- Larger storage options
- Better thermal management

---

**Document Version:** 1.0  
**Last Updated:** January 2026  
**Status:** Experimental  
**Maintainer:** Community
