using Koan.Data.Core;
using S6.SnapVault.Models;

namespace S6.SnapVault.Initialization;

/// <summary>
/// Seeds default analysis styles on application startup (S5.Recs pattern)
/// Styles are entity-based but prompt assembly is factory-controlled
/// </summary>
public static class AnalysisStyleSeeder
{
    public static async Task SeedDefaultStylesAsync(ILogger logger)
    {
        // Check if styles already seeded
        //var existingStyles = await AnalysisStyle.All();
        //if (existingStyles.Any())
        //{
        //    logger.LogInformation("Analysis styles already seeded ({Count} styles found)", existingStyles.Count);
        //    return;
        //}

        logger.LogInformation("Seeding default analysis styles...");

        var defaultStyles = CreateDefaultStyles();

        foreach (var style in defaultStyles)
        {
            await style.Save();
            logger.LogDebug("Seeded analysis style: {StyleName}", style.Name);
        }

        logger.LogInformation("Seeded {Count} default analysis styles", defaultStyles.Length);
    }

    private static AnalysisStyle[] CreateDefaultStyles()
    {
        return new[]
        {
            CreateSmartStyle(),
            CreatePortraitStyle(),
            CreateProductStyle(),
            CreateLandscapeStyle(),
            CreateArchitectureStyle(),
            CreateActionStyle(),
            CreateMacroStyle(),
            CreateGamingStyle()
        };
    }

    private static AnalysisStyle CreateSmartStyle() => new()
    {
        Id = "smart",
        Name = "Smart Analysis",
        Icon = "🤖",
        Description = "AI automatically determines best approach",
        Priority = 0,
        IsSmartStyle = true,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 1,
        // No focus instructions or enhancements - this is the classifier
    };

    private static AnalysisStyle CreatePortraitStyle() => new()
    {
        Id = "portrait",
        Name = "Portrait & People",
        Icon = "👤",
        Description = "Focus on faces, emotion, and human subjects",
        Priority = 1,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 2,  // v2: Refactored to template-based mandatory fields
        ClassificationKeywords = "people, faces, portraits, human subjects, emotion, person, group",
        FocusInstructions = @"PORTRAIT & PEOPLE ANALYSIS - Enhanced Guidelines:

For each visible person, provide detailed ""subject N"" facts with rich descriptors (5-10 elements per person):
- Emotional expressions: joy, contemplation, serenity, confidence, vulnerability, tension, surprise
- Clothing specifics: ""black-leather-jacket"", ""white-button-down"", ""red-knit-beanie"", ""silver-hoop-earrings""
- Body language: ""relaxed-pose"", ""arms-crossed"", ""leaning-forward"", ""sitting"", ""standing-confidently""
- Gaze direction: ""looking-left"", ""direct-eye-contact"", ""looking-away"", ""eyes-closed""
- Physical traits: hair-color, hair-length, hair-style, build, distinctive-features
- Group dynamics: proximity, interaction-type, relative-positioning (if multiple people)

Each subject fact should contain 5-10 descriptive elements covering emotion, clothing, pose, gaze, and physical characteristics.",

        // Promote subject 1 to mandatory (always required for portrait analysis)
        MandatoryFields = new List<string> { "subject 1" },

        // Emphasize subject 2/3 (encouraged if multiple people visible)
        EmphasisFields = new List<string> { "subject 2", "subject 3" },

        // De-emphasize environmental facts (less relevant for portraits)
        DeemphasizedFields = new List<string> { "atmospherics", "locale cues" }
    };

    private static AnalysisStyle CreateProductStyle() => new()
    {
        Id = "product",
        Name = "Product & Commercial",
        Icon = "📦",
        Description = "Detailed product features and marketability",
        Priority = 2,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 2,
        ClassificationKeywords = "product, commercial, item, object, merchandise, packaging, branding",
        FocusInstructions = @"PRODUCT & COMMERCIAL ANALYSIS - Enhanced Guidelines:

Focus on product presentation and commercial viability:
- Product clarity and feature visibility (what is being sold?)
- Material quality and finish (matte, glossy, textured, metallic)
- Visible product features (buttons, screens, labels, specifications)
- Background cleanliness and professionalism (seamless, neutral, styled)
- Lighting quality (even, shadow-free, three-point, rim-light)
- Commercial appeal and market readiness

When describing products, include:
- Specific product type and category (in subject 1)
- Visible materials and finishes
- Color accuracy and palette
- Brand elements if visible
- Presentation style (studio, lifestyle, flat-lay)",

        MandatoryFields = new List<string> { "subject 1" },
        EmphasisFields = new List<string> { "light sources" },  // Encouraged but not all products have visible sources
        DeemphasizedFields = new List<string> { "mood", "themes", "atmospherics" }
    };

    private static AnalysisStyle CreateLandscapeStyle() => new()
    {
        Id = "landscape",
        Name = "Landscape & Nature",
        Icon = "🏔️",
        Description = "Composition, lighting, and environmental mood",
        Priority = 3,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 2,
        ClassificationKeywords = "landscape, nature, scenery, outdoor, mountains, forest, water, sky, vista",
        FocusInstructions = @"ANALYSIS FOCUS:
Emphasize natural elements and environmental storytelling:
- Composition rules (rule-of-thirds, leading-lines, foreground-interest, depth-layers)
- Natural lighting and time of day (golden-hour, blue-hour, overcast, harsh-midday)
- Color palette and overall mood (vibrant, muted, warm, cool, saturated)
- Sense of place and scale (vast, intimate, grand, cozy)
- Environmental context (season, weather, climate indicators)
- Atmospheric conditions (fog, haze, clear, stormy)

When describing landscapes, include:
- Dominant natural elements (mountains, water, vegetation)
- Compositional structure (foreground/midground/background)
- Lighting quality and direction
- Weather and atmospheric cues
- Season indicators if visible",
        MandatoryFields = new List<string> { "time", "weather" },  // Always determinable
        EmphasisFields = new List<string> { "atmospherics", "composition details", "locale cues" },  // Atmospherics not always present (clear days)
        DeemphasizedFields = new List<string> { "subject 1", "subject 2", "subject 3" }
    };

    private static AnalysisStyle CreateArchitectureStyle() => new()
    {
        Id = "architecture",
        Name = "Architecture & Interior",
        Icon = "🏛️",
        Description = "Lines, symmetry, materials, and spatial design",
        Priority = 4,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 2,
        ClassificationKeywords = "architecture, building, interior, structure, design, geometric, lines, facade",
        FocusInstructions = @"ANALYSIS FOCUS:
Analyze structural and design elements:
- Lines, symmetry, and geometric composition (vertical, horizontal, converging)
- Perspective and point of view (eye-level, low-angle, high-angle, two-point)
- Materials, textures, and surfaces (concrete, glass, wood, metal, stone)
- Spatial relationships and flow (openness, enclosure, circulation)
- Design elements and architectural style (modern, classical, brutalist, minimalist)
- Lighting and ambiance (natural light, artificial, dramatic, even)

When describing architecture, include:
- Primary structural elements
- Material palette
- Geometric patterns and symmetry
- Architectural style or period
- Spatial qualities (vast, intimate, open, closed)",
        MandatoryFields = new List<string> { "locale cues", "era cues" },
        EmphasisFields = new List<string> { "composition details" },
        DeemphasizedFields = new List<string> { "subject 1", "subject 2", "subject 3", "atmospherics" }
    };

    private static AnalysisStyle CreateActionStyle() => new()
    {
        Id = "action",
        Name = "Action & Moment",
        Icon = "⚡",
        Description = "Motion, timing, and decisive moments",
        Priority = 5,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 2,
        ClassificationKeywords = "action, motion, movement, sports, dynamic, activity, energy, speed",
        FocusInstructions = @"ANALYSIS FOCUS:
Capture dynamic energy and timing:
- Decisive moment and timing (peak action, anticipation, follow-through)
- Motion capture and technical execution (frozen, blur, panning, tracking)
- Subject isolation and clarity (focus, separation from background)
- Energy and dynamism (explosive, graceful, powerful, fluid)
- Technical aspects (shutter speed evidence, motion blur, freeze)
- Composition under movement (predictive framing, lead room)

When describing action, include:
- Type of motion/activity
- Motion capture technique (frozen, blur)
- Energy level and dynamics
- Subject positioning and tracking
- Timing quality (peak moment, transitional)",
        MandatoryFields = new List<string> { "subject 1", "depth cues" },
        EmphasisFields = new List<string>(),
        DeemphasizedFields = new List<string> { "atmospherics", "locale cues" }
    };

    private static AnalysisStyle CreateMacroStyle() => new()
    {
        Id = "macro",
        Name = "Detail & Macro",
        Icon = "🔍",
        Description = "Texture, pattern, and close-up details",
        Priority = 6,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 2,
        ClassificationKeywords = "macro, detail, close-up, texture, pattern, small, intricate, fine",
        FocusInstructions = @"ANALYSIS FOCUS:
Emphasize fine details and textures:
- Texture and pattern detail (surface quality, repetition, variation)
- Technical sharpness and depth of field (razor-sharp, selective focus, bokeh)
- Isolated subject presentation (subject-background separation)
- Abstract visual qualities (form, shape, line, color independent of identity)
- Material characteristics and surfaces (rough, smooth, metallic, organic)
- Scale and perspective (extreme close-up, magnification cues)

When describing macro subjects, include:
- Texture description (rough, smooth, bumpy, crystalline)
- Pattern if present
- Depth of field characteristics
- Material quality
- Abstract visual elements",
        MandatoryFields = new List<string> { "depth cues", "subject 1" },
        EmphasisFields = new List<string> { "atmospherics" },
        DeemphasizedFields = new List<string> { "setting", "locale cues", "time", "weather" }
    };

    private static AnalysisStyle CreateGamingStyle() => new()
    {
        Id = "gaming",
        Name = "In-Game Screenshot",
        Icon = "🎮",
        Description = "Game UI, art style, and gameplay context",
        Priority = 7,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 2,
        ClassificationKeywords = "game, gaming, screenshot, video game, gameplay, UI, HUD, menu, ingame, playstation, xbox, pc gaming",
        FocusInstructions = @"ANALYSIS FOCUS:
Analyze as a video game screenshot with gaming-specific terminology:
- Game UI elements (HUD, health bars, minimaps, menus, quest markers, inventory, crosshairs)
- Art style and graphics (realistic, stylized, pixel-art, cel-shaded, low-poly, photorealistic, retro, anime-style)
- Game genre indicators (FPS, RPG, platformer, strategy, racing, fighting, adventure, simulation)
- In-game environment (dungeon, city, battlefield, space, fantasy world, sci-fi setting)
- Character models and player avatars (character design, armor, weapons, animations)
- Visual effects (particle effects, lighting, shadows, post-processing, motion blur, depth of field)
- Gameplay context (menu screen, combat, exploration, dialogue, cutscene, loading screen)
- Graphics quality (resolution, anti-aliasing, texture detail, rendering quality)

When describing gaming screenshots, include:
- Specific UI elements visible (health bar, minimap, quest log, etc.)
- Art style classification (photorealistic, stylized, pixel-art, etc.)
- Game genre if identifiable
- In-game setting and environment type
- Character/player model details if visible
- Visual effects and post-processing
- Gameplay state (active gameplay, menu, cutscene)",
        MandatoryFields = new List<string> { "subject 1", "color grade" },  // Core gaming elements always present
        EmphasisFields = new List<string> { "visible text" },  // UI text common but not in cutscenes/cinematic modes
        DeemphasizedFields = new List<string> { "weather", "time", "era cues", "locale cues" }
    };
}
