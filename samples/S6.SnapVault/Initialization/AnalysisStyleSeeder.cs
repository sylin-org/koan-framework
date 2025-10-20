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
            CreateGamingStyle(),
            CreateFoodStyle(),
            CreateFashionStyle(),
            CreateSocialMediaStyle(),
            CreateRealEstateStyle(),
            CreateEventStyle(),
            CreateWildlifeStyle(),
            CreateComicsStyle()
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
        ClassificationKeywords = "people, faces, portraits, human subjects, emotion, person, group, headshot, family photo, environmental portrait",
        FocusInstructions = @"PORTRAIT & PEOPLE ANALYSIS - Professional Guidelines:

For each visible person, provide detailed ""subject N"" facts with rich descriptors (5-10 elements per person):
- Emotional expressions: joy, contemplation, serenity, confidence, vulnerability, tension, surprise
- Clothing specifics: ""black-leather-jacket"", ""white-button-down"", ""red-knit-beanie"", ""silver-hoop-earrings""
- Body language: ""relaxed-pose"", ""arms-crossed"", ""leaning-forward"", ""sitting"", ""standing-confidently""
- Gaze direction: ""looking-left"", ""direct-eye-contact"", ""looking-away"", ""eyes-closed""
- Physical traits: hair-color, hair-length, hair-style, build, distinctive-features
- Group dynamics: proximity, interaction-type, relative-positioning (if multiple people)

Portrait context and style:
- Lighting approach (natural, studio, dramatic, soft, high-key, low-key)
- Setting context (formal, casual, professional, intimate)
- Traditional portraiture styles (studio, environmental, headshot, family, group)

When describing portraits, include:
- Emotional expression and mood for each person
- Physical characteristics and distinctive features
- Clothing and styling details
- Body language and pose
- Gaze direction and engagement
- Group dynamics if multiple subjects
- Lighting quality and approach
- Setting context when relevant

Each subject fact should contain 5-10 descriptive elements covering emotion, clothing, pose, gaze, and physical characteristics.",

        // Promote subject 1 to mandatory (always required for portrait analysis)
        MandatoryFields = new List<string> { "subject 1" },

        // Emphasize subject 2/3 and mood (emotional expression is core to portraiture)
        EmphasisFields = new List<string> { "subject 2", "subject 3", "mood" },

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
        ClassificationKeywords = "landscape, nature, scenery, outdoor, mountains, forest, water, sky, vista, horizon, environmental, seascape, countryside, wilderness",
        FocusInstructions = @"LANDSCAPE & NATURE ANALYSIS - Professional Guidelines:

Emphasize natural environments and scenic composition:
- Composition rules (rule-of-thirds, leading-lines, foreground-interest, depth-layers)
- Natural lighting and time of day (golden-hour, blue-hour, overcast, harsh-midday)
- Color palette and overall mood (vibrant, muted, warm, cool, saturated)
- Sense of place and scale (vast, intimate, grand, cozy)
- Environmental context (season, weather conditions, climate indicators)
- Atmospheric conditions (fog, haze, clear, stormy, misty)
- Dominant landscape features (mountains, water bodies, forests, skies, geological formations)

When describing landscapes, include:
- Dominant natural elements (mountains, water, vegetation)
- Compositional structure (foreground/midground/background)
- Lighting quality and direction
- Time of day indicators
- Weather and atmospheric cues (when determinable)
- Season indicators if visible
- Sense of scale and perspective
- Color palette and tonal qualities

Landscape vs other nature styles:
- If animals are the PRIMARY subject → Use Wildlife & Animals style
- If architectural structure is the focus → Use Architecture style
- Landscape = environment itself is the subject

NOTE: Weather may not always be determinable (studio work, abstract landscapes, clear neutral conditions).",
        MandatoryFields = new List<string> { "time" },  // Time always determinable from lighting
        EmphasisFields = new List<string> { "weather", "atmospherics", "composition details", "locale cues" },  // Weather not always determinable
        DeemphasizedFields = new List<string> { "subject 1", "subject 2", "subject 3" }
    };

    private static AnalysisStyle CreateArchitectureStyle() => new()
    {
        Id = "architecture",
        Name = "Architecture & Design",
        Icon = "🏛️",
        Description = "Structural design, lines, materials, and form",
        Priority = 4,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 2,
        ClassificationKeywords = "architecture, building, structure, design, geometric, lines, facade, structural, columns, beams, architectural detail",
        FocusInstructions = @"ARCHITECTURE & DESIGN ANALYSIS - Professional Guidelines:

Analyze structural and design elements:
- Lines, symmetry, and geometric composition (vertical, horizontal, converging, radial)
- Perspective and point of view (eye-level, low-angle, high-angle, two-point, worm's-eye)
- Materials, textures, and surfaces (concrete, glass, wood, metal, stone, brick)
- Structural elements (columns, beams, arches, cantilevers, load-bearing walls)
- Spatial relationships and flow (openness, enclosure, circulation, volumes)
- Design elements and architectural style (modern, classical, brutalist, minimalist, gothic, art-deco)
- Lighting and ambiance (natural light integration, artificial lighting, dramatic shadows)

When describing architecture, include:
- Primary structural elements and form
- Material palette and surface treatments
- Geometric patterns and symmetry
- Architectural style or period (when determinable)
- Spatial qualities (vast, intimate, open, closed, flowing)
- Design philosophy or movement if evident
- Perspective and viewing angle
- Lighting approach and shadow play

Architecture vs Real Estate distinction:
- Architecture style = STRUCTURAL DESIGN, form, materials, artistic/engineering merit
- Real Estate style = LIVABILITY, staging, marketability, room function
- If the focus is 'Would I live here?' → Use Real Estate
- If the focus is 'How is this designed?' → Use Architecture

NOTE: Era cues may not be determinable for ultra-modern or intentionally timeless designs.",
        MandatoryFields = new List<string> { "locale cues" },  // Can be architectural-context
        EmphasisFields = new List<string> { "composition details", "era cues" },  // Era not always determinable
        DeemphasizedFields = new List<string> { "subject 1", "subject 2", "subject 3", "atmospherics" }
    };

    private static AnalysisStyle CreateActionStyle() => new()
    {
        Id = "action",
        Name = "Action & Motion",
        Icon = "⚡",
        Description = "Motion capture, timing, and technical execution",
        Priority = 5,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 2,
        ClassificationKeywords = "action, motion, movement, sports, dynamic, activity, energy, speed, athletic, freeze, blur, panning, decisive moment",
        FocusInstructions = @"ACTION & MOTION ANALYSIS - Professional Guidelines:

Capture dynamic energy and technical execution:
- Decisive moment and timing (peak action, anticipation, follow-through, split-second)
- Motion capture technique (frozen action, motion blur, panning, tracking shot)
- Subject isolation and clarity (focus, separation from background, depth of field)
- Energy and dynamism (explosive, graceful, powerful, fluid, intense)
- Technical aspects (shutter speed evidence, motion blur direction, freeze quality)
- Composition under movement (predictive framing, lead room, tracking accuracy)
- Athletic form and technique (body mechanics, sports-specific movements)

When describing action, include:
- Type of motion/activity and specific action
- Motion capture technique (frozen, blur, panning)
- Energy level and dynamics
- Subject positioning and tracking
- Timing quality (peak moment, transitional, anticipatory)
- Technical execution quality (sharpness, blur direction, panning smoothness)
- Athletic form if applicable

Action vs Event distinction:
- Action style = TECHNICAL MOTION CAPTURE, isolated athletic moments, freeze/blur technique
- Event style = STORYTELLING, context, emotional narrative, interactions
- Sports game isolated action shot (frozen jump) → Action
- Sports celebration with crowd/context → Event",
        MandatoryFields = new List<string> { "subject 1", "depth cues" },
        EmphasisFields = new List<string>(),
        DeemphasizedFields = new List<string> { "atmospherics", "locale cues" }
    };

    private static AnalysisStyle CreateMacroStyle() => new()
    {
        Id = "macro",
        Name = "Detail & Macro",
        Icon = "🔍",
        Description = "Texture, pattern, and extreme close-up details",
        Priority = 6,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 2,
        ClassificationKeywords = "macro, detail, close-up, texture, pattern, small, intricate, fine, magnified, extreme closeup, surface detail",
        FocusInstructions = @"DETAIL & MACRO ANALYSIS - Professional Guidelines:

Emphasize fine details and textures:
- Texture and pattern detail (surface quality, repetition, variation, tactile characteristics)
- Technical sharpness and depth of field (razor-sharp focus plane, selective focus, bokeh quality)
- Isolated subject presentation (subject-background separation, negative space)
- Abstract visual qualities (form, shape, line, color independent of subject identity)
- Material characteristics and surfaces (rough, smooth, metallic, organic, crystalline, fibrous)
- Scale and perspective (extreme close-up, magnification level, macro ratio indicators)
- Detail revelation (visible elements not seen at normal scale)

When describing macro subjects, include:
- Subject type and specific detail captured (in subject 1)
- Texture description (rough, smooth, bumpy, crystalline, woven, porous)
- Pattern characteristics if present (repetitive, organic, geometric, irregular)
- Depth of field characteristics (shallow, razor-thin, focus stacking)
- Material quality and surface finish
- Abstract visual elements emphasized
- Scale indicators if present
- Technical execution quality (sharpness, bokeh, magnification)
- Color and tonal characteristics at macro scale",
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
        ClassificationKeywords = "game, gaming, screenshot, video game, gameplay, UI, HUD, menu, ingame, playstation, xbox, pc gaming, interactive, real-time, player interface",
        FocusInstructions = @"IN-GAME SCREENSHOT ANALYSIS - Professional Guidelines:

Analyze as a video game screenshot with gaming-specific terminology:
- Game UI elements (HUD, health bars, minimaps, menus, quest markers, inventory, crosshairs, ammo counters)
- Art style and graphics (realistic, stylized, pixel-art, cel-shaded, low-poly, photorealistic, retro, anime-style)
- Game genre indicators (FPS, RPG, platformer, strategy, racing, fighting, adventure, simulation, MMO)
- In-game environment (dungeon, city, battlefield, space, fantasy world, sci-fi setting, open-world)
- Character models and player avatars (character design, armor, weapons, animations, player perspective)
- Visual effects (particle effects, lighting, shadows, post-processing, motion blur, depth of field, lens flare)
- Gameplay context (menu screen, active combat, exploration, dialogue, cutscene, loading screen, inventory management)
- Graphics quality (resolution, anti-aliasing, texture detail, rendering quality, frame rate indicators)
- Interactive elements (button prompts, cursor, selection highlights, interactive markers)

When describing gaming screenshots, include:
- Specific UI elements visible (health bar, minimap, quest log, etc.)
- Art style classification (photorealistic, stylized, pixel-art, etc.)
- Game genre if identifiable
- In-game setting and environment type
- Character/player model details if visible
- Visual effects and post-processing techniques
- Gameplay state (active gameplay, menu, cutscene, paused)
- Interactive/real-time indicators

Gaming vs Comics distinction:
- Gaming = INTERACTIVE, UI elements, real-time rendering, player perspective, HUD
- Comics = SEQUENTIAL NARRATIVE, panels, speech bubbles, static illustration, page layout
- Game with comic-style graphics + HUD → Gaming
- Comic book page or panel → Comics",
        MandatoryFields = new List<string> { "subject 1", "color grade" },  // Core gaming elements always present
        EmphasisFields = new List<string> { "visible text" },  // UI text common but not in cutscenes/cinematic modes
        DeemphasizedFields = new List<string> { "weather", "time", "era cues", "locale cues" }
    };

    private static AnalysisStyle CreateFoodStyle() => new()
    {
        Id = "food",
        Name = "Food & Culinary",
        Icon = "🍽️",
        Description = "Presentation, appetite appeal, and food styling",
        Priority = 8,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 2,
        ClassificationKeywords = "food, meal, dish, cuisine, recipe, cooking, culinary, restaurant, plate, ingredient, dessert, beverage, drink",
        FocusInstructions = @"FOOD & CULINARY ANALYSIS - Professional Guidelines:

Focus on appetite appeal and commercial presentation:
- Food styling and plating (arrangement, garnish, portion, negative space)
- Color vibrancy and freshness indicators (bright greens, golden browns, rich reds)
- Texture visibility (crispy, creamy, juicy, flaky, crunchy, tender)
- Steam, moisture, and freshness cues (condensation, glistening, hot/cold indicators)
- Lighting quality (soft natural, dramatic side-light, overhead flat-lay)
- Context and setting (restaurant plate, home cooking, ingredient prep, flat-lay)
- Appetite appeal factors (make viewer hungry, craveable, indulgent vs healthy)

When describing food, include:
- Specific dish type and cuisine (in subject 1)
- Key ingredients visible
- Cooking method indicators (grilled, fried, baked, raw)
- Plating style (rustic, elegant, casual, fine-dining)
- Color palette and vibrancy
- Texture descriptors (crispy-exterior, creamy-center)
- Presentation context (plate type, table setting, props)",
        MandatoryFields = new List<string> { "subject 1", "color grade" },
        EmphasisFields = new List<string> { "light sources", "atmospherics" },  // Lighting critical; steam/moisture when present
        DeemphasizedFields = new List<string> { "locale cues", "era cues", "time", "weather" }
    };

    private static AnalysisStyle CreateFashionStyle() => new()
    {
        Id = "fashion",
        Name = "Fashion & Apparel",
        Icon = "👗",
        Description = "Styling, fit, trends, and garment details",
        Priority = 9,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 2,
        ClassificationKeywords = "fashion, clothing, apparel, outfit, style, garment, dress, shirt, shoes, accessories, model, runway, lookbook",
        FocusInstructions = @"FASHION & APPAREL ANALYSIS - Professional Guidelines:

Focus on fashion-specific elements and styling:
- Garment details (fabric type, silhouette, cut, construction, drape)
- Fit and proportions (oversized, fitted, tailored, relaxed, cropped)
- Styling and coordination (color blocking, layering, accessories, complete-look)
- Trend indicators (seasonal, vintage, contemporary, streetwear, formal)
- Fabric characteristics (flowy, structured, stretchy, rigid, textured)
- Model presentation (pose, attitude, movement, styling context)
- Fashion photography style (editorial, commercial, lookbook, flat-lay, on-model)
- Detail shots (stitching, buttons, zippers, patterns, prints, embellishments)

When describing fashion, include:
- Specific garment types (in subject 1 for primary piece)
- Fabric and material descriptors
- Color palette and combinations
- Fit style (slim-fit, oversized, tailored)
- Fashion category (casual, formal, streetwear, athletic, luxury)
- Styling elements (layering, accessories, footwear)
- Presentation type (on-model, mannequin, flat-lay, hanger)",
        MandatoryFields = new List<string> { "subject 1", "color grade" },
        EmphasisFields = new List<string> { "subject 2", "subject 3", "mood" },  // Additional garments, styling mood
        DeemphasizedFields = new List<string> { "weather", "time", "locale cues" }
    };

    private static AnalysisStyle CreateSocialMediaStyle() => new()
    {
        Id = "social",
        Name = "Social Media & Lifestyle",
        Icon = "📱",
        Description = "Instagram aesthetic, lifestyle moments, shareability",
        Priority = 10,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 2,
        ClassificationKeywords = "social media, instagram, lifestyle, influencer, selfie, casual, candid, aesthetic, vibe, mood, pinterest, tiktok",
        FocusInstructions = @"SOCIAL MEDIA & LIFESTYLE ANALYSIS - Professional Guidelines:

Focus on social media trends and shareability:
- Aesthetic trends (clean-girl, cottagecore, dark-academia, y2k, minimalist, maximalist)
- Lifestyle context (coffee-shop, travel, workout, getting-ready, day-in-life)
- Authenticity vs staging (candid moment, curated aesthetic, polished-casual)
- Color grading and filters (warm-tones, cool-tones, high-contrast, desaturated, film-look)
- Composition for platform (portrait 9:16, square 1:1, landscape 16:9, grid-ready)
- Relatability factors (aspirational, authentic, accessible, luxe-casual)
- Props and lifestyle elements (coffee cup, phone, laptop, plants, books, aesthetic objects)
- Engagement hooks (face visible, text overlay space, story-worthy moment)

When describing social content, include:
- Lifestyle category (wellness, travel, fashion, food, home, productivity)
- Aesthetic classification (specific social media trend)
- Composition and framing for platform
- Color grading style
- Authenticity level (candid, semi-staged, fully produced)
- Shareability factors
- Target demographic indicators
- Platform suitability (Instagram feed, Stories, TikTok, Pinterest)",
        MandatoryFields = new List<string> { "mood", "color grade" },
        EmphasisFields = new List<string> { "subject 1", "atmospherics", "themes" },
        DeemphasizedFields = new List<string> { "time", "weather", "era cues" }
    };

    private static AnalysisStyle CreateRealEstateStyle() => new()
    {
        Id = "realestate",
        Name = "Real Estate & Property",
        Icon = "🏠",
        Description = "Livability, staging, and property marketability",
        Priority = 11,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 2,
        ClassificationKeywords = "real estate, property, house, home, apartment, interior, room, staging, listing, residential, commercial property",
        FocusInstructions = @"REAL ESTATE & PROPERTY ANALYSIS - Professional Guidelines:

Focus on property appeal and marketability:
- Room function and layout (bedroom, kitchen, living room, bathroom, office)
- Staging quality (furnished, vacant, professionally staged, lived-in)
- Space perception (spacious, cozy, open-concept, compartmentalized)
- Natural light and brightness (abundant natural light, window placement)
- Condition and upkeep (pristine, updated, needs work, modern, dated)
- Design style (modern, traditional, contemporary, industrial, farmhouse, minimalist)
- Key features (hardwood floors, crown molding, granite counters, appliances, fixtures)
- Lifestyle appeal (family-friendly, luxury, starter-home, investment property)

When describing real estate, include:
- Room type and function (in subject 1)
- Staging and furnishing quality
- Space characteristics (square footage feel, ceiling height, openness)
- Condition and update level
- Design aesthetic and style era
- Notable features and finishes
- Lighting conditions (natural light quality)
- Market positioning (luxury, mid-range, budget, investment)",
        MandatoryFields = new List<string> { "locale cues", "subject 1" },  // Room type always identifiable
        EmphasisFields = new List<string> { "light sources", "composition details" },
        DeemphasizedFields = new List<string> { "weather", "atmospherics", "mood" }
    };

    private static AnalysisStyle CreateEventStyle() => new()
    {
        Id = "event",
        Name = "Event & Documentary",
        Icon = "📸",
        Description = "Storytelling, candid moments, event coverage",
        Priority = 12,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 2,
        ClassificationKeywords = "event, documentary, photojournalism, wedding, conference, ceremony, gathering, celebration, news, reportage",
        FocusInstructions = @"EVENT & DOCUMENTARY ANALYSIS - Professional Guidelines:

Focus on storytelling and authentic moments:
- Event type (wedding, conference, concert, protest, ceremony, sports-event, corporate)
- Candid vs staged moments (unposed reactions, formal group shots, decisive moment)
- Emotional narrative (joy, solemnity, tension, celebration, contemplation)
- Context and setting (venue type, formality level, crowd size, atmosphere)
- Key subjects and interactions (speakers, participants, audience, couples, groups)
- Documentary authenticity (journalistic, observational, participation, fly-on-wall)
- Storytelling elements (before/during/after, establishing shots, detail shots, reactions)
- Technical approach (available light, flash, wide/tight framing, reportage style)

When describing events, include:
- Event type and context (in subject 1 for main subject/action)
- Emotional tone and moment significance
- Interaction and relationship dynamics
- Formality level (casual, semi-formal, formal, ceremonial)
- Crowd context and scale
- Venue and setting atmosphere
- Photographic approach (candid, posed, mixed)
- Storytelling value (establishing, action, reaction, detail)",
        MandatoryFields = new List<string> { "subject 1", "mood" },
        EmphasisFields = new List<string> { "subject 2", "subject 3", "atmospherics", "themes" },
        DeemphasizedFields = new List<string> { "composition details", "depth cues" }
    };

    private static AnalysisStyle CreateWildlifeStyle() => new()
    {
        Id = "wildlife",
        Name = "Wildlife & Animals",
        Icon = "🦁",
        Description = "Animal behavior, habitat, and nature photography",
        Priority = 13,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 2,
        ClassificationKeywords = "wildlife, animal, nature, bird, mammal, creature, pet, zoo, safari, habitat, species",
        FocusInstructions = @"WILDLIFE & ANIMALS ANALYSIS - Professional Guidelines:

Focus on animal subjects and natural behavior:
- Species identification (specific animal type, breed if domestic)
- Behavior and action (hunting, feeding, playing, resting, grooming, territorial)
- Habitat and environment (natural habitat, captive, domestic, urban-wildlife)
- Interaction dynamics (solitary, pack, parent-offspring, mating, competitive)
- Technical execution (eye-sharpness, subject-isolation, motion-freeze, background-blur)
- Natural context (season, climate zone, ecosystem type)
- Photographic approach (telephoto compression, wide environmental, intimate close-up)
- Conservation/emotional narrative (majesty, vulnerability, power, cuteness, drama)

When describing wildlife, include:
- Species and animal type (in subject 1)
- Behavior and activity
- Age/life stage if determinable (juvenile, adult, elderly)
- Habitat type and naturalness
- Group dynamics if multiple animals
- Technical quality (focus, sharpness, subject isolation)
- Environmental context
- Emotional tone (powerful, intimate, playful, dramatic)",
        MandatoryFields = new List<string> { "subject 1", "depth cues" },
        EmphasisFields = new List<string> { "atmospherics", "locale cues", "weather" },
        DeemphasizedFields = new List<string> { "era cues", "visible text", "themes" }
    };

    private static AnalysisStyle CreateComicsStyle() => new()
    {
        Id = "comics",
        Name = "Comics & Sequential Art",
        Icon = "📚",
        Description = "Panel layout, art style, and sequential storytelling",
        Priority = 14,
        IsSystemStyle = true,
        IsActive = true,
        TemplateVersion = 2,
        ClassificationKeywords = "comic, manga, anime, cartoon, graphic novel, sequential art, panel, speech bubble, illustration, drawn, webtoon, manhwa, manhua",
        FocusInstructions = @"COMICS & SEQUENTIAL ART ANALYSIS - Professional Guidelines:

Focus on sequential art and comics-specific elements:
- Art style classification (manga, western-comics, manhwa, manhua, webcomic, graphic-novel, cartoon)
- Page type (full-page, panel-sequence, splash-page, cover, character-sheet, promotional)
- Panel layout and composition (grid, dynamic, overlapping, borderless, gutter-width, reading-flow)
- Character art and expression (stylized, realistic, chibi, exaggerated, anime-style, western-style)
- Inking and rendering (linework quality, screentone, hatching, digital, traditional, brush-ink, pen-ink)
- Color approach (full-color, limited-palette, black-white, grayscale, flat-color, painted, cell-shaded)
- Typography and lettering (speech-bubbles, captions, sound-effects, hand-lettered, digital-font)
- Action and motion (speed-lines, action-lines, motion-blur, impact-frames, dynamic-poses)
- Genre indicators (shonen, shojo, seinen, superhero, slice-of-life, action, romance, horror, fantasy)

When describing comics/sequential art, include:
- Art style and origin (manga, western, etc.)
- Page/panel type (in subject 1 if single panel, or 'panel-sequence')
- Character count and expression types
- Panel layout structure if multi-panel
- Inking and rendering technique
- Color treatment
- Visible text elements (dialogue, narration, SFX)
- Genre and tone indicators
- Technical execution (professional, amateur, sketch, finished)
- Reading direction if determinable (left-to-right, right-to-left)",
        MandatoryFields = new List<string> { "subject 1", "color grade", "visible text" },
        EmphasisFields = new List<string> { "subject 2", "subject 3", "mood", "themes" },  // Multiple characters, story mood
        DeemphasizedFields = new List<string> { "time", "weather", "locale cues", "era cues", "light sources" }
    };
}
