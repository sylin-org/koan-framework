using S6.SnapVault.Models;

namespace S6.SnapVault.Services.AI;

/// <summary>
/// Factory for assembling AI analysis prompts from base template + style customizations
/// Implements separation of concerns: base prompt in code (version-controlled),
/// style parameters in database (user-configurable)
/// </summary>
public interface IAnalysisPromptFactory
{
    /// <summary>
    /// Render base prompt with no customizations (default behavior)
    /// Returns the rigorous JSON-structured prompt verbatim
    /// </summary>
    string RenderPrompt();

    /// <summary>
    /// Render prompt customized for specific analysis style
    /// Assembles: FocusInstructions + Base + EnhancedExamples + RequiredFacts
    /// </summary>
    /// <param name="style">Style configuration with customization parameters</param>
    string RenderPromptFor(AnalysisStyle style);

    /// <summary>
    /// Get classification prompt for smart mode two-stage analysis
    /// Generates: "Which style best matches this image: [list of styles]"
    /// </summary>
    /// <param name="availableStyles">Active non-smart styles to choose from</param>
    string GetClassificationPrompt(IEnumerable<AnalysisStyle> availableStyles);

    /// <summary>
    /// Apply variable substitution to assembled prompt
    /// Replaces: {{width}}, {{height}}, {{camera}}, {{orientation}}
    /// </summary>
    string SubstituteVariables(string prompt, PhotoContext context);
}

/// <summary>
/// Photo context for prompt variable substitution
/// </summary>
public record PhotoContext(
    string PhotoId,
    int Width,
    int Height,
    double AspectRatio,
    string? CameraModel,
    DateTime? CapturedAt,
    Dictionary<string, string>? ExifData
);

/// <summary>
/// Minimal DTO for API responses (frontend dropdown)
/// </summary>
public record AnalysisStyleDefinition(
    string Id,
    string Label,
    string Icon,
    string Description,
    int Priority
);
