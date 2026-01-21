# garden-firefly Specification

**Physical status indicator for Zen Garden Stones**

**Status:** Draft  
**Version:** 0.1.0  
**Date:** January 2026

---

## Overview

garden-firefly is a companion service that drives a physical LED display attached to a Stone, providing ambient awareness of Stone health and activity. It connects to the local Moss instance via SSE (Server-Sent Events) and translates system state into visual feedback.

### Design Philosophy

The Firefly display serves as a window into your infrastructure. At a glance from across the room, you know if your garden is healthy. Walking closer reveals more detail. The display rewards attention without demanding it.

Three principles guide the visual language:

1. **Truthful** — The display never lies. Visual metaphors match system state.
2. **Glanceable** — Health is obvious in under one second from 3+ meters.
3. **Joyful** — Infrastructure can be delightful. Small moments of whimsy reward attention.

---

## Hardware

### Supported Device

**Primary target:** Waveshare RP2040-Matrix (or compatible)

| Specification | Value |
|---------------|-------|
| Display | 5×5 RGB LED matrix (WS2812B) |
| Controller | RP2040 dual-core ARM Cortex-M0+ |
| Connection | USB-C (appears as CDC-ACM serial) |
| Size | 23.5mm × 18mm |
| Mounting | Adhesive, Velcro, or enclosure |

Any device presenting a 5×5 addressable RGB matrix over USB serial may be supported with appropriate firmware.

### Wiring

None. The RP2040-Matrix is self-contained. Connect USB-C to Stone, apply power and data.

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  STONE                                                          │
│                                                                 │
│  ┌─────────────┐         ┌─────────────┐         ┌───────────┐ │
│  │             │   SSE   │             │  Serial │           │ │
│  │    Moss     │────────▶│   Firefly   │────────▶│  RP2040   │ │
│  │   :7185     │ events  │   Service   │  cmds   │  Matrix   │ │
│  │             │         │             │         │           │ │
│  └─────────────┘         └─────────────┘         └───────────┘ │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

garden-firefly:
1. Connects to Moss SSE endpoint (`http://localhost:7185/api/v1/events`)
2. Maintains internal state model (health, activity, load, security)
3. Renders appropriate visual mode to LED matrix
4. Sends frame updates over USB serial

Firefly is **optional**. Moss operates normally without it. If Firefly crashes, Moss is unaffected.

---

## Visual Modes

The Firefly display has three primary modes, selected based on system state or operator preference.

| Mode | Trigger | Metaphor | Feeling |
|------|---------|----------|---------|
| **Firefly** | No Pond configured | Fireflies in night air | Open, free |
| **Pond** | Pond security enabled | Water surface | Protected, contained |
| **Normative** | Operator override | Direct status | Terse, functional |

### Mode Selection Logic

```
if config.display.mode == "normative":
    use Normative mode
else if stone.pond_enabled:
    use Pond mode
else:
    use Firefly mode
```

The visual mode serves as a **security indicator**. Water only appears when Pond security is enabled. This is intentional and enforced—the display cannot lie about security state (unless Normative mode is explicitly chosen).

---

## Firefly Mode

**When:** No Pond configured (open garden)  
**Metaphor:** Fireflies dancing in night air  
**Feeling:** Open, natural, free

### Visual Language

| Channel | Encodes | Expression |
|---------|---------|------------|
| **Color (hue)** | Health | Green → Yellow → Red |
| **Brightness field** | Load over time | Dim (low) → Bright (high) |
| **Sparkles** | Live activity | Dancing points of light |
| **Animation tempo** | Urgency | Slow (calm) → Fast (stressed) |

### States

| State | Color | Animation | Description |
|-------|-------|-----------|-------------|
| Thriving, idle | Living green | Slow breathe | Healthy, at rest |
| Thriving, active | Living green | Breathe + sparkles | Healthy, working |
| Thriving, background | Deeper green | Steady glow | Maintenance task |
| Withering | Warm amber | Medium pulse | Degraded |
| Wilting | Soft coral | Fast pulse | Critical |
| Resting | Dim ember | Very slow breathe | Services paused |
| Offline | Off | None | Stone not running |
| Connection lost | Cool dim blue | Uncertain flicker | Cannot reach Moss |

### Breathing Animation

All 25 LEDs breathe together—brightness fades in and out like a sleeping creature.

```
Breathe cycle: 4 seconds (idle)
               2 seconds (withering)
               0.5 seconds (wilting)

Brightness range: 20% - 60% of max
```

### Sparkles (Activity)

When requests arrive, individual LEDs flash briefly—fireflies blinking.

```
Sparkle duration: 80-120ms
Sparkle color: Slightly brighter than base, hint of white
Density scales with request rate:
  1-5 req/s   → occasional sparkle (1-2 LEDs)
  5-20 req/s  → moderate (5-10 sparkles/sec)
  20+ req/s   → heavy (continuous glitter)
```

### Unified Brightness Field (Load + Timeline)

The 5×5 grid encodes load using brightness *within* the health color. Both dimensions coexist:

```
         ← Older          Newer →
         
Column:   1     2     3     4     5 (NOW)

◉ = dim (low load)
● = bright (high load)

All pixels show health COLOR.
Brightness shows load INTENSITY.

Example: Healthy (green), load increasing over time:

◉◉◉◉●     All green, but brightness tells the story:
◉◉◉●●     "Load has been climbing for the last few minutes"
◉◉●●●
◉●●●●
●●●●●
```

Timeline shifts left every N seconds (default: 60s). Current load renders in rightmost column. The wave of brightness *is* the history—no need for separate timeline mode.

### Boot Animation: Gather and Scatter

```
Phase 1 (0-1.5s):    Dim amber dots appear at edges, drift toward center
Phase 2 (1.5-2.5s):  Lights gather in center, brightness builds
Phase 3 (2.5-3.5s):  Burst outward like startled fireflies, trails fade
Phase 4 (3.5-4s):    Fade to black, pause
Phase 5 (4s+):       Soft green begins in center, spreads, settles to breathing
```

### Event Responses

| Event | Animation | Duration |
|-------|-----------|----------|
| `garden-rake tend` | Diagonal shimmer (happy wiggle) | 400ms |
| Service deployed | Flash ripple outward | 800ms |
| Service stopped | Fade from edges inward | 600ms |
| SSH connection | Corner flash | 200ms |
| 24h healthy | Deep green satisfied pulse | 2s |
| 7 day uptime | Brief celebration sparkle | 2s |
| 100 day uptime | Full sparkle show | 4s |

---

## Pond Mode

**When:** Pond security enabled  
**Metaphor:** Surface of a protected pond  
**Feeling:** Contained, protected, serene

### Visual Language

| Channel | Encodes | Expression |
|---------|---------|------------|
| **Water quality (hue)** | Health | Clear blue-green → Murky amber → Turbid red |
| **Wave intensity** | Load / Activity | Still → Ripples → Waves → Choppy |
| **Wave direction** | Time | Waves flow left (into history) |
| **Slosh** | Events | Physical response to interactions |

### States

| State | Water Quality | Water Motion | Description |
|-------|---------------|--------------|-------------|
| Thriving, idle | Clear blue-green | Still, subtle shimmer | Healthy pond at rest |
| Thriving, active | Clear blue-green | Ripples, gentle waves | Healthy, working |
| Thriving, background | Clear, deeper tint | Slow rolling waves | Maintenance task |
| Withering | Murky amber-green | Choppy | Degraded |
| Wilting | Turbid red-brown | Churning | Critical |
| Resting | Dark, still | Barely visible shimmer | Services paused |
| Offline | Empty (off) | None | Stone not running |
| Connection lost | Foggy gray-blue | Uncertain ripples | Cannot reach Moss |

### Still Water (Idle)

Barely perceptible movement. Like sunlight on a calm pond.

```
Shimmer: Random pixels vary ±5% brightness
Cycle: Very slow (6-8 seconds)
Feeling: Alive but peaceful
```

### Ripples (Single Events)

A request arrives. A drop hits the water.

```
Frame 1:  Single bright pixel (center or random)
Frame 2:  Ring expands outward
Frame 3:  Ring continues, fading
Frame 4:  Ring reaches edges, dissipates
Frame 5:  Return to still

Duration: 600-800ms
```

### Waves (Sustained Activity)

Continuous work creates waves that roll across the surface, flowing left into history.

```
Wave direction: Right to left (now → history)
Wave speed: 800ms per column
Wave height: Brightness variation (±30%)

Multiple overlapping waves create interference patterns.
More activity = more waves = choppier surface.
```

The wave pattern *is* the timeline. Recent activity creates waves on the right; they propagate left and fade. You're watching work ripple into history.

### Choppy Water (Heavy Load)

```
Rapid brightness variations across entire surface.
No clear wave direction—interference patterns.
Still the health color, but agitated.
```

### Churning (Critical)

```
Chaotic, rapid changes.
Red-brown turbid color.
No pattern—turbulent, dangerous water.
"Something is very wrong in this pond."
```

### Slosh (Event Response)

Physical response to interactions. Water moves.

```
TEND (finger dips in water):
  Gentle slosh—brightness shifts right then settles

DEPLOY (stone dropped in):
  Ripple from center outward

SHUTDOWN (drain):
  Water level appears to drop, fade from top
```

### Boot Animation: Pond Fills

```
Phase 1 (0-1s):    Empty (dark)
Phase 2 (1-3s):    Water rises from bottom row by row
Phase 3 (3-3.5s):  Surface settles, initial ripples
Phase 4 (3.5s+):   Still pond, operational
```

### Koi (Joy)

Very rarely, a bright golden pixel swims across the surface.

```
Frequency: Once every 5-10 minutes (randomized)
Path: Gentle curve across grid
Duration: 2-3 seconds
Color: Golden/orange, brighter than surroundings

The koi means nothing. The koi is pure delight.
```

### Security Transition Animations

**Joining Pond (enabling security):**

```
Frame 1-2:  Fireflies active (current mode)
Frame 3-4:  Fireflies drift downward
Frame 5:    They touch "surface" and become ripples
Frame 6:    Ripples spread
Frame 7:    Water settles, Pond mode active
```

**Leaving Pond (disabling security):**

```
Frame 1-2:  Still water (current mode)
Frame 3-4:  Water level drops / fades
Frame 5:    Points of light rise from where water was
Frame 6:    Fireflies take flight
Frame 7:    Firefly mode active
```

---

## Normative Mode

**When:** Operator explicitly configures  
**Metaphor:** None (direct status)  
**Feeling:** Terse, functional, no-nonsense

For operators who prefer straightforward status without visual poetry.

### Visual Language

| Channel | Encodes | Expression |
|---------|---------|------------|
| **Color** | Health | Green / Yellow / Red |
| **Brightness** | Load | Dim (low) → Bright (high) |
| **Blink** | Activity | Blinks when active |

### States

| State | Display |
|-------|---------|
| Healthy, idle | Solid green, dim |
| Healthy, active | Solid green, blinking |
| Healthy, heavy load | Solid green, bright |
| Degraded | Solid yellow |
| Critical | Blinking red |
| Resting | Dim white |
| Offline | Off |

### Layout Options

**Solid (default):** Entire grid shows single state.

```
◉◉◉◉◉
◉◉◉◉◉
◉◉◉◉◉    All green = healthy
◉◉◉◉◉
◉◉◉◉◉
```

**VU Meter:** Bottom-up fill shows load.

```
○○○○○
○○○○○
◉◉◉◉◉    60% load
◉◉◉◉◉
◉◉◉◉◉
```

**Timeline:** Columns show history (like other modes but static, no animation).

```
◉◉◉●●
◉◉●●●    Load increasing over time
◉●●●●
●●●●●
●●●●●
```

### No Animations

- No breathing
- No sparkles
- No boot show (immediate solid color)
- No event responses (or minimal: single flash)
- No koi

Status changes are instant. Direct. Functional.

### Security Indicator

Even in Normative mode, security state is indicated:

```
No Pond:     Standard colors (green/yellow/red)
Pond Active: Subtle blue tint added to all colors
             (e.g., green becomes teal, yellow becomes amber-blue)
```

This maintains the security-tell principle without visual metaphors.

---

## Color Palettes

### Firefly Mode Colors

| Name | RGB | HSL | Use |
|------|-----|-----|-----|
| Living Green | `(20, 180, 70)` | `140°, 0.80, 0.39` | Healthy |
| Content Green | `(15, 140, 50)` | `140°, 0.81, 0.30` | Sustained healthy |
| Warm Amber | `(200, 120, 20)` | `33°, 0.82, 0.43` | Degraded |
| Soft Coral | `(200, 60, 40)` | `8°, 0.67, 0.47` | Critical |
| Rest Ember | `(80, 45, 15)` | `28°, 0.68, 0.19` | Resting |
| Dawn Gold | `(220, 160, 40)` | `40°, 0.78, 0.51` | Boot animation |
| Lost Blue | `(40, 60, 100)` | `220°, 0.43, 0.27` | Connection lost |

### Pond Mode Colors

| Name | RGB | HSL | Use |
|------|-----|-----|-----|
| Clear Water | `(30, 150, 130)` | `170°, 0.67, 0.35` | Healthy |
| Deep Water | `(20, 100, 90)` | `173°, 0.67, 0.24` | Sustained healthy |
| Murky Water | `(150, 120, 50)` | `42°, 0.50, 0.39` | Degraded |
| Turbid Water | `(150, 60, 40)` | `11°, 0.58, 0.37` | Critical |
| Night Pond | `(20, 40, 45)` | `192°, 0.38, 0.13` | Resting |
| Koi Gold | `(220, 150, 40)` | `37°, 0.78, 0.51` | Koi fish |
| Fog | `(60, 70, 90)` | `220°, 0.20, 0.29` | Connection lost |

### Normative Mode Colors

| Name | RGB | Use |
|------|-----|-----|
| Green | `(0, 200, 0)` | Healthy |
| Yellow | `(200, 180, 0)` | Degraded |
| Red | `(200, 0, 0)` | Critical |
| White | `(60, 60, 60)` | Resting |
| Teal (Pond) | `(0, 180, 150)` | Healthy + Pond |
| Amber-Blue (Pond) | `(180, 150, 80)` | Degraded + Pond |

### Organic Color Variation

In Firefly and Pond modes, colors are not static. They drift subtly within their hue range.

```
Hue drift: ±8° over 10-30 seconds
Saturation drift: ±5%
Brightness drift: ±10% (in addition to breathing)

This creates "living" color that feels organic rather than synthetic.
```

---

## Personality

Each Firefly device derives subtle behavioral variations from its Stone identity.

```python
seed = hash(stone_name)

# Derived variations:
sparkle_positions = seed_to_pattern(seed)     # Preferred sparkle locations
breathe_phase = seed_to_offset(seed)          # Offset in breathing cycle
hue_bias = seed_to_hue_offset(seed)           # Slightly warmer or cooler
wave_pattern = seed_to_wave(seed)             # Wave interference seed
```

Three Fireflies on a shelf breathe in similar rhythm but not identical. They have fingerprints.

---

## Configuration

### Location

```
/etc/zen-garden/firefly.toml
```

### Full Configuration Reference

```toml
# garden-firefly configuration

[connection]
moss_url = "http://localhost:7185"    # Moss API endpoint
device = "auto"                        # Serial device ("auto" or "/dev/ttyACM0")
reconnect_interval = 5000              # ms between reconnection attempts

[display]
mode = "auto"                          # auto | firefly | pond | normative
brightness_max = 0.4                   # 0.0-1.0, cap brightness
brightness_min = 0.1                   # Minimum brightness (for breathing)
transition_duration = 500              # ms for color transitions
timeline_interval = 60                 # Seconds per column shift (0 = disabled)

[display.normative]
layout = "solid"                       # solid | vu_meter | timeline
blink_on_activity = true               # Blink when processing requests
security_tint = true                   # Add blue tint when Pond active

[animation]
boot_animation = true                  # Show boot sequence
boot_duration = 4000                   # ms
breathing_enabled = true               # Subtle idle animation
breathe_cycle_idle = 4000              # ms
breathe_cycle_warning = 2000           # ms
sparkle_duration = 100                 # ms per sparkle
wave_speed = 800                       # ms per wave cycle

[events]
tend_response = true                   # Respond to garden-rake tend
deploy_ripple = true                   # Ripple on service deploy
milestone_celebrations = true          # Celebrate uptime milestones

[joy]
organic_color = true                   # Subtle hue/saturation drift
personality = true                     # Derive variations from stone name
koi_enabled = true                     # Occasional koi in Pond mode
koi_interval_min = 300                 # Minimum seconds between koi
koi_interval_max = 600                 # Maximum seconds between koi

[colors.firefly]
healthy = { h = 140, s = 0.80, l = 0.39 }
degraded = { h = 33, s = 0.82, l = 0.43 }
critical = { h = 8, s = 0.67, l = 0.47 }
resting = { h = 28, s = 0.68, l = 0.19 }
lost = { h = 220, s = 0.43, l = 0.27 }

[colors.pond]
healthy = { h = 170, s = 0.67, l = 0.35 }
degraded = { h = 42, s = 0.50, l = 0.39 }
critical = { h = 11, s = 0.58, l = 0.37 }
resting = { h = 192, s = 0.38, l = 0.13 }
lost = { h = 220, s = 0.20, l = 0.29 }
koi = { h = 37, s = 0.78, l = 0.51 }
```

### Minimal Configuration

For most users, no configuration needed. Defaults are sensible.

```toml
# /etc/zen-garden/firefly.toml

# Empty file = all defaults
# Firefly auto-detects Moss and device
```

### Normative Mode Quick Start

```toml
# /etc/zen-garden/firefly.toml

[display]
mode = "normative"

[display.normative]
layout = "vu_meter"
```

---

## Protocol

### Moss → Firefly (SSE Events)

Firefly subscribes to Moss SSE endpoint. Relevant events:

| Event Type | Payload | Firefly Response |
|------------|---------|------------------|
| `stone.health.changed` | `{ health: "thriving" \| "withering" \| "wilting" }` | Update color |
| `stone.load.updated` | `{ cpu: float, memory: float }` | Update brightness |
| `service.started` | `{ service: string }` | Deploy animation |
| `service.stopped` | `{ service: string }` | Fade animation |
| `service.health.changed` | `{ service: string, health: string }` | May affect stone health |
| `request.received` | `{ service: string }` | Sparkle/ripple |
| `task.background.started` | `{ task: string }` | Steady glow/slow wave |
| `task.background.completed` | `{ task: string }` | Return to idle |
| `stone.tended` | `{ by: string }` | Happy wiggle/slosh |
| `pond.joined` | `{ pond: string }` | Transition to Pond mode |
| `pond.left` | `{}` | Transition to Firefly mode |

### Firefly → Matrix (Serial Protocol)

Simple binary protocol over USB CDC serial.

**Frame format:**

```
[0xAA] [length] [pixel data...]

length = number of bytes following (75 for full frame)
pixel data = 25 pixels × 3 bytes (RGB) = 75 bytes
pixel order = row-major, top-left to bottom-right
```

**Example: All green at 50% brightness**

```
0xAA 0x4B [0x00 0x5A 0x23] × 25
```

---

## Installation

### From Package Manager

```bash
# Debian/Ubuntu
sudo apt install garden-firefly

# Arch
sudo pacman -S garden-firefly

# Fedora
sudo dnf install garden-firefly
```

### From Cargo

```bash
cargo install garden-firefly
```

### From Source

```bash
git clone https://github.com/zen-garden/firefly
cd firefly
cargo build --release
sudo cp target/release/garden-firefly /usr/local/bin/
```

### Device Permissions

Firefly needs access to USB serial device.

```bash
# Add user to dialout group (Debian/Ubuntu)
sudo usermod -aG dialout $USER

# Or create udev rule for specific device
echo 'SUBSYSTEM=="tty", ATTRS{idVendor}=="2e8a", ATTRS{idProduct}=="0005", MODE="0666"' | \
  sudo tee /etc/udev/rules.d/99-firefly.rules
sudo udevadm control --reload-rules
```

---

## Systemd Service

```ini
# /etc/systemd/system/garden-firefly.service

[Unit]
Description=Zen Garden Firefly LED indicator
Documentation=https://zen-garden.dev/docs/firefly
After=garden-moss.service
Wants=garden-moss.service

[Service]
Type=simple
ExecStart=/usr/bin/garden-firefly
Restart=always
RestartSec=5
User=root
Environment=RUST_LOG=info

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable garden-firefly
sudo systemctl start garden-firefly
```

---

## Firmware

### RP2040-Matrix MicroPython Firmware

```python
# main.py — Firefly firmware for RP2040-Matrix

import sys
import select
from machine import Pin
from neopixel import NeoPixel

# Configuration
NUM_LEDS = 25
LED_PIN = 16  # GPIO for WS2812 on RP2040-Matrix (verify for your board)

# Initialize
np = NeoPixel(Pin(LED_PIN), NUM_LEDS)

def clear():
    for i in range(NUM_LEDS):
        np[i] = (0, 0, 0)
    np.write()

def parse_frame(data):
    """Parse binary frame: 0xAA + length + RGB data"""
    if len(data) < 2:
        return False
    if data[0] != 0xAA:
        return False
    length = data[1]
    if len(data) < 2 + length:
        return False
    if length != 75:  # 25 pixels × 3 bytes
        return False
    
    for i in range(25):
        offset = 2 + (i * 3)
        r, g, b = data[offset], data[offset + 1], data[offset + 2]
        np[i] = (r, g, b)
    np.write()
    return True

# Main loop
buffer = bytearray()
clear()

while True:
    if sys.stdin in select.select([sys.stdin], [], [], 0.01)[0]:
        chunk = sys.stdin.buffer.read(1)
        if chunk:
            buffer.extend(chunk)
            
            while len(buffer) >= 77:
                if buffer[0] == 0xAA:
                    if parse_frame(buffer[:77]):
                        buffer = buffer[77:]
                    else:
                        buffer = buffer[1:]
                else:
                    buffer = buffer[1:]
```

---

## Command Line

```bash
# Start foreground (for testing)
garden-firefly

# With debug output
RUST_LOG=debug garden-firefly

# Specify device
garden-firefly --device /dev/ttyACM0

# Test mode (cycle through states)
garden-firefly --test

# Demo mode (show boot animation, sample states)
garden-firefly --demo
```

### garden-rake Integration

```bash
# Tend to a stone (triggers happy wiggle / slosh)
garden-rake tend to stone-01

# The Stone's Firefly responds with acknowledgment animation
```

---

## Troubleshooting

### Device Not Found

```
Error: No Firefly device found

Solutions:
1. Check USB connection
2. Verify device: ls /dev/ttyACM*
3. Check permissions: groups $USER (should include 'dialout')
4. Specify device: garden-firefly --device /dev/ttyACM0
```

### No Response to Events

```
Symptoms: Display stuck, not responding to activity

Check:
1. Moss is running: curl http://localhost:7185/api/v1/health
2. SSE endpoint works: curl http://localhost:7185/api/v1/events
3. Firefly logs: journalctl -u garden-firefly -f
```

### Wrong Mode Displaying

```
Symptoms: Shows Firefly mode but Pond is enabled (or vice versa)

Check:
1. Mode not forced in config: display.mode should be "auto"
2. Moss Pond status: garden-rake status
3. Restart Firefly: sudo systemctl restart garden-firefly
```

---

## Quick Reference

```
┌─────────────────────────────────────────────────────────────────┐
│  FIREFLY QUICK REFERENCE                                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  MODE DETECTION (Security Tell)                                 │
│  ─────────────────────────────                                  │
│  Sparkles/breathing = Firefly mode (no Pond, open garden)       │
│  Ripples/waves      = Pond mode (Pond enabled, secured)         │
│  Solid color        = Normative mode (operator override)        │
│                                                                 │
│  HEALTH (COLOR)                                                 │
│  ─────────────                                                  │
│  Green/Blue-green   = Thriving                                  │
│  Yellow/Amber       = Withering (degraded)                      │
│  Red/Coral          = Wilting (critical)                        │
│  Dim ember/gray     = Resting                                   │
│                                                                 │
│  LOAD (BRIGHTNESS within color)                                 │
│  ──────────────────────────────                                 │
│  Dim                = Low load                                  │
│  Bright             = High load                                 │
│  Right → Left       = Now → History (timeline)                  │
│                                                                 │
│  ACTIVITY                                                       │
│  ────────                                                       │
│  Still/breathing    = Idle                                      │
│  Sparkles/ripples   = Active requests                           │
│  Steady glow/waves  = Background work                           │
│  Fast pulse/churn   = Stressed                                  │
│                                                                 │
│  EVENTS                                                         │
│  ──────                                                         │
│  Wiggle/slosh       = Someone tended to this Stone              │
│  Ripple outward     = Service deployed                          │
│  Fade inward        = Service stopped                           │
│  Celebration        = Uptime milestone                          │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Design Notes

### The Security Tell

The visual mode itself communicates security state:

- **Firefly mode** (sparkles, air) = Garden is open, no Pond
- **Pond mode** (water, ripples) = Garden is secured with Pond

This mapping is enforced. Water metaphors only appear when Pond is enabled. The display cannot lie about security posture.

From across a room with multiple Stones, you can instantly see which are in a secured Pond (water) versus open (fireflies).

### Joy in Infrastructure

The Firefly isn't just functional—it's delightful:

- **Boot animation:** Fireflies gather and scatter, or water fills. A moment of arrival.
- **Organic color:** Colors drift subtly, never static. Living, not mechanical.
- **Personality:** Each device derives variations from its Stone name. Fingerprints.
- **Celebrations:** Uptime milestones trigger brief light shows.
- **Koi:** In Pond mode, occasionally a golden pixel swims by. Pure whimsy.

### The Normative Option

Some operators prefer function over poetry. Normative mode provides:

- Direct color mapping (green/yellow/red)
- Optional VU meter or timeline layout
- No animations, instant state changes
- Security indicated by color tint rather than metaphor

Normative mode respects that different operators have different needs.

---

**Document Status:** Draft specification  
**Last Updated:** January 2026

---

**Zen Garden Firefly**  
*See your infrastructure breathe.*
