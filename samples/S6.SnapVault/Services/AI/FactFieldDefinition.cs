namespace S6.SnapVault.Services.AI;

/// <summary>
/// Defines a fact field with its key, example values, and rendering options
/// Used to build JSON structure dynamically based on style requirements
/// </summary>
public class FactFieldDefinition
{
    public string Key { get; init; } = "";
    public string[] ExampleValues { get; init; } = Array.Empty<string>();
    public bool IsAlwaysMandatory { get; init; } = false;

    /// <summary>
    /// Renders this field as a JSON line
    /// </summary>
    public string RenderAsJsonLine(bool commented = false)
    {
        var values = string.Join(",", ExampleValues.Select(v => $"\"{v}\""));
        var line = $"    \"{Key}\": [{values}]";

        if (commented)
        {
            return $"    // {line.TrimStart()}";
        }

        return line;
    }

    /// <summary>
    /// Renders field with enhanced examples
    /// </summary>
    public string RenderAsJsonLine(string[] enhancedValues, bool commented = false)
    {
        var values = string.Join(",", enhancedValues.Select(v => $"\"{v}\""));
        var line = $"    \"{Key}\": [{values}]";

        if (commented)
        {
            return $"    // {line.TrimStart()}";
        }

        return line;
    }
}

/// <summary>
/// Registry of all available fact fields with their default examples
/// Single source of truth for fact field definitions
/// </summary>
public static class FactFieldRegistry
{
    // ==================== Base Mandatory Fields (always required) ====================

    public static readonly FactFieldDefinition Type = new()
    {
        Key = "type",
        ExampleValues = new[] { "portrait", "landscape", "still-life", "product", "food", "ingame-screenshot", "architecture", "wildlife", "macro", "abstract", "other" },
        IsAlwaysMandatory = true
    };

    public static readonly FactFieldDefinition Style = new()
    {
        Key = "style",
        ExampleValues = new[] { "photography", "painting", "digital-art", "illustration", "3d-render", "pixel-art", "cel-shaded", "game-graphics", "other" },
        IsAlwaysMandatory = true
    };

    public static readonly FactFieldDefinition SubjectCount = new()
    {
        Key = "subject count",
        ExampleValues = new[] { "no subjects", "1 person", "2 people", "3+ people", "single object", "multiple items", "animals" },
        IsAlwaysMandatory = true
    };

    public static readonly FactFieldDefinition Composition = new()
    {
        Key = "composition",
        ExampleValues = new[] { "centered", "rule-of-thirds", "symmetrical", "diagonal", "leading-lines", "framed", "off-center", "close-up", "wide" },
        IsAlwaysMandatory = true
    };

    public static readonly FactFieldDefinition Palette = new()
    {
        Key = "palette",
        ExampleValues = new[] { "color1", "color2", "color3" },
        IsAlwaysMandatory = true
    };

    public static readonly FactFieldDefinition Lighting = new()
    {
        Key = "lighting",
        ExampleValues = new[] { "overcast", "golden-hour", "studio", "natural", "soft", "dramatic", "backlit", "low-key", "high-key", "neon", "spotlit" },
        IsAlwaysMandatory = true
    };

    public static readonly FactFieldDefinition Setting = new()
    {
        Key = "setting",
        ExampleValues = new[] { "indoor", "outdoor", "studio", "urban", "nature" },
        IsAlwaysMandatory = true
    };

    public static readonly FactFieldDefinition Mood = new()
    {
        Key = "mood",
        ExampleValues = new[] { "mysterious", "cheerful", "serene", "dramatic", "playful", "somber", "energetic", "contemplative", "romantic", "tense" },
        IsAlwaysMandatory = true
    };

    public static readonly FactFieldDefinition Themes = new()
    {
        Key = "themes",
        ExampleValues = new[] { "minimalist", "vintage", "retro", "modern", "rustic", "industrial", "bohemian", "film-noir", "cyberpunk", "y2k", "cottagecore", "art-deco" },
        IsAlwaysMandatory = true
    };

    public static readonly FactFieldDefinition FashionStyle = new()
    {
        Key = "fashion-style",
        ExampleValues = new[] { "streetwear", "formal", "casual", "athletic", "business-casual", "evening-wear", "workwear", "bohemian", "preppy", "punk", "goth", "vintage", "minimalist", "avant-garde", "traditional" },
        IsAlwaysMandatory = true
    };

    public static readonly FactFieldDefinition CulturalStyle = new()
    {
        Key = "cultural-style",
        ExampleValues = new[] { "western", "eastern", "japanese-street", "korean-street", "scandinavian", "mediterranean", "african", "latin-american", "middle-eastern", "indigenous", "traditional-dress", "cultural-fusion" },
        IsAlwaysMandatory = true
    };

    // ==================== Optional Fields (style-dependent) ====================

    public static readonly FactFieldDefinition Subject1 = new()
    {
        Key = "subject 1",
        ExampleValues = new[] { "person", "joyful-expression", "black-hoodie", "red-headphones", "relaxed-pose", "looking-left", "confident-demeanor", "short-dark-hair" }
    };

    public static readonly FactFieldDefinition Subject2 = new()
    {
        Key = "subject 2",
        ExampleValues = new[] { "person", "describe-if-second-person-visible" }
    };

    public static readonly FactFieldDefinition Subject3 = new()
    {
        Key = "subject 3",
        ExampleValues = new[] { "person", "describe-if-third-person-visible" }
    };

    public static readonly FactFieldDefinition EraCues = new()
    {
        Key = "era cues",
        ExampleValues = new[] { "1920s", "1960s", "1980s", "2000s", "art-deco", "mid-century", "vintage", "retro", "contemporary" }
    };

    public static readonly FactFieldDefinition ColorGrade = new()
    {
        Key = "color grade",
        ExampleValues = new[] { "black-and-white", "sepia", "teal-orange", "cool", "warm", "neutral", "monochrome", "duotone", "desaturated", "vibrant" }
    };

    public static readonly FactFieldDefinition LightSources = new()
    {
        Key = "light sources",
        ExampleValues = new[] { "sun", "neon-signs", "led-panels", "candles", "firelight", "streetlamps" }
    };

    public static readonly FactFieldDefinition DepthCues = new()
    {
        Key = "depth cues",
        ExampleValues = new[] { "bokeh", "shallow-focus", "deep-focus", "motion-blur", "rack-focus" }
    };

    public static readonly FactFieldDefinition Atmospherics = new()
    {
        Key = "atmospherics",
        ExampleValues = new[] { "fog", "haze", "smoke", "rain", "snow", "sparks", "god-rays", "dust", "mist" }
    };

    public static readonly FactFieldDefinition LocaleCues = new()
    {
        Key = "locale cues",
        ExampleValues = new[] { "architecture-type", "region-specific-props", "local-vegetation" }
    };

    public static readonly FactFieldDefinition Time = new()
    {
        Key = "time",
        ExampleValues = new[] { "day", "night", "sunset", "sunrise", "twilight", "midday" }
    };

    public static readonly FactFieldDefinition Weather = new()
    {
        Key = "weather",
        ExampleValues = new[] { "clear", "overcast", "rainy", "snowy", "foggy", "indoor" }
    };

    public static readonly FactFieldDefinition VisibleText = new()
    {
        Key = "visible text",
        ExampleValues = new[] { "exact text if readable" }
    };

    // ==================== Field Collections ====================

    /// <summary>
    /// All base mandatory fields (always required in every analysis)
    /// </summary>
    public static readonly FactFieldDefinition[] BaseMandatoryFields = new[]
    {
        Type, Style, SubjectCount, Composition, Palette, Lighting, Setting, Mood, Themes, FashionStyle, CulturalStyle
    };

    /// <summary>
    /// All optional fields (available for style-specific promotion to mandatory)
    /// </summary>
    public static readonly Dictionary<string, FactFieldDefinition> OptionalFields = new()
    {
        ["subject 1"] = Subject1,
        ["subject 2"] = Subject2,
        ["subject 3"] = Subject3,
        ["era cues"] = EraCues,
        ["color grade"] = ColorGrade,
        ["light sources"] = LightSources,
        ["depth cues"] = DepthCues,
        ["atmospherics"] = Atmospherics,
        ["locale cues"] = LocaleCues,
        ["time"] = Time,
        ["weather"] = Weather,
        ["visible text"] = VisibleText
    };

    /// <summary>
    /// Get field definition by key
    /// </summary>
    public static FactFieldDefinition? GetField(string key)
    {
        // Check base mandatory first
        var baseMandatory = BaseMandatoryFields.FirstOrDefault(f => f.Key == key);
        if (baseMandatory != null) return baseMandatory;

        // Check optional
        return OptionalFields.TryGetValue(key, out var field) ? field : null;
    }
}
