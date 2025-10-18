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
        var existingStyles = await AnalysisStyle.All();
        if (existingStyles.Any())
        {
            logger.LogInformation("Analysis styles already seeded ({Count} styles found)", existingStyles.Count);
            return;
        }

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
            CreateMacroStyle()
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
        TemplateVersion = 1,
        ClassificationKeywords = "people, faces, portraits, human subjects, emotion, person, group",
        FocusInstructions = @"ANALYSIS FOCUS:
Pay special attention to:
- Facial expressions and emotional content (joy, contemplation, tension, serenity, vulnerability, confidence)
- Clothing details and fashion choices (specific garments, colors, patterns, accessories, style era)
- Body language, pose, and gaze direction (relaxed, tense, dynamic, static, eye contact)
- Interpersonal relationships between subjects (proximity, interaction, group dynamics)

When describing subjects, ALWAYS include:
- Expression/emotion cues (not just ""person"")
- Specific clothing items with colors (""black-hoodie"", ""red-headphones"")
- Pose description (""relaxed-pose"", ""arms-crossed"", ""leaning"")
- Gaze direction (""looking-left"", ""eye-contact"", ""looking-away"")",
        EnhanceExamples = new List<string> { "subject clothing", "facial expressions" },
        RequiredOptionalFacts = new List<string> { "subject 1", "subject 2", "subject 3" },
        OmittedOptionalFacts = new List<string> { "atmospherics", "locale cues" }
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
        TemplateVersion = 1,
        ClassificationKeywords = "product, commercial, item, object, merchandise, packaging, branding",
        FocusInstructions = @"ANALYSIS FOCUS:
Focus on product presentation and commercial viability:
- Product clarity and feature visibility (what is being sold?)
- Material quality and finish (matte, glossy, textured, metallic)
- Visible product features (buttons, screens, labels, specifications)
- Background cleanliness and professionalism (seamless, neutral, styled)
- Lighting quality (even, shadow-free, three-point, rim-light)
- Commercial appeal and market readiness

When describing products, include:
- Specific product type and category
- Visible materials and finishes
- Color accuracy and palette
- Brand elements if visible
- Presentation style (studio, lifestyle, flat-lay)",
        EnhanceExamples = new List<string> { "lighting setup" },
        RequiredOptionalFacts = new List<string> { "subject 1", "light sources" },
        OmittedOptionalFacts = new List<string> { "mood", "themes", "atmospherics" }
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
        TemplateVersion = 1,
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
        EnhanceExamples = new List<string> { "composition details", "atmospherics" },
        RequiredOptionalFacts = new List<string> { "atmospherics", "time", "weather", "locale cues" },
        OmittedOptionalFacts = new List<string> { "subject 1", "subject 2" }
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
        TemplateVersion = 1,
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
        EnhanceExamples = new List<string> { "composition details" },
        RequiredOptionalFacts = new List<string> { "locale cues", "era cues" },
        OmittedOptionalFacts = new List<string> { "subject 1", "atmospherics" }
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
        TemplateVersion = 1,
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
        EnhanceExamples = new List<string> { "subject clothing" },
        RequiredOptionalFacts = new List<string> { "subject 1", "depth cues" },
        OmittedOptionalFacts = new List<string> { "atmospherics" }
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
        TemplateVersion = 1,
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
        EnhanceExamples = new List<string> { "atmospherics" },
        RequiredOptionalFacts = new List<string> { "depth cues", "subject 1" },
        OmittedOptionalFacts = new List<string> { "setting", "locale cues" }
    };
}
