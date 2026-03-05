namespace Koan.Core.AI;

/// <summary>
/// Service Provider Interface for dynamic AI model recommendations.
/// Implementations provide the best available model for a given AI category
/// based on runtime conditions (e.g., orchestrator recommendations, garden availability).
///
/// Resolution priority in the AI pipeline:
///   1. Explicit <c>ChatOptions.Model</c> on the call
///   2. Ambient <c>AiCategoryScope</c> model override
///   3. <b>IAiModelAdvisor recommendation</b> (this interface)
///   4. Category configuration (<c>Koan:Ai:{Category}:Model</c>)
///   5. Source/member default model
///   6. Hardcoded fallback
///
/// Categories are defined in <see cref="AiCapability"/>: Chat, Embed, Ocr, Vision, etc.
/// Return null to defer to the next resolution step.
/// </summary>
public interface IAiModelAdvisor
{
    /// <summary>
    /// Returns the recommended model name for the given AI category,
    /// or null if no recommendation is available.
    /// </summary>
    /// <param name="category">AI category from <see cref="AiCapability"/> constants.</param>
    string? GetRecommendedModel(string category);
}
