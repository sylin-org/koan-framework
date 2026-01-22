# garden-cricket Specification

**Ambient audio awareness for Zen Garden**

**Status:** Draft  
**Version:** 0.1.0  
**Date:** January 2026

---

## Overview

garden-cricket is an audio companion service that creates a nature soundscape from infrastructure state. Each Stone becomes a voice in a spatial chorus—crickets chirping, water flowing, wind rising. You can hear your garden's health without looking.

### Status: Easter Egg

**Cricket is intentionally undocumented.**

- Not listed in `garden-rake --help`
- Not mentioned in public documentation
- Not enabled by default
- Discovered by those who look

```
# Found only in config file comments:

# [cricket]
# enabled = false  
# There is sound in the garden, for those who listen.
```

The feature exists for those who find it. It demonstrates that Zen Garden believes hardware can delight—that infrastructure can be *inhabited*, not just operated. 

Those who discover it become the ones who tell others.

### Design Philosophy

Cricket extends ambient awareness to hearing. Where Firefly lets you *see* your infrastructure breathe, Cricket lets you *hear* it sing.

Four principles:

1. **Beautiful first** — The soundscape must be something people *want* to listen to, independent of monitoring value.
2. **Never alarm** — The soundscape shifts but never startles. No beeps, no sirens, no sudden sounds.
3. **Rise from silence** — The soundscape never hits like a bus. When you start listening, sound emerges gently from nothing. Always.
4. **Spatial and alive** — Each Stone has its own voice in physical space. The garden has dimension.

### The Core Idea

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   Your homelab shelf at night:                                  │
│                                                                 │
│   ┌─────────┐    ┌─────────┐    ┌─────────┐                    │
│   │ wyse-01 │    │ wyse-02 │    │ wyse-03 │                    │
│   │   🔊    │    │   🔊    │    │   🔊    │                    │
│   │  chirp  │    │  chirp  │    │  chirp  │                    │
│   └─────────┘    └─────────┘    └─────────┘                    │
│       ↓              ↓              ↓                           │
│                                                                 │
│   Three crickets, three positions in space.                     │
│   Each with its own voice, its own rhythm.                      │
│   Together: a summer night soundscape.                          │
│                                                                 │
│   You fall asleep to the sound of healthy infrastructure.       │
│   You wake if the soundscape changes.                           │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Architecture

### Default Mode: Per-Stone Audio

Each Stone runs garden-cricket and produces its own audio output. This creates true spatial sound—the cricket on the left is literally on the left.

```
┌─────────────────────────────────────────────────────────────────┐
│  PER-STONE MODE (Default)                                       │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌───────────────┐  ┌───────────────┐  ┌───────────────┐       │
│  │   Stone 1     │  │   Stone 2     │  │   Stone 3     │       │
│  │               │  │               │  │               │       │
│  │  Moss         │  │  Moss         │  │  Moss         │       │
│  │    ↓ SSE      │  │    ↓ SSE      │  │    ↓ SSE      │       │
│  │  Cricket      │  │  Cricket      │  │  Cricket      │       │
│  │    ↓          │  │    ↓          │  │    ↓          │       │
│  │  🔊 Speaker   │  │  🔊 Speaker   │  │  🔊 Speaker   │       │
│  │               │  │               │  │               │       │
│  └───────────────┘  └───────────────┘  └───────────────┘       │
│                                                                 │
│  Each Stone: its own cricket, its own speaker, its own voice.   │
│  Physical 3D soundscape. True spatial awareness.                │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Hardware:** USB speakers, 3.5mm speakers, or HDMI audio on each Stone. Even cheap $5 USB speakers work.

### Alternative Mode: Centralized Mixer

For setups without per-Stone audio, a single workstation can mix all Stones into one stereo/surround output.

```
┌─────────────────────────────────────────────────────────────────┐
│  CENTRALIZED MODE                                               │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────┐  ┌─────────┐  ┌─────────┐                         │
│  │ Stone 1 │  │ Stone 2 │  │ Stone 3 │                         │
│  │  Moss   │  │  Moss   │  │  Moss   │                         │
│  └────┬────┘  └────┬────┘  └────┬────┘                         │
│       │            │            │                               │
│       └────────────┼────────────┘                               │
│                    ▼                                            │
│           ┌────────────────┐                                    │
│           │  Workstation   │                                    │
│           │                │                                    │
│           │  Cricket       │                                    │
│           │  (mixer mode)  │                                    │
│           │       ↓        │                                    │
│           │   🔊 Stereo    │                                    │
│           └────────────────┘                                    │
│                                                                 │
│  Workstation queries all Stones, mixes into stereo/surround.    │
│  Stone positions mapped to stereo field.                        │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Web Mode: Browser-Based

Cricket can run entirely in the browser via WebAudio API. The Lantern dashboard could include an audio toggle.

---

## Sound Design

### Layers

The soundscape has four layers that mix together:

```
┌─────────────────────────────────────────────────────────────────┐
│  LAYER STACK                                                    │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  Layer 4: EVENTS (momentary)                                    │
│  ─────────────────────────                                      │
│  Deploy: wind chime                                             │
│  Tend: gentle tone                                              │
│  Backup: owl hoot                                               │
│                                                                 │
│  Layer 3: WEATHER (garden-wide, dynamic)                        │
│  ─────────────────────────────────────                          │
│  Light load: silence / soft breeze                              │
│  Moderate load: breeze in leaves                                │
│  Heavy load: gentle rain                                        │
│  Critical: wind picks up                                        │
│                                                                 │
│  Layer 2: CRICKETS (per-stone, continuous)                      │
│  ──────────────────────────────────────                         │
│  Each Stone = one cricket voice                                 │
│  Chirp frequency = activity level                               │
│  Chirp quality = health                                         │
│                                                                 │
│  Layer 1: BASE (environment, always present)                    │
│  ─────────────────────────────────────────                      │
│  Very low ambient bed                                           │
│  Sets the "room" - night, forest, etc.                          │
│  Pond enabled: add water sounds                                 │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Per-Stone Cricket Voice

Each Stone is a cricket. The cricket's characteristics come from the Stone's identity:

```python
def stone_to_cricket(stone_name: str) -> CricketVoice:
    seed = hash(stone_name)
    
    return CricketVoice(
        pitch = 2000 + (seed % 2000),      # 2-4 kHz range
        timbre = select_timbre(seed),       # Tone character
        rhythm_offset = seed % 1000,        # Phase offset in ms
        chirp_pattern = select_pattern(seed) # Rhythmic pattern
    )
```

**Over time, you learn your crickets:**
- "That high one is the database server"
- "The rhythmic one is the web frontend"
- "The deep one is the storage node"

### Health Mapping

| Stone State | Cricket Behavior |
|-------------|------------------|
| Thriving, idle | Occasional chirp (every 15-30s) |
| Thriving, active | Frequent chirps, lively |
| Heavy activity | Rapid chirping, layered |
| Withering | Chirps become strained, irregular |
| Wilting | Distressed call, then silence |
| Resting | Very rare, sleepy chirp |
| Offline | Silence (this cricket stops) |

### The Security Tell: Water

Following Firefly's pattern—water sounds only appear when Pond security is enabled.

```
No Pond:    Dry night (crickets, breeze, no water)
Pond:       Water feature (gentle stream, fountain)

You can hear security state with your eyes closed.
```

### Weather Layer (Garden-Wide)

The weather layer responds to aggregate garden state:

| Garden State | Weather |
|--------------|---------|
| All healthy, light activity | Clear night (crickets only) |
| Moderate activity | Soft breeze |
| Heavy activity | Light rain |
| Something degraded | Cooler atmosphere (pre-dawn feel) |
| Something critical | Unsettled (wind picks up) |

**Key principle:** Weather *shifts*, never *snaps*. A 30-60 second crossfade between states. Never jarring.

### Event Sounds

| Event | Sound | Duration |
|-------|-------|----------|
| Service deployed | Soft wind chime (single note) | 2s |
| Service stopped | Chime fades out | 1.5s |
| Backup started | Distant owl hoot | 1s |
| Backup completed | Owl settles | 1s |
| `garden-rake tend` | Gentle acknowledgment tone | 0.5s |
| Stone joined garden | New cricket fades in | 3s |
| Stone left garden | Cricket fades out | 3s |
| 24h healthy | Dawn chorus (brief birdsong) | 4s |
| 100 day uptime | Wind chimes celebration | 5s |

---

## Commands

### CLI Integration

Cricket integrates with garden-rake, but the commands are **hidden**:

```bash
# Not shown in --help, but works for those who know

# Start/stop
garden-rake listen                      # Begin default soundscape
garden-rake listen to garden-life       # Explicit soundscape
garden-rake listen to night-rain        # Different soundscape
garden-rake hush                        # Stop audio

# Aliases for poetic stop
garden-rake listen to silence           # Intentional silence
garden-rake listen to the silence       # Same

# Options
garden-rake listen --volume 0.3         # Volume 0.0-1.0
garden-rake listen --stones "wyse-*"    # Only specific stones
garden-rake listen --schedule 22:00-08:00  # Time window

# Soundscape management
garden-rake soundscapes                 # List installed
garden-rake soundscapes search forest   # Find community soundscapes
garden-rake soundscapes install {name}  # Download
garden-rake soundscapes create          # Interactive builder
```

### Command Syntax

Following the zen verb pattern:

```bash
# Zen syntax (positional)
garden-rake listen
garden-rake listen to garden-life
garden-rake listen to the rain

# Natural language
"Listen to the garden"
"Listen to the rain"
"Hush"

These are sentences, not commands.
```

### The Verb Trinity

```
observe  →  see the garden (visual snapshot)
watch    →  monitor the garden (visual stream)
listen   →  hear the garden (audio)
hush     →  silence
```

Three senses: sight (observe), attention (watch), hearing (listen).

---

## Soundscapes

### Default: garden-life

The default soundscape—a warm summer night.

```yaml
# soundscapes/garden-life/manifest.yaml

soundscape:
  name: garden-life
  version: 1.0.0
  description: Summer night in the garden
  author: Zen Garden Project
  license: MIT

layers:
  base:
    file: base-night-ambient.ogg
    volume: 0.2
    loop: true
    
  cricket:
    healthy:
      files:
        - cricket-chirp-1.ogg
        - cricket-chirp-2.ogg
        - cricket-chirp-3.ogg
      interval: [15000, 30000]  # ms range
    active:
      files:
        - cricket-chirp-rapid-1.ogg
      interval: [2000, 5000]
    degraded:
      files:
        - cricket-strained-1.ogg
      interval: [10000, 20000]
      
  pond:
    file: gentle-stream.ogg
    volume: 0.25
    loop: true
    
  weather:
    breeze:
      file: wind-in-leaves.ogg
      volume: 0.15
    rain:
      file: light-rain.ogg
      volume: 0.2
    wind:
      file: wind-unsettled.ogg
      volume: 0.25
      
  events:
    deploy:
      file: wind-chime-single.ogg
      volume: 0.4
    tend:
      file: singing-bowl-gentle.ogg
      volume: 0.3
    backup_start:
      file: owl-hoot-distant.ogg
      volume: 0.25
    dawn_chorus:
      file: morning-birds-brief.ogg
      volume: 0.3
```

### Community Soundscapes

Users can create and share soundscapes:

| Soundscape | Theme | Author |
|------------|-------|--------|
| `garden-life` | Summer night, crickets | Core |
| `night-rain` | Rainy evening | Core |
| `deep-forest` | Owls, forest ambience | Community |
| `japanese-garden` | Temple bells, bamboo fountain | Community |
| `coastal` | Waves, seabirds | Community |
| `minimal` | Single tones, ultra-subtle | Community |
| `arctic` | Wind, distant ice | Community |

### Soundscape Distribution

```
~/.zen-garden/soundscapes/
├── garden-life/
│   ├── manifest.yaml
│   ├── sounds/
│   │   ├── base-night-ambient.ogg
│   │   ├── cricket-chirp-1.ogg
│   │   ├── gentle-stream.ogg
│   │   └── ...
│   └── LICENSE
├── night-rain/
│   └── ...
└── deep-forest/
    └── ...
```

### Soundscape Index

```bash
$ garden-rake soundscapes

INSTALLED
  garden-life     Summer night, crickets       [default]
  night-rain      Rainy evening

AVAILABLE (zen-garden.dev/soundscapes)
  deep-forest     Owls and forest ambience     ⭐ 4.8
  japanese-garden Temple bells, bamboo         ⭐ 4.7
  coastal         Waves and seabirds           ⭐ 4.5
  minimal         Ultra-subtle tones           ⭐ 4.2

Install: garden-rake soundscapes install deep-forest
```

---

## Sound Sources (CC/Open-Source)

Cricket ships with sounds sourced from Creative Commons libraries. Users can substitute their own.

### Recommended Sources

| Source | License | Best For | URL |
|--------|---------|----------|-----|
| **Freesound.org** | CC0, CC-BY, CC-BY-NC | Individual sounds, crickets, water | freesound.org |
| **BBC Sound Effects** | RemArc (personal/educational) | Nature ambience, high quality | bbcrewind.co.uk/sounds |
| **Mixkit** | Mixkit License (free) | Nature, ambient | mixkit.co/free-sound-effects/ |
| **BigSoundBank** | CC0 | Field recordings | bigsoundbank.com |
| **Internet Archive** | Various | Ambient collections | archive.org |

### Recommended Freesound.org Sounds

**Cricket chirps (CC0 or CC-BY):**

| Sound ID | Author | Description | License |
|----------|--------|-------------|---------|
| `353073` | kmckinney7 | Single cricket chirp, clean | CC0 |
| `32244` | digifishmusic | Cricket chirp 3600Hz, high quality | CC-BY |
| `253447` | Otterbahn | Cricket chirping at night | CC-BY |
| `175020` | sengjinn | Ambient night field cricket | CC0 |

**Water sounds (search terms):**
- "gentle stream" — Soft water flow
- "small fountain" — Bubbling water
- "brook babble" — Natural stream

**Ambient beds:**
- User `Luftrum` — Excellent ambient nature packs
- Search: "night ambient", "summer night", "evening atmosphere"

**Weather:**
- "light rain" — Gentle rain sounds
- "soft breeze" — Wind in grass
- "wind in trees" — Rustling leaves

### BigSoundBank (CC0 Public Domain)

High-quality field recordings, all CC0:
- Field cricket: bigsoundbank.com/detail-1020-field-cricket.html
- Various nature ambiences

### BBC Sound Effects Archive

33,000+ sounds, free for personal/educational use:
- bbcrewind.co.uk/sounds
- RemArc license (non-commercial)
- Exceptional quality nature recordings from BBC Natural History Unit

**Note:** BBC sounds require attribution and are non-commercial only. For open-source distribution, prefer CC0/CC-BY sources.

### Attribution

If using CC-BY sources, Cricket generates an attribution file:

```markdown
# ~/.zen-garden/soundscapes/garden-life/ATTRIBUTION.md

# Sound Attribution

This soundscape contains sounds from:

- "Cricket Chirp" by digifishmusic
  freesound.org/people/digifishmusic/sounds/32244/
  Licensed under CC-BY 3.0

- "Gentle Stream" by Luftrum
  freesound.org/people/Luftrum/sounds/...
  Licensed under CC-BY 3.0
```

---

## Configuration

### Location

```
/etc/zen-garden/cricket.toml         # System-wide
~/.config/zen-garden/cricket.toml    # User override
```

### Full Configuration Reference

```toml
# garden-cricket configuration

[audio]
enabled = true
device = "default"              # ALSA/PulseAudio device, or "default"
volume = 0.4                    # Master volume 0.0-1.0

[connection]
moss_url = "http://localhost:7185"
mode = "local"                  # local | mixer | web
# For mixer mode:
# stones = ["stone-01", "stone-02", "stone-03"]

[schedule]
# Only play during these hours (empty = always)
active_hours = ""               # e.g., "22:00-08:00"
active_days = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"]

[soundscape]
default = "garden-life"
path = "~/.zen-garden/soundscapes"

[cricket]
base_interval = 20000           # ms between chirps (idle)
active_interval = 3000          # ms between chirps (active)
pitch_range = [2000, 4000]      # Hz range for cricket voices
variation = 0.2                 # Timing variation (0.0-1.0)

[layers]
base_volume = 0.2
cricket_volume = 0.5
weather_volume = 0.3
event_volume = 0.4
pond_volume = 0.25

[transitions]
crossfade_duration = 30000      # ms for weather transitions
event_duration = 2000           # ms for event sounds

[events]
deploy_sound = true
tend_sound = true
backup_sound = true
milestone_celebrations = true
```

### Minimal Configuration

```toml
# Just enable it, defaults are sensible

[audio]
enabled = true
volume = 0.3
```

---

## Installation

### Per-Stone (Default Mode)

Install Cricket on each Stone alongside Moss:

```bash
# From package manager
sudo apt install garden-cricket

# From cargo
cargo install garden-cricket

# Enable and start
sudo systemctl enable garden-cricket
sudo systemctl start garden-cricket
```

### Hardware: USB Speakers

Cheap USB speakers work fine:

| Option | Price | Notes |
|--------|-------|-------|
| Generic USB speaker | $5-10 | Adequate quality |
| USB soundbar | $15-20 | Better clarity |
| 3.5mm speakers + USB DAC | $20-30 | Best quality |

For thin clients without audio out, USB speakers that show up as an ALSA device work well.

---

## Systemd Service

```ini
# /etc/systemd/system/garden-cricket.service

[Unit]
Description=Zen Garden Cricket audio indicator
Documentation=https://zen-garden.dev/docs/cricket
After=garden-moss.service sound.target
Wants=garden-moss.service

[Service]
Type=simple
ExecStart=/usr/bin/garden-cricket
Restart=always
RestartSec=5
User=root
Environment=RUST_LOG=info
SupplementaryGroups=audio

[Install]
WantedBy=multi-user.target
```

---

## Sound Design Principles

### 1. Rise from Silence

**The soundscape never starts abruptly.**

When you run `garden-rake listen`, you don't get hit with a wall of crickets. Instead:

```
0s        Silence
2s        Barely perceptible ambient bed fades in
5s        First cricket chirp, distant, soft  
10s       Second Stone's cricket joins, still gentle
15s       Base layer reaches normal volume
20s       Crickets at normal rhythm
30s       Weather layer fades in (if applicable)
45s       Water sounds fade in (if Pond enabled)
60s       Full soundscape, gently arrived
```

The garden wakes up. It doesn't switch on.

**This applies to everything:**
- Starting the soundscape
- Resuming from pause
- A Stone coming online (its cricket fades in over 10-15s)
- Weather changes (30-60s crossfade)
- Events (gentle onset, even celebrations)

**Never:**
- Instant full volume
- Sudden sound after silence
- Jarring transitions

The experience should feel like walking into a garden at dusk—the sounds were always there, you just started noticing them.

### 2. Never Alarm

```
BAD:    Silence → BEEP BEEP BEEP
        Jarring, fight-or-flight response

GOOD:   Calm night → wind picks up → unsettled weather
        Gradual, noticeable, not alarming
```

### 3. Natural Variation

```
Crickets don't chirp metronomically.
Add 20-30% timing variation.
Add subtle pitch variation per chirp.
No two chirps identical.
```

### 4. Gradual Transitions

```
State change: healthy → degraded

Don't: Instant cut to new atmosphere
Do:    30-60 second crossfade
```

### 5. The Absence Signal

When a Stone goes offline, its cricket *stops*.

You may not consciously notice. But your brain does. "Why is it so quiet on the left?"

This is "negative space monitoring"—the absence of expected sound is itself information.

---

## Quick Reference

```
┌─────────────────────────────────────────────────────────────────┐
│  CRICKET QUICK REFERENCE                                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  STATUS: Easter egg. Undocumented. For those who look.          │
│                                                                 │
│  COMMANDS (hidden from --help)                                  │
│  ─────────────────────────────                                                       │
│  garden-rake listen              Start default soundscape       │
│  garden-rake listen to {name}    Start specific soundscape      │
│  garden-rake hush                Stop audio                     │
│  garden-rake soundscapes         List/manage soundscapes        │
│                                                                 │
│  WHAT YOU HEAR                                                  │
│  ─────────────                                                  │
│  Crickets chirping       = Stones are alive                     │
│  Cricket frequency       = Activity level                       │
│  Water sounds            = Pond security enabled                │
│  Rain                    = Heavy load                           │
│  Wind picking up         = Something's wrong                    │
│  Cricket goes silent     = Stone offline                        │
│  Wind chime              = Service deployed                     │
│  Gentle tone             = Someone tended the garden            │
│                                                                 │
│  MODES                                                          │
│  ─────                                                          │
│  Per-stone (default)     Each Stone plays its own cricket       │
│  Mixer                   Workstation mixes all Stones           │
│  Web                     Browser-based via Lantern              │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Relationship with Firefly

Cricket and Firefly are siblings—same information, different senses.

| State | Firefly (Visual) | Cricket (Audio) |
|-------|------------------|-----------------|
| Thriving, idle | Green breathe | Calm crickets |
| Active | Green + sparkles | Active chirping |
| Heavy load | Bright green | Add rain layer |
| Degraded | Yellow pulse | Irregular crickets |
| Critical | Red churn | Unsettled weather |
| Pond enabled | Water ripples | Water sounds |
| Tend event | Happy wiggle | Gentle tone |

Use one, the other, or both.

---

**Document Status:** Draft specification  
**Last Updated:** January 2026

---

**Zen Garden Cricket**  
*Hear your infrastructure sing.*

---

```
There is sound in the garden,
for those who listen.
```
