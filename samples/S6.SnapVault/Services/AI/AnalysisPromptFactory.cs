using S6.SnapVault.Models;
using System.Text;

namespace S6.SnapVault.Services.AI;

/// <summary>
/// Factory for assembling AI analysis prompts
/// Base prompt is protected constant (version-controlled in code)
/// Style customizations applied via parameter injection (stored in database)
/// </summary>
public class AnalysisPromptFactory : IAnalysisPromptFactory
{
    private readonly ILogger<AnalysisPromptFactory> _logger;

    // Base prompt - Single Source of Truth (user's original high-quality prompt)
    private const string BASE_PROMPT = @"Analyze the image and output ONLY valid JSON (no markdown, no comments). Describe ONLY what is clearly visible—never guess. Use concise, concrete language.

Guidelines:
- ""tags"": 6–10 searchable keywords; lowercase; hyphenate multi-word terms (e.g., ""red-hoodie"", ""neon-lights""); include evident visual elements, clothing styles if present, and aesthetic cues (e.g., ""b&w"", ""minimalist"", ""vintage"").
- ""summary"": single sentence with concrete visual facts + evident aesthetic cues.
- ""facts"": ALL keys MUST be lowercase (e.g., ""type"", ""style"", ""subject count""). ALL values MUST be arrays, even single values, to enable uniform filtering. Each fact CAN have multiple entries; examples are non-exhaustive, complement the fact's list as necessary.
- Add optional fact fields ONLY when clearly visible; omit otherwise.
- Escape all strings properly; return the JSON object only.

Return JSON in this format:
{
  ""tags"": [""tag1"",""tag2"",""tag3""],
  ""summary"": ""30–80 words describing subject, action, setting, lighting, and any evident aesthetics/themes."",
  ""facts"": {
    ""type"": [""portrait"",""landscape"",""still-life"",""product"",""food"",""ingame-screenshot"",""architecture"",""wildlife"",""macro"",""abstract"",""other""],
    ""style"": [""photography"",""painting"",""digital-art"",""illustration"",""3d-render"",""pixel-art"",""cel-shaded"",""game-graphics"",""other""],
    ""subject count"": [""no subjects"",""1 person"",""2 people"",""3+ people"",""single object"",""multiple items"",""animals""],
    ""composition"": [""centered"",""rule-of-thirds"",""symmetrical"",""diagonal"",""leading-lines"",""framed"",""off-center"",""close-up"",""wide""],
    ""palette"": [""color1"",""color2"",""color3""],
    ""lighting"": [""overcast"",""golden-hour"",""studio"",""natural"",""soft"",""dramatic"",""backlit"",""low-key"",""high-key"",""neon"",""spotlit""],
    ""setting"": [""indoor"",""outdoor"",""studio"",""urban"",""nature""],
    ""mood"": [""mysterious"",""cheerful"",""serene"",""dramatic"",""playful"",""somber"",""energetic"",""contemplative"",""romantic"",""tense""],
    ""themes"": [""minimalist"",""vintage"",""retro"",""modern"",""rustic"",""industrial"",""bohemian"",""film-noir"",""cyberpunk"",""y2k"",""cottagecore"",""art-deco""],
    ""fashion-style"": [""streetwear"",""formal"",""casual"",""athletic"",""business-casual"",""evening-wear"",""workwear"",""bohemian"",""preppy"",""punk"",""goth"",""vintage"",""minimalist"",""avant-garde"",""traditional""],
    ""cultural-style"": [""western"",""eastern"",""japanese-street"",""korean-street"",""scandinavian"",""mediterranean"",""african"",""latin-american"",""middle-eastern"",""indigenous"",""traditional-dress"",""cultural-fusion""],

    // Per-subject facts (arrays; MUST be present if at least one subject is shown):
    // ""subject 1"": [""person"",""black-hoodie"",""smiling"",""looking-left"",""streetwear""],
    // ""subject 2"": [""building"",""brick-facade"",""arched-windows"",""centered""],
    // ""subject 3"": [""tree"",""bare-branches"",""midground""],

    // Optional facts (arrays; only if clearly visible, omit otherwise; 2+ items per fact preferred if applicable):
    // ""era cues"": [""1920s"",""1960s"",""1980s"",""2000s"",""art-deco"",""mid-century"",""vintage"",""retro"",""contemporary""],
    // ""color grade"": [""black-and-white"",""sepia"",""teal-orange"",""cool"",""warm"",""neutral"",""monochrome"",""duotone"",""desaturated"",""vibrant""],
    // ""light sources"": [""sun"",""neon-signs"",""led-panels"",""candles"",""firelight"",""streetlamps""],
    // ""depth cues"": [""bokeh"",""shallow-focus"",""deep-focus"",""motion-blur"",""rack-focus""],
    // ""atmospherics"": [""fog"",""haze"",""smoke"",""rain"",""snow"",""sparks"",""god-rays"",""dust"",""mist""],
    // ""locale cues"": [""architecture-type"",""region-specific-props"",""local-vegetation""],
    // ""time"": [""day"",""night"",""sunset"",""sunrise"",""twilight"",""midday""],
    // ""weather"": [""clear"",""overcast"",""rainy"",""snowy"",""foggy"",""indoor""],
    // ""visible text"": [""exact text if readable""],
    // ""fashion-style"": [""specific-style-if-clearly-identifiable""] // Use only when fashion is prominent and identifiable
    // ""cultural-style"": [""specific-culture-if-clearly-evident""] // Use only when cultural elements are distinctive and visible
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
        return BASE_PROMPT;
    }

    public string RenderPromptFor(AnalysisStyle style)
    {
        // Escape hatch: If user provides full override, use it
        if (!string.IsNullOrEmpty(style.FullPromptOverride))
        {
            _logger.LogDebug("Using full prompt override for style '{StyleName}'", style.Name);
            return style.FullPromptOverride;
        }

        var builder = new StringBuilder();

        // 1. Add style-specific focus instructions (prepended)
        if (!string.IsNullOrEmpty(style.FocusInstructions))
        {
            builder.AppendLine(style.FocusInstructions);
            builder.AppendLine();
        }

        // 2. Apply style enhancements to base prompt
        var enhancedPrompt = ApplyStyleEnhancements(BASE_PROMPT, style);
        builder.Append(enhancedPrompt);

        return builder.ToString();
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

    private string ApplyStyleEnhancements(string basePrompt, AnalysisStyle style)
    {
        var result = basePrompt;

        // Enhance subject examples with clothing/expression details (for portrait style)
        if (style.EnhanceExamples?.Contains("subject clothing") == true)
        {
            result = result.Replace(
                @"""subject 1"": [""person"",""black-hoodie"",""smiling"",""looking-left"",""streetwear""]",
                @"""subject 1"": [""person"",""joyful-expression"",""black-hoodie"",""red-headphones"",""relaxed-pose"",""looking-left"",""confident-demeanor"",""streetwear""]"
            );
        }

        // Enhance subject examples with facial expressions (for portrait style)
        if (style.EnhanceExamples?.Contains("facial expressions") == true)
        {
            result = result.Replace(
                @"""subject 1"": [""person"",""black-hoodie"",""smiling"",""looking-left"",""streetwear""]",
                @"""subject 1"": [""person"",""smiling"",""joyful-expression"",""black-hoodie"",""looking-left""]"
            );
        }

        // Enhance composition examples (for landscape/architecture styles)
        if (style.EnhanceExamples?.Contains("composition details") == true)
        {
            result = result.Replace(
                @"""composition"": [""centered"",""rule-of-thirds"",""symmetrical"",""diagonal"",""leading-lines"",""framed"",""off-center"",""close-up"",""wide""]",
                @"""composition"": [""centered"",""rule-of-thirds"",""symmetrical"",""diagonal"",""leading-lines"",""framed"",""off-center"",""close-up"",""wide"",""foreground-interest"",""depth-layers"",""golden-ratio""]"
            );
        }

        // Enhance lighting examples (for product/studio styles)
        if (style.EnhanceExamples?.Contains("lighting setup") == true)
        {
            result = result.Replace(
                @"""lighting"": [""overcast"",""golden-hour"",""studio"",""natural"",""soft"",""dramatic"",""backlit"",""low-key"",""high-key"",""neon"",""spotlit""]",
                @"""lighting"": [""studio"",""three-point-lit"",""soft"",""even"",""product-lit"",""rim-light"",""key-light"",""fill-light"",""diffused""]"
            );
        }

        // Enhance atmospherics examples (for landscape/nature styles)
        if (style.EnhanceExamples?.Contains("atmospherics") == true)
        {
            result = result.Replace(
                @"""atmospherics"": [""fog"",""haze"",""smoke"",""rain"",""snow"",""sparks"",""god-rays"",""dust""]",
                @"""atmospherics"": [""fog"",""haze"",""mist"",""clouds"",""rain"",""snow"",""god-rays"",""dust"",""morning-dew""]"
            );
        }

        // Add required optional facts reminder
        if (style.RequiredOptionalFacts?.Any() == true)
        {
            var factsHint = $"\n\nIMPORTANT: For this image type, ensure these optional facts are included if visible: {string.Join(", ", style.RequiredOptionalFacts)}";
            result = result.Replace(
                "IMPORTANT: All fact keys MUST be lowercase.",
                $"IMPORTANT: All fact keys MUST be lowercase.{factsHint}"
            );
        }

        // Add omitted facts reminder
        if (style.OmittedOptionalFacts?.Any() == true)
        {
            var omitHint = $" Omit these facts unless highly relevant: {string.Join(", ", style.OmittedOptionalFacts)}";
            result = result.Replace(
                "IMPORTANT: All fact keys MUST be lowercase.",
                $"IMPORTANT: All fact keys MUST be lowercase.{omitHint}"
            );
        }

        return result;
    }

    private static string GetOrientation(double aspectRatio)
    {
        if (aspectRatio > 1.3) return "landscape";
        if (aspectRatio < 0.8) return "portrait";
        return "square";
    }
}
