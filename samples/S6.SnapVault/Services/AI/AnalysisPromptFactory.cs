using S6.SnapVault.Models;
using System.Text;

namespace S6.SnapVault.Services.AI;

/// <summary>
/// Factory for assembling AI analysis prompts using template-based approach
/// Base prompt template is version-controlled in code
/// Style customizations applied via field collections (stored in database)
/// </summary>
public class AnalysisPromptFactory : IAnalysisPromptFactory
{
    private readonly ILogger<AnalysisPromptFactory> _logger;

    // Base prompt template - Single Source of Truth
    private const string PROMPT_TEMPLATE = @"Analyze the image and output ONLY valid JSON (no markdown, no comments). Describe ONLY what is clearly visible—never guess. Use concise, concrete language.

Guidelines:
- ""tags"": 6–10 searchable keywords; lowercase; hyphenate multi-word terms (e.g., ""red-hoodie"", ""neon-lights""); include evident visual elements, clothing styles if present, and aesthetic cues (e.g., ""b&w"", ""minimalist"", ""vintage"").
- ""summary"": single sentence with concrete visual facts + evident aesthetic cues.
- ""facts"": ALL keys MUST be lowercase (e.g., ""type"", ""style"", ""subject count""). ALL values MUST be arrays, even single values, to enable uniform filtering. Each fact CAN have multiple entries; examples are non-exhaustive, complement the fact's list as necessary.
- Add optional fact fields ONLY when clearly visible; omit otherwise.
- Escape all strings properly; return the JSON object only.

{{FOCUS_INSTRUCTIONS}}

Return JSON in this format:
{
  ""tags"": [""tag1"",""tag2"",""tag3""],
  ""summary"": ""30–80 words describing subject, action, setting, lighting, and any evident aesthetics/themes."",
  ""facts"": {
{{MANDATORY_FACTS}}

{{OPTIONAL_FACTS}}
  }
}

IMPORTANT:
- All fact keys MUST be lowercase. All fact values MUST be arrays, even single items. Example: ""type"": [""portrait""], not ""Type"": ""portrait"".
- For clothing/fashion: Be specific and descriptive (e.g., ""black-leather-jacket"", ""white-button-down"", ""red-sneakers""). Only use ""fashion-style"" or ""cultural-style"" facts when the overall style is CLEARLY and CONFIDENTLY identifiable.
- Avoid labeling generic clothing as specific subcultures. A black outfit is not automatically ""goth""; casual streetwear is not automatically ""japanese-street"". Use precise descriptors instead of broad style categories unless absolutely certain.";

    public AnalysisPromptFactory(ILogger<AnalysisPromptFactory> logger)
    {
        _logger = logger;
    }

    public string RenderPrompt()
    {
        return RenderPromptFor(null);
    }

    public string RenderPromptFor(AnalysisStyle? style)
    {
        // Escape hatch: If user provides full override, use it
        if (style != null && !string.IsNullOrEmpty(style.FullPromptOverride))
        {
            _logger.LogDebug("Using full prompt override for style '{StyleName}'", style.Name);
            return style.FullPromptOverride;
        }

        var prompt = PROMPT_TEMPLATE;

        // 1. Inject focus instructions (prepended context)
        var focusInstructions = style?.FocusInstructions ?? "";
        if (!string.IsNullOrEmpty(focusInstructions))
        {
            focusInstructions = focusInstructions + "\n";
        }
        prompt = prompt.Replace("{{FOCUS_INSTRUCTIONS}}", focusInstructions);

        // 2. Build mandatory facts section
        var mandatoryFacts = BuildMandatoryFactsSection(style);
        prompt = prompt.Replace("{{MANDATORY_FACTS}}", mandatoryFacts);

        // 3. Build optional facts section
        var optionalFacts = BuildOptionalFactsSection(style);
        prompt = prompt.Replace("{{OPTIONAL_FACTS}}", optionalFacts);

        return prompt;
    }

    public string GetClassificationPrompt(IEnumerable<AnalysisStyle> availableStyles)
    {
        var styleList = string.Join("\n", availableStyles
            .OrderBy(s => s.Priority)
            .Select(s => $"- {s.Name.ToLower()}: {s.Description}"));

        return $@"Analyze this image and determine which photography style best matches it.
Respond with ONLY the style name from this list:

{styleList}

Return ONLY the style name (lowercase), no explanation.";
    }

    public string SubstituteVariables(string prompt, PhotoContext context)
    {
        return prompt
            .Replace("{{photoId}}", context.PhotoId)
            .Replace("{{width}}", context.Width.ToString())
            .Replace("{{height}}", context.Height.ToString())
            .Replace("{{aspectRatio}}", context.AspectRatio.ToString("F2"))
            .Replace("{{camera}}", context.CameraModel ?? "Unknown")
            .Replace("{{orientation}}", GetOrientation(context.AspectRatio));
    }

    /// <summary>
    /// Build mandatory facts section from base mandatory + style-specific mandatory fields
    /// </summary>
    private string BuildMandatoryFactsSection(AnalysisStyle? style)
    {
        var lines = new List<string>();

        // 1. Always include base mandatory fields
        foreach (var field in FactFieldRegistry.BaseMandatoryFields)
        {
            // Check if style provides enhanced examples for this field
            var enhancedExamples = GetEnhancedExamples(field.Key, style);
            if (enhancedExamples != null)
            {
                lines.Add(field.RenderAsJsonLine(enhancedExamples) + ",");
            }
            else
            {
                lines.Add(field.RenderAsJsonLine() + ",");
            }
        }

        // 2. Add style-specific mandatory fields (promoted from optional)
        if (style?.MandatoryFields != null)
        {
            foreach (var fieldKey in style.MandatoryFields)
            {
                var field = FactFieldRegistry.GetField(fieldKey);
                if (field != null && !field.IsAlwaysMandatory)
                {
                    var enhancedExamples = GetEnhancedExamples(field.Key, style);
                    if (enhancedExamples != null)
                    {
                        lines.Add(field.RenderAsJsonLine(enhancedExamples) + ",");
                    }
                    else
                    {
                        lines.Add(field.RenderAsJsonLine() + ",");
                    }
                }
            }
        }

        // Remove trailing comma from last line
        if (lines.Any())
        {
            var lastLine = lines[^1];
            lines[^1] = lastLine.TrimEnd(',');
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Build optional facts section (commented examples)
    /// Excludes de-emphasized fields and mandatory fields
    /// Highlights emphasis fields
    /// </summary>
    private string BuildOptionalFactsSection(AnalysisStyle? style)
    {
        var lines = new List<string>
        {
            "    // Optional facts (arrays; only if clearly visible, omit otherwise):"
        };

        var mandatoryKeys = style?.MandatoryFields?.ToHashSet() ?? new HashSet<string>();
        var emphasizedKeys = style?.EmphasisFields?.ToHashSet() ?? new HashSet<string>();
        var deemphasizedKeys = style?.DeemphasizedFields?.ToHashSet() ?? new HashSet<string>();

        foreach (var kvp in FactFieldRegistry.OptionalFields)
        {
            var fieldKey = kvp.Key;
            var field = kvp.Value;

            // Skip if already in mandatory
            if (mandatoryKeys.Contains(fieldKey))
                continue;

            // Skip if de-emphasized
            if (deemphasizedKeys.Contains(fieldKey))
                continue;

            // Add with enhanced examples if emphasized
            var enhancedExamples = emphasizedKeys.Contains(fieldKey)
                ? GetEnhancedExamples(fieldKey, style)
                : null;

            if (enhancedExamples != null)
            {
                lines.Add(field.RenderAsJsonLine(enhancedExamples, commented: true));
            }
            else
            {
                lines.Add(field.RenderAsJsonLine(commented: true));
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Get enhanced example values for specific fields based on style
    /// Returns null if no enhancements for this field
    /// </summary>
    private string[]? GetEnhancedExamples(string fieldKey, AnalysisStyle? style)
    {
        if (style == null) return null;

        // Composition enhancements (Landscape, Architecture)
        if (fieldKey == "composition" && style.EmphasisFields?.Contains("composition details") == true)
        {
            return new[] { "centered", "rule-of-thirds", "symmetrical", "diagonal", "leading-lines", "framed", "off-center", "close-up", "wide", "foreground-interest", "depth-layers", "golden-ratio" };
        }

        // Lighting enhancements (Product)
        if (fieldKey == "lighting" && style.EmphasisFields?.Contains("lighting setup") == true)
        {
            return new[] { "studio", "three-point-lit", "soft", "even", "product-lit", "rim-light", "key-light", "fill-light", "diffused" };
        }

        // Atmospherics enhancements (Landscape, Macro)
        if (fieldKey == "atmospherics" && style.EmphasisFields?.Contains("atmospherics") == true)
        {
            return new[] { "fog", "haze", "mist", "clouds", "rain", "snow", "god-rays", "dust", "morning-dew" };
        }

        return null;
    }

    private static string GetOrientation(double aspectRatio)
    {
        if (aspectRatio > 1.3) return "landscape";
        if (aspectRatio < 0.8) return "portrait";
        return "square";
    }
}
