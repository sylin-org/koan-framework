# Gaming Screenshot Analysis Style

**Added:** 2025-10-18  
**Style ID:** `gaming`  
**Icon:** 🎮  
**Priority:** 7 (8th position in list)

## Purpose

Specialized analysis style for video game screenshots with gaming-specific terminology and focus on:
- Game UI/HUD elements
- Art style and graphics quality
- Gameplay context
- In-game environment

## Classification

**Smart Mode Detection Keywords:**
```
game, gaming, screenshot, video game, gameplay, UI, HUD, menu, 
ingame, playstation, xbox, pc gaming
```

**When to Use:**
- Screenshots from any video game
- Game menus and UI screens
- Gameplay footage stills
- Game development screenshots
- Game review/streaming content

## Analysis Focus

### 1. Game UI Elements
- HUD (heads-up display)
- Health/stamina bars
- Minimaps and radar
- Quest markers and objectives
- Inventory screens
- Menu interfaces
- Crosshairs and targeting reticles

### 2. Art Style & Graphics
- **Realistic:** Photorealistic rendering
- **Stylized:** Artistic interpretation
- **Pixel Art:** Retro/indie aesthetic
- **Cel-Shaded:** Cartoon/comic style
- **Low-Poly:** Minimalist geometric
- **Anime-Style:** Japanese animation aesthetic

### 3. Game Genre Indicators
- FPS (First-Person Shooter)
- RPG (Role-Playing Game)
- Platformer
- Strategy
- Racing
- Fighting
- Adventure
- Simulation

### 4. Visual Effects
- Particle effects
- Post-processing (bloom, chromatic aberration)
- Motion blur
- Depth of field
- Lens flares
- Screen-space reflections

### 5. Graphics Quality
- Resolution indicators
- Anti-aliasing quality
- Texture detail level
- Rendering fidelity

## Required Facts

The gaming style ensures these optional facts are included when visible:

1. **visible text**: UI labels, quest names, game text
2. **subject 1**: Character/player models
3. **themes**: Art style classification
4. **color grade**: Post-processing effects

## Omitted Facts

These facts are typically not relevant for game screenshots:

- **weather**: Unless game simulates realistic weather
- **time**: Unless realistic day/night cycle
- **era cues**: Virtual worlds don't have eras
- **locale cues**: In-game locations are fictional

## Example Output

```json
{
  "tags": ["fps-game", "sci-fi", "hud-visible", "first-person", "combat"],
  "summary": "First-person shooter gameplay showing futuristic sci-fi corridor with HUD elements visible. Health bar at 100%, ammo counter showing 30/120 rounds. Cyberpunk aesthetic with neon lighting and holographic UI elements.",
  "facts": {
    "type": ["ingame-screenshot"],
    "style": ["3d-render", "photorealistic"],
    "composition": ["centered", "first-person-view"],
    "palette": ["blue", "cyan", "purple"],
    "lighting": ["neon", "dramatic", "low-key"],
    "setting": ["indoor", "sci-fi-corridor"],
    "mood": ["tense", "futuristic"],
    "themes": ["cyberpunk", "sci-fi"],
    "subject 1": ["player-character", "armored-suit", "weapon-visible"],
    "visible text": ["Health: 100", "Ammo: 30/120", "Quest: Find the Artifact"],
    "color grade": ["teal-orange", "high-contrast", "bloom"]
  }
}
```

## Integration

### Automatic Seeding

The gaming style is seeded automatically on application startup via `AnalysisStyleSeeder.CreateGamingStyle()`.

### Smart Mode

When using "Smart Analysis", the AI will automatically detect gaming screenshots and apply this specialized style.

### Manual Selection

Users can manually select "In-Game Screenshot" from the split button dropdown to force gaming-specific analysis.

## Prompt Enhancements

Base prompt includes gaming-specific style examples:

**Type Examples:**
```
"type": ["portrait", "landscape", ..., "ingame-screenshot", ...]
```

**Style Examples:**
```
"style": ["photography", "painting", "3d-render", "pixel-art", 
          "cel-shaded", "game-graphics", ...]
```

## Use Cases

1. **Game Library Management**: Organizing gaming screenshots
2. **Game Reviews**: Analyzing game visuals and UI
3. **Streaming Content**: Cataloging gameplay moments
4. **Game Development**: Tracking visual progress
5. **Retro Gaming**: Pixel art and classic game screenshots
6. **Esports**: Competitive gameplay analysis

## Notes

- Gaming screenshots often have unique characteristics (UI overlays, stylized graphics)
- The style properly handles both realistic and stylized game art
- Useful for distinguishing game content from real photography
- Supports all gaming platforms (PC, console, mobile)
